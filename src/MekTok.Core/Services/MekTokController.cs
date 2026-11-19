using System.Collections.Concurrent;
using System.Diagnostics;
using MekTok.Core.Audio;
using MekTok.Core.Config;
using MekTok.Core.Keyboard;
using MekTok.Core.Packs;
using MekTok.Core.Platform;

namespace MekTok.Core.Services;

public sealed record ProfileChoiceItem(
    string Id,
    string DisplayName,
    string ShortName,
    string Subtitle,
    bool IsBuiltIn,
    string AccentHex)
{
    public override string ToString() => DisplayName;
}

/// <summary>
/// Cross-platform central coordinator connecting global keyboard input capture, smart auto-pausing,
/// sound resolution, low-latency audio mixing, settings persistence, and UI state notifications.
/// </summary>
public sealed class MekTokController : IDisposable
{
    private readonly SettingsStore _settingsStore;
    private readonly SoundResolver _soundResolver;
    private readonly IAudioOutputEngine _audioEngine;
    private readonly IGlobalKeyboardService _keyboardService;
    private readonly IGlobalHotkeyService _hotkeyService;
    private readonly IActiveWindowMonitor _windowMonitor;
    private readonly IStartupManager _startupManager;
    private readonly Action<Action> _dispatchToUi;
    private readonly KeyRepeatFilter _repeatFilter = new();
    private readonly object _saveSync = new();
    private readonly object _chordSync = new();

    private readonly ConcurrentDictionary<string, AudioClip> _comboClipsCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<int> _currentlyPressedScanCodes = [];
    private readonly HashSet<string> _triggeredCombos = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<int> _comboSuppressedScanCodes = [];

    private CancellationTokenSource? _saveDebounceCts;
    private CancellationTokenSource? _synthDebounceCts;
    private volatile bool _isConfiguratorVisible;
    private bool _disposed;

    public MekTokController(
        SettingsStore settingsStore,
        IAudioOutputEngine audioEngine,
        IGlobalKeyboardService keyboardService,
        IGlobalHotkeyService hotkeyService,
        IActiveWindowMonitor windowMonitor,
        IStartupManager startupManager,
        Action<Action>? dispatchToUi = null)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _audioEngine = audioEngine ?? throw new ArgumentNullException(nameof(audioEngine));
        _keyboardService = keyboardService ?? throw new ArgumentNullException(nameof(keyboardService));
        _hotkeyService = hotkeyService ?? throw new ArgumentNullException(nameof(hotkeyService));
        _windowMonitor = windowMonitor ?? throw new ArgumentNullException(nameof(windowMonitor));
        _startupManager = startupManager ?? throw new ArgumentNullException(nameof(startupManager));
        _dispatchToUi = dispatchToUi ?? (action => action());

        Settings = _settingsStore.Load();
        _soundResolver = new SoundResolver();

        InitializeEngine();
    }

    public AppSettings Settings { get; }
    public SettingsStore Store => _settingsStore;
    public KeyboardAudioMixer Mixer => _audioEngine.Mixer;

    public bool IsEffectivelyActive => Settings.IsEnabled && !_windowMonitor.IsAutoPaused;
    public bool IsAutoPaused => _windowMonitor.IsAutoPaused;
    public string? AutoPauseReason => _windowMonitor.AutoPauseReason;
    public string ActiveAudioDeviceName => _audioEngine.ActiveDeviceName;
    public bool HasActiveAudioOutput => _audioEngine.HasActiveOutput;
    public double? AudioLatencyMs => _audioEngine.EstimatedLatencyMs;
    public bool SupportsPerDeviceKeyboardFiltering => _keyboardService.SupportsPerDeviceFiltering;
    public string? KeyboardPermissionNotice => _keyboardService.PermissionNotice;
    public bool IsActiveWindowMonitorSupported => _windowMonitor.IsSupported;
    public string? LastHotkeyError { get; private set; }

    public event EventHandler? StateChanged;
    public event EventHandler? ProfilesChanged;
    public event EventHandler? AudioDevicesChanged;
    public event EventHandler? KeyboardsChanged;
    public event EventHandler? CombosChanged;
    public event Action<int, bool>? KeyActivityObserved;

    private void InitializeEngine()
    {
        _settingsStore.EnsureDirectories();

        IReadOnlyList<ImportedSoundPack> installedPacks =
            MechvibesPackImporter.LoadInstalledPacks(_settingsStore.PacksDirectory);
        _soundResolver.RegisterImportedPacks(installedPacks);

        // Migrate legacy "smooth-linear" ID cleanly to "mx-red" and fall back if a deleted pack was active.
        Settings.ActiveProfileId = BuiltInProfiles.NormalizeProfileId(Settings.ActiveProfileId);
        if (!BuiltInProfiles.Exists(Settings.ActiveProfileId) &&
            !installedPacks.Any(p => string.Equals(p.Id, Settings.ActiveProfileId, StringComparison.OrdinalIgnoreCase)))
        {
            Settings.ActiveProfileId = BuiltInProfiles.CreamyThockId;
        }

        _soundResolver.RebuildSynthesizedBanks(Settings.Acoustics);
        _audioEngine.Mixer.MasterVolume = Settings.MasterVolume;
        _audioEngine.Start(Settings.OutputDeviceId);

        ReloadComboAudioClips();

        _audioEngine.DevicesChanged += OnAudioDevicesChanged;
        _audioEngine.ActiveDeviceChanged += OnAudioActiveDeviceChanged;

        _keyboardService.KeyEvent += OnRawKeyInput;
        _keyboardService.KeyboardsChanged += OnServiceKeyboardsChanged;
        _keyboardService.Start();

        _hotkeyService.HotkeyPressed += OnGlobalHotkeyPressed;
        (_, LastHotkeyError) = _hotkeyService.ApplyHotkey(Settings.MuteHotkey);

        _windowMonitor.AutoPauseStateChanged += OnAutoPauseStateChanged;
        _windowMonitor.UpdateRules(Settings.ExcludedProcessNames, Settings.MuteInFullscreen);

        // Sync startup state with saved preference cleanly.
        bool startupEnabled = _startupManager.IsEnabled();
        if (Settings.StartWithWindows != startupEnabled)
        {
            Settings.StartWithWindows = startupEnabled;
        }
    }

    private void OnAudioDevicesChanged(object? sender, EventArgs e) =>
        AudioDevicesChanged?.Invoke(this, EventArgs.Empty);

    private void OnAudioActiveDeviceChanged(object? sender, EventArgs e) =>
        StateChanged?.Invoke(this, EventArgs.Empty);

    private void OnServiceKeyboardsChanged(object? sender, EventArgs e) =>
        KeyboardsChanged?.Invoke(this, EventArgs.Empty);

    private void OnGlobalHotkeyPressed(object? sender, EventArgs e) =>
        _dispatchToUi(ToggleEnabled);

    private void OnAutoPauseStateChanged(object? sender, EventArgs e)
    {
        _repeatFilter.Reset();
        lock (_chordSync)
        {
            _currentlyPressedScanCodes.Clear();
            _triggeredCombos.Clear();
            _comboSuppressedScanCodes.Clear();
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void NotifyConfiguratorVisibility(bool visible)
    {
        _isConfiguratorVisible = visible;
    }

    public IReadOnlyList<ProfileChoiceItem> GetAllProfiles()
    {
        var items = new List<ProfileChoiceItem>(BuiltInProfiles.All.Count + _soundResolver.GetImportedPacks().Count);
        foreach (SwitchProfileDefinition def in BuiltInProfiles.All)
        {
            items.Add(new ProfileChoiceItem(def.Id, def.Name, def.ShortName, def.Tagline, IsBuiltIn: true, def.AccentHex));
        }

        foreach (ImportedSoundPack pack in _soundResolver.GetImportedPacks())
        {
            items.Add(new ProfileChoiceItem(
                pack.Id,
                pack.Name,
                pack.Name,
                $"Custom Pack • {pack.PressClips.Count} keys",
                IsBuiltIn: false,
                AccentHex: "#A78BFA"));
        }

        return items;
    }

    /// <summary>
    /// Cleans verbose OS audio endpoint strings into natural user-friendly labels.
    /// </summary>
    public static string FormatFriendlyAudioDeviceName(string? rawName, bool isDefaultPrefix = false)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return isDefaultPrefix ? "System Default Output" : "Audio Output";
        }

        string cleaned = rawName
            .Replace("(R)", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("®", string.Empty, StringComparison.Ordinal)
            .Replace("(TM)", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();

        while (cleaned.Contains("  ", StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace("  ", " ", StringComparison.Ordinal);
        }

        // Convert "Headphones (SOUNDPEATS Space)" -> "SOUNDPEATS Space (Headphones)"
        // or "Speakers (Realtek Audio)" -> "Speakers • Realtek Audio"
        int openParen = cleaned.IndexOf('(');
        int closeParen = cleaned.LastIndexOf(')');
        if (openParen > 0 && closeParen > openParen)
        {
            string kind = cleaned[..openParen].Trim();
            string device = cleaned[(openParen + 1)..closeParen].Trim();
            if (!string.IsNullOrWhiteSpace(device))
            {
                if (kind.Equals("Headphones", StringComparison.OrdinalIgnoreCase) ||
                    kind.Equals("Headset", StringComparison.OrdinalIgnoreCase))
                {
                    cleaned = $"{device} ({kind})";
                }
                else
                {
                    cleaned = $"{kind} • {device}";
                }
            }
        }

        return isDefaultPrefix ? $"System Default • {cleaned}" : cleaned;
    }

    public IReadOnlyList<ImportedSoundPack> GetImportedPacks() => _soundResolver.GetImportedPacks();

    public IReadOnlyList<AudioOutputDeviceInfo> GetOutputDevices() => _audioEngine.ListOutputDevices();

    public IReadOnlyList<KeyboardDeviceInfo> GetConnectedKeyboards() => _keyboardService.GetConnectedKeyboards();

    public IReadOnlyList<string> GetRunningUserProcesses() => _windowMonitor.GetRunningUserProcesses();

    public void ToggleEnabled() => SetEnabled(!Settings.IsEnabled);

    public void SetEnabled(bool enabled)
    {
        if (Settings.IsEnabled == enabled) return;
        Settings.IsEnabled = enabled;
        _repeatFilter.Reset();
        if (!enabled)
        {
            _audioEngine.Mixer.ClearAllVoices();
        }
        SaveDebounced();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetMasterVolume(float volume)
    {
        Settings.MasterVolume = Math.Clamp(volume, 0f, 1f);
        _audioEngine.Mixer.MasterVolume = Settings.MasterVolume;
        SaveDebounced();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SelectProfile(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId)) return;
        Settings.ActiveProfileId = profileId;
        SaveDebounced();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SelectOutputDevice(string? deviceId)
    {
        Settings.OutputDeviceId = string.IsNullOrWhiteSpace(deviceId) ? null : deviceId;
        _audioEngine.SetPreferredDevice(Settings.OutputDeviceId);
        SaveDebounced();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateAcoustics(Action<AcousticSettings> mutator, bool rebuildSynthBanks)
    {
        mutator(Settings.Acoustics);
        Settings.Acoustics.Normalize();
        SaveDebounced();

        if (rebuildSynthBanks)
        {
            ScheduleSynthRebuild();
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public SoundOverride? GetKeyOverride(int scanCode)
    {
        if (!Settings.ProfileCustomizations.TryGetValue(Settings.ActiveProfileId, out ProfileCustomization? cust))
        {
            return null;
        }
        return cust.KeyOverrides.TryGetValue(scanCode, out SoundOverride? ov) ? ov : null;
    }

    public SoundOverride? GetGroupOverride(KeyGroup group)
    {
        if (!Settings.ProfileCustomizations.TryGetValue(Settings.ActiveProfileId, out ProfileCustomization? cust))
        {
            return null;
        }
        return cust.GroupOverrides.TryGetValue(group, out SoundOverride? ov) ? ov : null;
    }

    public bool HasKeyOverride(int scanCode) => GetKeyOverride(scanCode) is { IsEmpty: false };

    public bool IsKeyEffectivelyMuted(int scanCode, KeyGroup group)
    {
        SoundOverride? k = GetKeyOverride(scanCode);
        if (k is not null) return k.IsMuted;
        SoundOverride? g = GetGroupOverride(group);
        return g?.IsMuted ?? false;
    }

    public void SetKeyOverride(int scanCode, SoundOverride? soundOverride)
    {
        ProfileCustomization cust = Settings.GetOrCreateCustomization(Settings.ActiveProfileId);
        if (soundOverride is null)
        {
            cust.KeyOverrides.Remove(scanCode);
        }
        else
        {
            SoundOverride copy = soundOverride.Clone();
            copy.Normalize();
            if (copy.IsEmpty) cust.KeyOverrides.Remove(scanCode);
            else cust.KeyOverrides[scanCode] = copy;
        }

        SaveDebounced();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetGroupOverride(KeyGroup group, SoundOverride? soundOverride)
    {
        ProfileCustomization cust = Settings.GetOrCreateCustomization(Settings.ActiveProfileId);
        if (soundOverride is null)
        {
            cust.GroupOverrides.Remove(group);
        }
        else
        {
            SoundOverride copy = soundOverride.Clone();
            copy.Normalize();
            if (copy.IsEmpty) cust.GroupOverrides.Remove(group);
            else cust.GroupOverrides[group] = copy;
        }

        SaveDebounced();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ResetActiveProfileCustomizations()
    {
        Settings.ProfileCustomizations.Remove(Settings.ActiveProfileId);
        SaveDebounced();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public string CopyCustomAudioToStore(string sourcePath)
    {
        // Decode first to validate file format and size before copying into user storage.
        _ = AudioLoader.LoadClip(sourcePath);

        _settingsStore.EnsureDirectories();
        string safeName = SanitizeFileName(Path.GetFileNameWithoutExtension(sourcePath));
        string ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        string destFileName = $"{safeName}_{Guid.NewGuid():N}{ext}";
        string destPath = Path.Combine(_settingsStore.CustomAudioDirectory, destFileName);
        File.Copy(sourcePath, destPath, overwrite: true);
        _soundResolver.InvalidateCustomAudioCache(destPath);
        return destPath;
    }

    public async Task<ImportedSoundPack> ImportMechvibesFolderAsync(string folderPath)
    {
        ImportedSoundPack pack = await MechvibesPackImporter.ImportFromFolderAsync(
            folderPath,
            _settingsStore.PacksDirectory);
        _soundResolver.AddOrUpdatePack(pack);
        Settings.ActiveProfileId = pack.Id;
        SaveImmediate();
        ProfilesChanged?.Invoke(this, EventArgs.Empty);
        StateChanged?.Invoke(this, EventArgs.Empty);
        return pack;
    }

    public async Task<ImportedSoundPack> ImportMechvibesZipAsync(string zipPath)
    {
        ImportedSoundPack pack = await MechvibesPackImporter.ImportFromZipAsync(
            zipPath,
            _settingsStore.PacksDirectory);
        _soundResolver.AddOrUpdatePack(pack);
        Settings.ActiveProfileId = pack.Id;
        SaveImmediate();
        ProfilesChanged?.Invoke(this, EventArgs.Empty);
        StateChanged?.Invoke(this, EventArgs.Empty);
        return pack;
    }

    public void DeleteImportedPack(ImportedSoundPack pack)
    {
        _soundResolver.RemovePack(pack.Id);
        MechvibesPackImporter.DeletePackDirectory(pack);
        if (string.Equals(Settings.ActiveProfileId, pack.Id, StringComparison.OrdinalIgnoreCase))
        {
            Settings.ActiveProfileId = BuiltInProfiles.CreamyThockId;
        }
        SaveImmediate();
        ProfilesChanged?.Invoke(this, EventArgs.Empty);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void AuditionKey(int scanCode)
    {
        ResolvedKeySound? press = _soundResolver.Resolve(scanCode, isKeyUp: false, Settings);
        if (press is not null)
        {
            _audioEngine.Mixer.Trigger(press);
        }

        if (Settings.Acoustics.EnableKeyUpSound)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(70);
                ResolvedKeySound? release = _soundResolver.Resolve(scanCode, isKeyUp: true, Settings);
                if (release is not null)
                {
                    _audioEngine.Mixer.Trigger(release);
                }
            });
        }
    }

    public void AuditionProfile(string profileId)
    {
        string previous = Settings.ActiveProfileId;
        Settings.ActiveProfileId = profileId;
        try
        {
            // Play 3 crisp test keystrokes (A, S, Space) so user hears alphas + spacebar character.
            int[] sequence = [0x001E, 0x001F, 0x0039];
            for (int i = 0; i < sequence.Length; i++)
            {
                int scan = sequence[i];
                ResolvedKeySound? press = _soundResolver.Resolve(scan, isKeyUp: false, Settings);
                ResolvedKeySound? release = Settings.Acoustics.EnableKeyUpSound
                    ? _soundResolver.Resolve(scan, isKeyUp: true, Settings)
                    : null;

                int delayMs = i * 95;
                if (delayMs == 0)
                {
                    if (press is not null) _audioEngine.Mixer.Trigger(press);
                    if (release is not null)
                    {
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(55);
                            _audioEngine.Mixer.Trigger(release);
                        });
                    }
                }
                else
                {
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(delayMs);
                        if (press is not null) _audioEngine.Mixer.Trigger(press);
                        if (release is not null)
                        {
                            await Task.Delay(55);
                            _audioEngine.Mixer.Trigger(release);
                        }
                    });
                }
            }
        }
        finally
        {
            Settings.ActiveProfileId = previous;
        }
    }

    public (bool Success, string? ErrorMessage) SetMuteHotkey(HotkeyConfig newConfig)
    {
        HotkeyConfig previous = Settings.MuteHotkey;
        newConfig.Normalize();
        Settings.MuteHotkey = newConfig;
        (bool ok, string? err) = _hotkeyService.ApplyHotkey(newConfig);
        if (!ok)
        {
            // Keep UI feedback clear; still restore previous active hotkey if new one conflicts.
            Settings.MuteHotkey = previous;
            _hotkeyService.ApplyHotkey(previous);
            LastHotkeyError = err;
            StateChanged?.Invoke(this, EventArgs.Empty);
            return (false, err);
        }

        LastHotkeyError = null;
        SaveDebounced();
        StateChanged?.Invoke(this, EventArgs.Empty);
        return (true, null);
    }

    public void SetMuteInFullscreen(bool mute)
    {
        Settings.MuteInFullscreen = mute;
        _windowMonitor.UpdateRules(Settings.ExcludedProcessNames, Settings.MuteInFullscreen);
        SaveDebounced();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void AddExcludedProcess(string processName)
    {
        string normalized = AppSettings.NormalizeProcessName(processName);
        if (string.IsNullOrWhiteSpace(normalized)) return;

        if (!Settings.ExcludedProcessNames.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            Settings.ExcludedProcessNames.Add(normalized);
            _windowMonitor.UpdateRules(Settings.ExcludedProcessNames, Settings.MuteInFullscreen);
            SaveDebounced();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void RemoveExcludedProcess(string processName)
    {
        string normalized = AppSettings.NormalizeProcessName(processName);
        if (Settings.ExcludedProcessNames.RemoveAll(
                p => string.Equals(p, normalized, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            _windowMonitor.UpdateRules(Settings.ExcludedProcessNames, Settings.MuteInFullscreen);
            SaveDebounced();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetKeyboardEnabled(string deviceId, bool enabled)
    {
        if (enabled)
        {
            Settings.DisabledKeyboardDeviceIds.Remove(deviceId);
        }
        else
        {
            Settings.DisabledKeyboardDeviceIds.Add(deviceId);
        }
        SaveDebounced();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetStartWithWindows(bool enabled)
    {
        _startupManager.SetEnabled(enabled);
        Settings.StartWithWindows = enabled;
        SaveDebounced();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetStartOnLogin(bool enabled) => SetStartWithWindows(enabled);

    public void SetHideNumpad(bool hide)
    {
        Settings.HideNumpadInConfigurator = hide;
        SaveDebounced();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetTheme(ThemePreference theme)
    {
        Settings.Theme = theme;
        SaveDebounced();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void OpenDataDirectory()
    {
        _settingsStore.EnsureDirectories();
        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _settingsStore.RootDirectory,
                UseShellExecute = true
            });
        }
        else if (OperatingSystem.IsMacOS())
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "open",
                ArgumentList = { _settingsStore.RootDirectory },
                UseShellExecute = false
            });
        }
        else
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "xdg-open",
                ArgumentList = { _settingsStore.RootDirectory },
                UseShellExecute = false
            });
        }
    }

    public void OpenDataDirectoryInExplorer() => OpenDataDirectory();

    private void OnRawKeyInput(RawKeyInputEvent ev)
    {
        if (Settings.DisabledKeyboardDeviceIds.Contains(ev.DeviceId))
        {
            return;
        }

        if (_isConfiguratorVisible)
        {
            _dispatchToUi(() => KeyActivityObserved?.Invoke(ev.CanonicalScanCode, ev.IsKeyUp));
        }

        bool suppressSwitchSound = false;

        lock (_chordSync)
        {
            if (!ev.IsKeyUp)
            {
                _currentlyPressedScanCodes.Add(ev.CanonicalScanCode);

                if (Settings.IsEnabled && !_windowMonitor.IsAutoPaused)
                {
                    foreach (CustomKeyCombo combo in Settings.CustomCombos)
                    {
                        if (!combo.IsEnabled || combo.ScanCodes.Count == 0)
                        {
                            continue;
                        }

                        if (combo.ScanCodes.Contains(ev.CanonicalScanCode) &&
                            combo.ScanCodes.All(_currentlyPressedScanCodes.Contains))
                        {
                            if (_triggeredCombos.Add(combo.Id))
                            {
                                if (_comboClipsCache.TryGetValue(combo.Id, out AudioClip? clip))
                                {
                                    _audioEngine.Mixer.TriggerClip(clip, combo.Volume * Settings.MasterVolume);
                                }

                                if (combo.SuppressSwitchSound)
                                {
                                    suppressSwitchSound = true;
                                    foreach (int sc in combo.ScanCodes)
                                    {
                                        _comboSuppressedScanCodes.Add(sc);
                                    }
                                }
                            }
                            else if (combo.SuppressSwitchSound)
                            {
                                suppressSwitchSound = true;
                            }
                        }
                    }
                }
            }
            else
            {
                _currentlyPressedScanCodes.Remove(ev.CanonicalScanCode);

                foreach (CustomKeyCombo combo in Settings.CustomCombos)
                {
                    if (_triggeredCombos.Contains(combo.Id) &&
                        !combo.ScanCodes.All(_currentlyPressedScanCodes.Contains))
                    {
                        _triggeredCombos.Remove(combo.Id);
                    }
                }

                if (_comboSuppressedScanCodes.Remove(ev.CanonicalScanCode))
                {
                    suppressSwitchSound = true;
                }
            }
        }

        if (suppressSwitchSound)
        {
            return;
        }

        if (!Settings.IsEnabled || _windowMonitor.IsAutoPaused)
        {
            return;
        }

        if (!_repeatFilter.ShouldTrigger(
                ev.CanonicalScanCode,
                ev.IsKeyUp,
                Settings.Acoustics.SuppressHoldRepeat,
                Settings.Acoustics.AllowEditingKeyRepeat))
        {
            return;
        }

        if (ev.IsKeyUp && !Settings.Acoustics.EnableKeyUpSound)
        {
            return;
        }

        ResolvedKeySound? resolved = _soundResolver.Resolve(ev.CanonicalScanCode, ev.IsKeyUp, Settings);
        if (resolved is not null)
        {
            _audioEngine.Mixer.Trigger(resolved);
        }
    }

    public void ReloadComboAudioClips()
    {
        _comboClipsCache.Clear();
        foreach (CustomKeyCombo combo in Settings.CustomCombos)
        {
            if (string.IsNullOrWhiteSpace(combo.AudioFilePath) || !File.Exists(combo.AudioFilePath))
            {
                continue;
            }

            try
            {
                AudioClip clip = AudioLoader.LoadClip(combo.AudioFilePath, maxDurationMs: 20000);
                _comboClipsCache[combo.Id] = clip;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load combo clip '{combo.Name}': {ex.Message}");
            }
        }
    }

    public void AddCustomCombo(CustomKeyCombo combo)
    {
        ArgumentNullException.ThrowIfNull(combo);
        combo.Normalize();
        Settings.CustomCombos.Add(combo);
        ReloadComboAudioClips();
        SaveDebounced();
        CombosChanged?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateCustomCombo(CustomKeyCombo combo)
    {
        ArgumentNullException.ThrowIfNull(combo);
        combo.Normalize();
        int index = Settings.CustomCombos.FindIndex(c => string.Equals(c.Id, combo.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            Settings.CustomCombos[index] = combo;
        }
        else
        {
            Settings.CustomCombos.Add(combo);
        }
        ReloadComboAudioClips();
        SaveDebounced();
        CombosChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveCustomCombo(string comboId)
    {
        if (Settings.CustomCombos.RemoveAll(c => string.Equals(c.Id, comboId, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            _comboClipsCache.TryRemove(comboId, out _);
            SaveDebounced();
            CombosChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void TestPlayCustomCombo(CustomKeyCombo combo)
    {
        ArgumentNullException.ThrowIfNull(combo);
        if (!_comboClipsCache.TryGetValue(combo.Id, out AudioClip? clip))
        {
            if (!string.IsNullOrWhiteSpace(combo.AudioFilePath) && File.Exists(combo.AudioFilePath))
            {
                try
                {
                    clip = AudioLoader.LoadClip(combo.AudioFilePath, maxDurationMs: 20000);
                    _comboClipsCache[combo.Id] = clip;
                }
                catch
                {
                    return;
                }
            }
            else
            {
                return;
            }
        }

        _audioEngine.Mixer.TriggerClip(clip, combo.Volume * Settings.MasterVolume);
    }

    public string ImportComboAudioFile(string sourceFilePath)
    {
        return _settingsStore.CopyComboAudioFile(sourceFilePath);
    }

    private void ScheduleSynthRebuild()
    {
        _synthDebounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _synthDebounceCts = cts;
        AcousticSettings snapshot = Settings.Acoustics;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(75, cts.Token);
                _soundResolver.RebuildSynthesizedBanks(snapshot);
            }
            catch (OperationCanceledException)
            {
                // Superseded by newer slider change.
            }
        });
    }

    private void SaveDebounced()
    {
        lock (_saveSync)
        {
            _saveDebounceCts?.Cancel();
            var cts = new CancellationTokenSource();
            _saveDebounceCts = cts;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(250, cts.Token);
                    _settingsStore.Save(Settings);
                }
                catch (OperationCanceledException)
                {
                    // Superseded by newer change.
                }
            });
        }
    }

    private void SaveImmediate()
    {
        lock (_saveSync)
        {
            _saveDebounceCts?.Cancel();
            _settingsStore.Save(Settings);
        }
    }

    private static string SanitizeFileName(string raw)
    {
        var chars = raw.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        string cleaned = new string(chars).Trim('_');
        return string.IsNullOrWhiteSpace(cleaned) ? "custom" : cleaned[..Math.Min(cleaned.Length, 32)];
    }

    public static bool RunCoreSelfTest(out string summary)
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "MekTok_SelfTest_" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SettingsStore(tempRoot);
            AppSettings cfg = store.Load();
            store.Save(cfg);
            AppSettings reloaded = store.Load();

            int bankCount = 0;
            foreach (SwitchProfileDefinition profile in BuiltInProfiles.All)
            {
                SynthesizedProfileBank bank = SwitchSynthesizer.SynthesizeBank(profile, reloaded.Acoustics);
                AudioClip space = bank.GetClip(KeyGroup.Spacebar, isKeyUp: false, 0);
                if (space.Samples.Length == 0 || space.Samples.Any(s => !float.IsFinite(s)))
                {
                    throw new InvalidOperationException($"Invalid synthesized audio for {profile.Id}.");
                }
                bankCount++;
            }

            var sampleChoice = new ProfileChoiceItem(
                BuiltInProfiles.MxRedId,
                "MX Red — Light Linear",
                "Red",
                "Light Linear",
                true,
                "#E53935");
            if (sampleChoice.ToString().Contains('{') || sampleChoice.ToString().Contains("Id ="))
            {
                throw new InvalidOperationException("ProfileChoiceItem.ToString leaked internal record metadata.");
            }

            summary = $"Core OK ({bankCount} switch banks synthesized)";
            return true;
        }
        catch (Exception ex)
        {
            summary = $"Core self-test failed: {ex.Message}";
            return false;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
            catch
            {
                // Best-effort temp cleanup.
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _synthDebounceCts?.Cancel();
        SaveImmediate();

        _audioEngine.DevicesChanged -= OnAudioDevicesChanged;
        _audioEngine.ActiveDeviceChanged -= OnAudioActiveDeviceChanged;
        _keyboardService.KeyEvent -= OnRawKeyInput;
        _keyboardService.KeyboardsChanged -= OnServiceKeyboardsChanged;
        _hotkeyService.HotkeyPressed -= OnGlobalHotkeyPressed;
        _windowMonitor.AutoPauseStateChanged -= OnAutoPauseStateChanged;

        _windowMonitor.Dispose();
        _hotkeyService.Dispose();
        _keyboardService.Dispose();
        _audioEngine.Dispose();
    }
}


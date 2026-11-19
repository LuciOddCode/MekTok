using System.Diagnostics;
using System.IO;
using System.Windows;
using MekTok.App.Native;
using MekTok.App.UI;
using MekTok.Core.Audio;
using MekTok.Core.Config;
using MekTok.Core.Keyboard;
using MekTok.Core.Packs;

namespace MekTok.App.Services;

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
/// Central coordinator connecting Raw Input capture, smart pausing, sound resolution,
/// WASAPI low-latency playback, settings persistence, and UI state notifications.
/// </summary>
public sealed class MekTokController : IDisposable
{
    private readonly SettingsStore _settingsStore;
    private readonly SoundResolver _soundResolver;
    private readonly WasapiAudioEngine _audioEngine;
    private readonly RawInputKeyboardService _rawInput;
    private readonly GlobalHotkeyService _hotkeyService;
    private readonly ActiveWindowMonitor _windowMonitor;
    private readonly WindowsThemeService _themeService;
    private readonly KeyRepeatFilter _repeatFilter = new();
    private readonly object _saveSync = new();

    private CancellationTokenSource? _saveDebounceCts;
    private CancellationTokenSource? _synthDebounceCts;
    private volatile bool _isConfiguratorVisible;
    private bool _disposed;

    public MekTokController()
    {
        _settingsStore = new SettingsStore();
        Settings = _settingsStore.Load();

        _soundResolver = new SoundResolver();
        _audioEngine = new WasapiAudioEngine();
        _rawInput = new RawInputKeyboardService();
        _hotkeyService = new GlobalHotkeyService(_rawInput);
        _windowMonitor = new ActiveWindowMonitor();
        _themeService = new WindowsThemeService();

        InitializeEngine();
    }

    public AppSettings Settings { get; }
    public SettingsStore Store => _settingsStore;
    public WindowsThemeService ThemeService => _themeService;

    public bool IsEffectivelyActive => Settings.IsEnabled && !_windowMonitor.IsAutoPaused;
    public bool IsAutoPaused => _windowMonitor.IsAutoPaused;
    public string? AutoPauseReason => _windowMonitor.AutoPauseReason;
    public string ActiveAudioDeviceName => _audioEngine.ActiveDeviceName;
    public bool HasActiveAudioOutput => _audioEngine.HasActiveOutput;
    public string? LastHotkeyError { get; private set; }

    public event EventHandler? StateChanged;
    public event EventHandler? ProfilesChanged;
    public event EventHandler? AudioDevicesChanged;
    public event EventHandler? KeyboardsChanged;
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

        _audioEngine.DevicesChanged += (_, _) => AudioDevicesChanged?.Invoke(this, EventArgs.Empty);
        _audioEngine.ActiveDeviceChanged += (_, _) => StateChanged?.Invoke(this, EventArgs.Empty);

        _rawInput.KeyEvent += OnRawKeyInput;
        _rawInput.KeyboardsChanged += (_, _) => KeyboardsChanged?.Invoke(this, EventArgs.Empty);

        _hotkeyService.HotkeyPressed += (_, _) =>
        {
            Application.Current?.Dispatcher.BeginInvoke(() => ToggleEnabled());
        };
        (_, LastHotkeyError) = _hotkeyService.ApplyHotkey(Settings.MuteHotkey);

        _windowMonitor.AutoPauseStateChanged += (_, _) =>
        {
            _repeatFilter.Reset();
            StateChanged?.Invoke(this, EventArgs.Empty);
        };
        _windowMonitor.UpdateRules(Settings.ExcludedProcessNames, Settings.MuteInFullscreen);

        // Sync startup registry state with saved preference cleanly.
        if (Settings.StartWithWindows != StartupManager.IsEnabled())
        {
            Settings.StartWithWindows = StartupManager.IsEnabled();
        }
    }

    public void ApplyInitialTheme() => _themeService.ApplyTheme(Settings.Theme);

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
    /// Cleans verbose Windows WASAPI endpoint strings into natural user-friendly labels.
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

    public IReadOnlyList<KeyboardDeviceInfo> GetConnectedKeyboards() => _rawInput.GetConnectedKeyboards();

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
        // Decode first to validate file format and size before copying into AppData.
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
        StartupManager.SetEnabled(enabled);
        Settings.StartWithWindows = enabled;
        SaveDebounced();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetHideNumpad(bool hide)
    {
        Settings.HideNumpadInConfigurator = hide;
        SaveDebounced();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetTheme(ThemePreference theme)
    {
        Settings.Theme = theme;
        _themeService.ApplyTheme(theme);
        SaveDebounced();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void OpenDataDirectoryInExplorer()
    {
        _settingsStore.EnsureDirectories();
        Process.Start(new ProcessStartInfo
        {
            FileName = _settingsStore.RootDirectory,
            UseShellExecute = true
        });
    }

    private void OnRawKeyInput(RawKeyInputEvent ev)
    {
        if (Settings.DisabledKeyboardDeviceIds.Contains(ev.DeviceId))
        {
            return;
        }

        if (_isConfiguratorVisible)
        {
            Application.Current?.Dispatcher.BeginInvoke(
                () => KeyActivityObserved?.Invoke(ev.CanonicalScanCode, ev.IsKeyUp));
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

    public static bool RunHeadlessSelfTest(out string summary)
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

            using var audioEngine = new WasapiAudioEngine();
            IReadOnlyList<AudioOutputDeviceInfo> devices = audioEngine.ListOutputDevices();

            using var rawInput = new RawInputKeyboardService();
            IReadOnlyList<KeyboardDeviceInfo> keyboards = rawInput.GetConnectedKeyboards();

            using var activeIcon = IconFactory.CreateTrayIcon(active: true);
            using var mutedIcon = IconFactory.CreateTrayIcon(active: false);
            if (activeIcon.Width <= 0 || mutedIcon.Width <= 0)
            {
                throw new InvalidOperationException("Tray icon generation failed.");
            }

            _ = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Themes/FluentDark.xaml", UriKind.Absolute)
            };
            _ = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Themes/FluentLight.xaml", UriKind.Absolute)
            };
            _ = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Themes/Controls.xaml", UriKind.Absolute)
            };
            var visualKeyboard = new VisualKeyboardControl();
            if (visualKeyboard.KeyCanvas.Children.Count != Ansi104Layout.AllKeys.Count)
            {
                throw new InvalidOperationException("Visual keyboard failed to render all 104 ANSI keys.");
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

            summary =
                $"MekTok self-test passed: {bankCount} switch banks synthesized, " +
                $"{devices.Count} audio output(s), {keyboards.Count} keyboard(s), XAML themes & 104-key UI OK.";
            return true;
        }
        catch (Exception ex)
        {
            summary = $"MekTok self-test failed: {ex.Message}";
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

        _rawInput.KeyEvent -= OnRawKeyInput;
        _windowMonitor.Dispose();
        _hotkeyService.Dispose();
        _rawInput.Dispose();
        _audioEngine.Dispose();
        _themeService.Dispose();
    }
}

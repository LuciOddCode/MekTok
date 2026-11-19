using MekTok.Core.Audio;
using MekTok.Core.Config;
using MekTok.Core.Platform;
using MekTok.Core.Services;
using Xunit;

namespace MekTok.Core.Tests;

public class MekTokControllerTests : IDisposable
{
    private readonly string _tempRoot;

    public MekTokControllerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "MekTok_ControllerTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    private sealed class FakeAudioEngine : IAudioOutputEngine
    {
        public KeyboardAudioMixer Mixer { get; } = new();
        public bool HasActiveOutput => true;
        public string ActiveDeviceName => "Fake Output";
        public double? EstimatedLatencyMs => 12.5;
        public event EventHandler? DevicesChanged;
        public event EventHandler? ActiveDeviceChanged;
        public void Start(string? preferredDeviceId) { }
        public void SetPreferredDevice(string? deviceId) { }
        public IReadOnlyList<AudioOutputDeviceInfo> ListOutputDevices() =>
            [new AudioOutputDeviceInfo("fake-1", "Fake Output", true)];
        public void Dispose()
        {
            _ = DevicesChanged;
            _ = ActiveDeviceChanged;
        }
    }

    private sealed class FakeKeyboardService : IGlobalKeyboardService
    {
        public bool Started { get; private set; }
        public bool SupportsPerDeviceFiltering => true;
        public string? PermissionNotice => null;
        public event Action<RawKeyInputEvent>? KeyEvent;
        public event EventHandler? KeyboardsChanged;
        public void Start() => Started = true;
        public IReadOnlyList<KeyboardDeviceInfo> GetConnectedKeyboards() =>
            [new KeyboardDeviceInfo("kb-1", "Built-in Keyboard")];
        public void EmitKey(int scanCode, bool isKeyUp, string deviceId = "kb-1") =>
            KeyEvent?.Invoke(new RawKeyInputEvent(scanCode, isKeyUp, deviceId));
        public void Dispose() => _ = KeyboardsChanged;
    }

    private sealed class FakeHotkeyService : IGlobalHotkeyService
    {
        public HotkeyConfig? Applied { get; private set; }
        public event EventHandler? HotkeyPressed;
        public (bool Success, string? ErrorMessage) ApplyHotkey(HotkeyConfig config)
        {
            Applied = config;
            return (true, null);
        }
        public void Unregister() { }
        public void Trigger() => HotkeyPressed?.Invoke(this, EventArgs.Empty);
        public void Dispose() { }
    }

    private sealed class FakeWindowMonitor : IActiveWindowMonitor
    {
        private HashSet<string> _excluded = new(StringComparer.OrdinalIgnoreCase);
        public bool IsSupported => true;
        public bool IsAutoPaused { get; private set; }
        public string? AutoPauseReason { get; private set; }
        public event EventHandler? AutoPauseStateChanged;

        public void UpdateRules(IEnumerable<string> excludedProcessNames, bool muteInFullscreen)
        {
            _excluded = new HashSet<string>(excludedProcessNames, StringComparer.OrdinalIgnoreCase);
        }

        public void SimulateForegroundApp(string processName)
        {
            bool pause = _excluded.Contains(processName);
            if (pause != IsAutoPaused)
            {
                IsAutoPaused = pause;
                AutoPauseReason = pause ? $"App: {processName}" : null;
                AutoPauseStateChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public IReadOnlyList<string> GetRunningUserProcesses() => ["code", "firefox"];
        public void Dispose() { }
    }

    private sealed class FakeStartupManager : IStartupManager
    {
        public bool Enabled { get; set; }
        public bool IsEnabled() => Enabled;
        public void SetEnabled(bool enabled) => Enabled = enabled;
    }

    private (MekTokController Controller, FakeAudioEngine Audio, FakeKeyboardService Keyboard, FakeHotkeyService Hotkey, FakeWindowMonitor Monitor, FakeStartupManager Startup) CreateHarness()
    {
        var store = new SettingsStore(_tempRoot);
        var audio = new FakeAudioEngine();
        var keyboard = new FakeKeyboardService();
        var hotkey = new FakeHotkeyService();
        var monitor = new FakeWindowMonitor();
        var startup = new FakeStartupManager();
        var controller = new MekTokController(store, audio, keyboard, hotkey, monitor, startup);
        return (controller, audio, keyboard, hotkey, monitor, startup);
    }

    [Fact]
    public void KeyPress_TriggersAudioUnlessMutedDisabledKeyboardOrAutoPaused()
    {
        (MekTokController controller, FakeAudioEngine audio, FakeKeyboardService keyboard, FakeHotkeyService hotkey, FakeWindowMonitor monitor, _) = CreateHarness();
        using (controller)
        {
            Assert.True(keyboard.Started);
            float[] buf = new float[512];

            // Normal press emits audio
            keyboard.EmitKey(0x001E, isKeyUp: false);
            audio.Mixer.Read(buf.AsSpan());
            Assert.Contains(buf, s => MathF.Abs(s) > 0.001f);
            keyboard.EmitKey(0x001E, isKeyUp: true);
            DrainMixer(audio.Mixer);

            // Global mute hotkey toggles sounds off
            hotkey.Trigger();
            Assert.False(controller.Settings.IsEnabled);
            keyboard.EmitKey(0x001E, isKeyUp: false);
            audio.Mixer.Read(buf.AsSpan());
            Assert.All(buf, s => Assert.Equal(0f, s));
            keyboard.EmitKey(0x001E, isKeyUp: true);

            // Re-enable and verify disabled keyboard device ID is ignored
            hotkey.Trigger();
            Assert.True(controller.Settings.IsEnabled);
            controller.SetKeyboardEnabled("kb-1", false);
            keyboard.EmitKey(0x001E, isKeyUp: false, deviceId: "kb-1");
            audio.Mixer.Read(buf.AsSpan());
            Assert.All(buf, s => Assert.Equal(0f, s));
            controller.SetKeyboardEnabled("kb-1", true);

            // Auto-pause when excluded app is focused suppresses sound
            controller.AddExcludedProcess("valorant");
            monitor.SimulateForegroundApp("valorant");
            Assert.True(controller.IsAutoPaused);
            keyboard.EmitKey(0x001E, isKeyUp: false);
            audio.Mixer.Read(buf.AsSpan());
            Assert.All(buf, s => Assert.Equal(0f, s));
        }
    }

    [Fact]
    public void StartupAndProfileSelection_PersistThroughSettingsStore()
    {
        (MekTokController controller, _, _, _, _, FakeStartupManager startup) = CreateHarness();
        using (controller)
        {
            controller.SetStartOnLogin(true);
            Assert.True(startup.Enabled);

            controller.SelectProfile(BuiltInProfiles.MxRedId);
        }

        var reloaded = new SettingsStore(_tempRoot).Load();
        Assert.Equal(BuiltInProfiles.MxRedId, reloaded.ActiveProfileId);
        Assert.True(reloaded.StartWithWindows);
    }

    private static void DrainMixer(KeyboardAudioMixer mixer)
    {
        float[] tmp = new float[4096];
        for (int i = 0; i < 4; i++)
        {
            mixer.Read(tmp.AsSpan());
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }
}


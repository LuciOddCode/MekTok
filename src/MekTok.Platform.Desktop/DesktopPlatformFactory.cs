using MekTok.Core.Config;
using MekTok.Core.Platform;
using MekTok.Core.Services;
using MekTok.Platform.Desktop.Audio;
using MekTok.Platform.Desktop.Input;
using MekTok.Platform.Desktop.Lifecycle;
using MekTok.Platform.Desktop.Startup;
using MekTok.Platform.Desktop.Windowing;

namespace MekTok.Platform.Desktop;

public sealed class NoOpStartupManager : IStartupManager
{
    public bool IsEnabled() => false;
    public void SetEnabled(bool enabled) { }
}

public static class DesktopPlatformFactory
{
    public static ISingleInstanceGuard CreateSingleInstanceGuard(string? runtimeDirectory = null) =>
        new UnixSingleInstanceGuard(runtimeDirectory);

    public static IAudioOutputEngine CreateAudioEngine() =>
        new MiniAudioOutputEngine();

    public static IGlobalKeyboardService CreateKeyboardService() =>
        new SharpHookKeyboardService();

    public static IGlobalHotkeyService CreateHotkeyService(IGlobalKeyboardService keyboardService) =>
        new KeyboardListenerHotkeyService(keyboardService);

    public static IActiveWindowMonitor CreateActiveWindowMonitor()
    {
        if (OperatingSystem.IsLinux())
        {
            string? sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
            if (string.Equals(sessionType, "wayland", StringComparison.OrdinalIgnoreCase))
            {
                return new NoOpActiveWindowMonitor();
            }

            var x11 = new LinuxX11ActiveWindowMonitor();
            if (x11.IsSupported)
            {
                return x11;
            }
            x11.Dispose();
            return new NoOpActiveWindowMonitor();
        }

        if (OperatingSystem.IsMacOS())
        {
            var mac = new MacOsActiveWindowMonitor();
            if (mac.IsSupported)
            {
                return mac;
            }
            mac.Dispose();
            return new NoOpActiveWindowMonitor();
        }

        return new NoOpActiveWindowMonitor();
    }

    public static IStartupManager CreateStartupManager()
    {
        if (OperatingSystem.IsLinux())
        {
            return new LinuxStartupManager();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacOsStartupManager();
        }

        return new NoOpStartupManager();
    }

    public static MekTokController CreateController(Action<Action>? dispatchToUi = null)
    {
        var store = new SettingsStore();
        IAudioOutputEngine audio = CreateAudioEngine();
        IGlobalKeyboardService keyboard = CreateKeyboardService();
        IGlobalHotkeyService hotkey = CreateHotkeyService(keyboard);
        IActiveWindowMonitor monitor = CreateActiveWindowMonitor();
        IStartupManager startup = CreateStartupManager();

        return new MekTokController(
            store,
            audio,
            keyboard,
            hotkey,
            monitor,
            startup,
            dispatchToUi);
    }
}


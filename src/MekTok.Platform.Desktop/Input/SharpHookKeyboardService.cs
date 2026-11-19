using MekTok.Core.Platform;
using SharpHook;
using SharpHook.Data;
using SharpHook.Providers;

namespace MekTok.Platform.Desktop.Input;

/// <summary>
/// Cross-platform global keyboard listener built on SharpHook (libuiohook).
/// Captures keyboard events globally across X11, macOS (with Input Monitoring permission), and Windows.
/// </summary>
public sealed class SharpHookKeyboardService : IGlobalKeyboardService
{
    private const string DefaultDeviceId = "SHARPHOOK_DEFAULT_KEYBOARD";

    private TaskPoolGlobalHook? _hook;
    private Task? _hookTask;
    private bool _started;
    private bool _disposed;

    public SharpHookKeyboardService()
    {
        PermissionNotice = DetectInitialPermissionNotice();
    }

    public bool SupportsPerDeviceFiltering => false;
    public string? PermissionNotice { get; private set; }

    public event Action<RawKeyInputEvent>? KeyEvent;
    public event EventHandler? KeyboardsChanged;

    public void Start()
    {
        if (_started || _disposed) return;
        _started = true;

        try
        {
            UioHookProvider.Instance.KeyTypedEnabled = false;
            _hook = new TaskPoolGlobalHook(parallelismLevel: 1);
            _hook.KeyPressed += OnKeyPressed;
            _hook.KeyReleased += OnKeyReleased;

            _hookTask = _hook.RunAsync(GlobalHookType.Keyboard);
            _ = _hookTask.ContinueWith(t =>
            {
                if (t.IsFaulted && !_disposed)
                {
                    PermissionNotice = BuildHookFailureNotice(t.Exception?.GetBaseException());
                }
            }, TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            PermissionNotice = BuildHookFailureNotice(ex);
        }
    }

    public IReadOnlyList<KeyboardDeviceInfo> GetConnectedKeyboards() => Array.Empty<KeyboardDeviceInfo>();

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        if (SharpHookKeyMap.TryMapKeyCode(e.Data.KeyCode, out int scanCode))
        {
            KeyEvent?.Invoke(new RawKeyInputEvent(scanCode, IsKeyUp: false, DefaultDeviceId));
        }
    }

    private void OnKeyReleased(object? sender, KeyboardHookEventArgs e)
    {
        if (SharpHookKeyMap.TryMapKeyCode(e.Data.KeyCode, out int scanCode))
        {
            KeyEvent?.Invoke(new RawKeyInputEvent(scanCode, IsKeyUp: true, DefaultDeviceId));
        }
    }

    private static string? DetectInitialPermissionNotice()
    {
        if (OperatingSystem.IsMacOS())
        {
            try
            {
                if (!UioHookProvider.Instance.IsAxApiEnabled(promptUserIfDisabled: false))
                {
                    return "macOS requires Input Monitoring & Accessibility permissions for global key capture. " +
                           "Grant access in System Settings → Privacy & Security → Input Monitoring & Accessibility. " +
                           "Note: Password/Secure Input fields remain silent by OS design.";
                }
            }
            catch
            {
                // Ignore query errors before hook start.
            }

            return "On macOS, global key capture requires System Settings → Privacy & Security → Input Monitoring. " +
                   "Password prompts (Secure Input) stay silent by OS security design.";
        }

        if (OperatingSystem.IsLinux())
        {
            string? sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
            if (string.Equals(sessionType, "wayland", StringComparison.OrdinalIgnoreCase))
            {
                return "Wayland session detected: libuiohook captures XWayland windows by default. " +
                       "Native Wayland surfaces restrict global key snooping without an evdev (input group) backend. " +
                       "Linux Mint Cinnamon (X11) supports full global capture.";
            }

            try
            {
                if (UioHookProvider.Instance.GetLinuxMode() == LinuxMode.Wayland)
                {
                    return "Wayland input backend active; global key capture may be restricted by the compositor.";
                }
            }
            catch
            {
                // Ignore if not applicable.
            }
        }

        return null;
    }

    private static string BuildHookFailureNotice(Exception? ex)
    {
        if (OperatingSystem.IsMacOS())
        {
            return $"Global keyboard hook could not start ({ex?.Message ?? "permission denied"}). " +
                   "Enable MekTok under System Settings → Privacy & Security → Input Monitoring & Accessibility, then restart.";
        }

        if (OperatingSystem.IsLinux())
        {
            return $"Global keyboard hook could not start ({ex?.Message ?? "X11/Wayland error"}). " +
                   "Ensure an X11 DISPLAY session is running and libX11/libXi are installed.";
        }

        return $"Global keyboard hook could not start: {ex?.Message ?? "unknown error"}.";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hook is not null)
        {
            _hook.KeyPressed -= OnKeyPressed;
            _hook.KeyReleased -= OnKeyReleased;
            try
            {
                _hook.Stop();
            }
            catch
            {
                // Ignore errors during shutdown.
            }
            try
            {
                _hook.Dispose();
            }
            catch
            {
                // Ignore disposal exceptions.
            }
            _hook = null;
        }

        _ = KeyboardsChanged;
    }
}


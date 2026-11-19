using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using MekTok.Core.Config;
using MekTok.Core.Platform;

namespace MekTok.App.Native;

/// <summary>
/// Tracks foreground window process and fullscreen state using WinEventHook + periodic fullscreen check
/// so the keystroke hot path reads pre-computed volatile booleans with zero Win32 calls.
/// </summary>
public sealed class ActiveWindowMonitor : IActiveWindowMonitor
{
    private static readonly HashSet<string> DesktopShellClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd"
    };

    private readonly NativeMethods.WinEventDelegate _winEventDelegate;
    private readonly DispatcherTimer _pollTimer;
    private readonly IntPtr _hookHandle;
    private readonly int _ownProcessId = Environment.ProcessId;

    private IReadOnlySet<string> _excludedProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private bool _muteInFullscreen;
    private volatile bool _isAutoPaused;
    private volatile string? _autoPauseReason;
    private bool _disposed;

    public ActiveWindowMonitor()
    {
        _winEventDelegate = OnWinEvent;
        _hookHandle = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero,
            _winEventDelegate,
            0,
            0,
            NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);

        _pollTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(1500)
        };
        _pollTimer.Tick += (_, _) => EvaluateCurrentWindow();
        _pollTimer.Start();
    }

    public bool IsSupported => true;
    public bool IsAutoPaused => _isAutoPaused;
    public string? AutoPauseReason => _autoPauseReason;

    public event EventHandler? AutoPauseStateChanged;

    public void UpdateRules(IEnumerable<string> excludedProcessNames, bool muteInFullscreen)
    {
        _excludedProcesses = new HashSet<string>(
            excludedProcessNames.Select(AppSettings.NormalizeProcessName),
            StringComparer.OrdinalIgnoreCase);
        _muteInFullscreen = muteInFullscreen;
        EvaluateCurrentWindow();
    }

    IReadOnlyList<string> IActiveWindowMonitor.GetRunningUserProcesses() => GetRunningUserProcesses();

    public static IReadOnlyList<string> GetRunningUserProcesses()
    {
        int ownPid = Environment.ProcessId;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Process p in Process.GetProcesses())
        {
            try
            {
                if (p.Id == ownPid || p.MainWindowHandle == IntPtr.Zero) continue;
                string name = AppSettings.NormalizeProcessName(p.ProcessName);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    names.Add(name);
                }
            }
            catch
            {
                // Skip protected processes cleanly.
            }
            finally
            {
                p.Dispose();
            }
        }

        return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private void OnWinEvent(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        EvaluateWindow(hwnd);
    }

    private void EvaluateCurrentWindow()
    {
        if (_disposed) return;
        EvaluateWindow(NativeMethods.GetForegroundWindow());
    }

    private void EvaluateWindow(IntPtr hwnd)
    {
        bool paused = false;
        string? reason = null;

        if (hwnd != IntPtr.Zero && (_excludedProcesses.Count > 0 || _muteInFullscreen))
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid != 0 && pid != _ownProcessId)
            {
                string? procName = TryGetProcessName(pid);
                if (!string.IsNullOrWhiteSpace(procName) && _excludedProcesses.Contains(procName))
                {
                    paused = true;
                    reason = $"App: {procName}";
                }
                else if (_muteInFullscreen && IsWindowFullscreen(hwnd))
                {
                    paused = true;
                    reason = string.IsNullOrWhiteSpace(procName)
                        ? "Fullscreen"
                        : $"Fullscreen ({procName})";
                }
            }
        }

        if (paused != _isAutoPaused || !string.Equals(reason, _autoPauseReason, StringComparison.Ordinal))
        {
            _isAutoPaused = paused;
            _autoPauseReason = reason;
            AutoPauseStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static string? TryGetProcessName(uint pid)
    {
        IntPtr handle = NativeMethods.OpenProcess(
            NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION,
            false,
            pid);

        if (handle == IntPtr.Zero) return null;

        try
        {
            var sb = new StringBuilder(512);
            uint size = (uint)sb.Capacity;
            if (!NativeMethods.QueryFullProcessImageName(handle, 0, sb, ref size))
            {
                return null;
            }

            string fileName = Path.GetFileNameWithoutExtension(sb.ToString());
            return string.IsNullOrWhiteSpace(fileName) ? null : AppSettings.NormalizeProcessName(fileName);
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    private static bool IsWindowFullscreen(IntPtr hwnd)
    {
        var classSb = new StringBuilder(128);
        if (NativeMethods.GetClassName(hwnd, classSb, classSb.Capacity) > 0)
        {
            if (DesktopShellClasses.Contains(classSb.ToString()))
            {
                return false;
            }
        }

        if (NativeMethods.SHQueryUserNotificationState(out var state) == 0)
        {
            if (state is NativeMethods.QUERY_USER_NOTIFICATION_STATE.QUNS_BUSY
                or NativeMethods.QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN)
            {
                return true;
            }
        }

        if (!NativeMethods.GetWindowRect(hwnd, out var winRect))
        {
            return false;
        }

        IntPtr monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var info = new NativeMethods.MONITORINFO
        {
            cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>()
        };

        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return false;
        }

        const int tolerance = 2;
        return winRect.Left <= info.rcMonitor.Left + tolerance
            && winRect.Top <= info.rcMonitor.Top + tolerance
            && winRect.Right >= info.rcMonitor.Right - tolerance
            && winRect.Bottom >= info.rcMonitor.Bottom - tolerance;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _pollTimer.Stop();
        if (_hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_hookHandle);
        }
    }
}

using System.Runtime.InteropServices;
using MekTok.Core.Config;
using MekTok.Core.Platform;

namespace MekTok.Platform.Desktop.Windowing;

/// <summary>
/// Tracks foreground window process and fullscreen state on Linux X11 desktops (e.g. Linux Mint Cinnamon)
/// using EWMH properties (_NET_ACTIVE_WINDOW, _NET_WM_PID, _NET_WM_STATE_FULLSCREEN, _NET_CLIENT_LIST).
/// </summary>
public sealed class LinuxX11ActiveWindowMonitor : IActiveWindowMonitor
{
    private const int AnyPropertyType = 0;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int XErrorHandlerDelegate(IntPtr display, IntPtr errorEvent);

    private static readonly XErrorHandlerDelegate SafeErrorHandler = (_, _) => 0;

    [DllImport("libX11.so.6", EntryPoint = "XOpenDisplay")]
    private static extern IntPtr XOpenDisplay(IntPtr displayName);

    [DllImport("libX11.so.6", EntryPoint = "XCloseDisplay")]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport("libX11.so.6", EntryPoint = "XDefaultRootWindow")]
    private static extern IntPtr XDefaultRootWindow(IntPtr display);

    [DllImport("libX11.so.6", EntryPoint = "XInternAtom", CharSet = CharSet.Ansi)]
    private static extern IntPtr XInternAtom(
        IntPtr display,
        string atomName,
        [MarshalAs(UnmanagedType.Bool)] bool onlyIfExists);

    [DllImport("libX11.so.6", EntryPoint = "XGetWindowProperty")]
    private static extern int XGetWindowProperty(
        IntPtr display,
        IntPtr window,
        IntPtr property,
        nint longOffset,
        nint longLength,
        [MarshalAs(UnmanagedType.Bool)] bool delete,
        IntPtr reqType,
        out IntPtr actualTypeReturn,
        out int actualFormatReturn,
        out nuint nitemsReturn,
        out nuint bytesAfterReturn,
        out IntPtr propReturn);

    [DllImport("libX11.so.6", EntryPoint = "XFree")]
    private static extern int XFree(IntPtr data);

    [DllImport("libX11.so.6", EntryPoint = "XSetErrorHandler")]
    private static extern IntPtr XSetErrorHandler(XErrorHandlerDelegate handler);

    private readonly object _x11Sync = new();
    private readonly int _ownPid = Environment.ProcessId;
    private readonly CancellationTokenSource _cts = new();

    private IntPtr _display;
    private IntPtr _rootWindow;
    private IntPtr _atomActiveWindow;
    private IntPtr _atomWmPid;
    private IntPtr _atomWmState;
    private IntPtr _atomWmStateFullscreen;
    private IntPtr _atomClientList;
    private IntPtr _atomWindow;
    private IntPtr _atomCardinal;
    private IntPtr _atomAtom;

    private IReadOnlySet<string> _excludedProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private bool _muteInFullscreen;
    private volatile bool _isAutoPaused;
    private volatile string? _autoPauseReason;
    private bool _disposed;

    public LinuxX11ActiveWindowMonitor()
    {
        if (!OperatingSystem.IsLinux() ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")))
        {
            return;
        }

        try
        {
            XSetErrorHandler(SafeErrorHandler);
            _display = XOpenDisplay(IntPtr.Zero);
            if (_display == IntPtr.Zero)
            {
                return;
            }

            _rootWindow = XDefaultRootWindow(_display);
            _atomActiveWindow = XInternAtom(_display, "_NET_ACTIVE_WINDOW", false);
            _atomWmPid = XInternAtom(_display, "_NET_WM_PID", false);
            _atomWmState = XInternAtom(_display, "_NET_WM_STATE", false);
            _atomWmStateFullscreen = XInternAtom(_display, "_NET_WM_STATE_FULLSCREEN", false);
            _atomClientList = XInternAtom(_display, "_NET_CLIENT_LIST", false);
            _atomWindow = XInternAtom(_display, "WINDOW", false);
            _atomCardinal = XInternAtom(_display, "CARDINAL", false);
            _atomAtom = XInternAtom(_display, "ATOM", false);

            IsSupported = true;
            _ = Task.Run(PollLoopAsync);
        }
        catch
        {
            CloseDisplaySafe();
            IsSupported = false;
        }
    }

    public bool IsSupported { get; private set; }
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

    public IReadOnlyList<string> GetRunningUserProcesses()
    {
        if (!IsSupported || _disposed)
        {
            return Array.Empty<string>();
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        lock (_x11Sync)
        {
            if (_display == IntPtr.Zero || _disposed)
            {
                return Array.Empty<string>();
            }

            foreach (IntPtr win in ReadWindowArrayProperty(_rootWindow, _atomClientList, _atomWindow))
            {
                int pid = ReadCardinalProperty(win, _atomWmPid);
                if (pid <= 0 || pid == _ownPid) continue;

                string? procName = TryReadLinuxProcessName(pid);
                if (!string.IsNullOrWhiteSpace(procName))
                {
                    names.Add(procName);
                }
            }
        }

        return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private async Task PollLoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(750));
        try
        {
            while (await timer.WaitForNextTickAsync(_cts.Token))
            {
                EvaluateCurrentWindow();
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    private void EvaluateCurrentWindow()
    {
        if (!IsSupported || _disposed) return;

        bool paused = false;
        string? reason = null;

        if (_excludedProcesses.Count > 0 || _muteInFullscreen)
        {
            lock (_x11Sync)
            {
                if (_display != IntPtr.Zero && !_disposed)
                {
                    IntPtr activeWin = ReadSingleWindowProperty(_rootWindow, _atomActiveWindow);
                    if (activeWin != IntPtr.Zero)
                    {
                        int pid = ReadCardinalProperty(activeWin, _atomWmPid);
                        if (pid != _ownPid)
                        {
                            string? procName = pid > 0 ? TryReadLinuxProcessName(pid) : null;
                            if (!string.IsNullOrWhiteSpace(procName) && _excludedProcesses.Contains(procName))
                            {
                                paused = true;
                                reason = $"App: {procName}";
                            }
                            else if (_muteInFullscreen && HasAtomInProperty(activeWin, _atomWmState, _atomWmStateFullscreen))
                            {
                                paused = true;
                                reason = string.IsNullOrWhiteSpace(procName)
                                    ? "Fullscreen"
                                    : $"Fullscreen ({procName})";
                            }
                        }
                    }
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

    private IntPtr ReadSingleWindowProperty(IntPtr window, IntPtr atom)
    {
        IntPtr[] windows = ReadWindowArrayProperty(window, atom, _atomWindow);
        return windows.Length > 0 ? windows[0] : IntPtr.Zero;
    }

    private IntPtr[] ReadWindowArrayProperty(IntPtr window, IntPtr atom, IntPtr reqType)
    {
        if (window == IntPtr.Zero || atom == IntPtr.Zero) return Array.Empty<IntPtr>();

        if (XGetWindowProperty(
                _display,
                window,
                atom,
                0,
                1024,
                false,
                reqType == IntPtr.Zero ? (IntPtr)AnyPropertyType : reqType,
                out _,
                out int actualFormat,
                out nuint itemCount,
                out _,
                out IntPtr prop) != 0 || prop == IntPtr.Zero)
        {
            return Array.Empty<IntPtr>();
        }

        try
        {
            if (actualFormat != 32 || itemCount == 0)
            {
                return Array.Empty<IntPtr>();
            }

            int count = (int)Math.Min((ulong)itemCount, 1024);
            var results = new IntPtr[count];
            for (int i = 0; i < count; i++)
            {
                results[i] = Marshal.ReadIntPtr(prop, i * IntPtr.Size);
            }
            return results;
        }
        finally
        {
            XFree(prop);
        }
    }

    private int ReadCardinalProperty(IntPtr window, IntPtr atom)
    {
        if (window == IntPtr.Zero || atom == IntPtr.Zero) return 0;

        if (XGetWindowProperty(
                _display,
                window,
                atom,
                0,
                1,
                false,
                _atomCardinal == IntPtr.Zero ? (IntPtr)AnyPropertyType : _atomCardinal,
                out _,
                out int actualFormat,
                out nuint itemCount,
                out _,
                out IntPtr prop) != 0 || prop == IntPtr.Zero)
        {
            return 0;
        }

        try
        {
            if (actualFormat != 32 || itemCount == 0)
            {
                return 0;
            }

            return (int)Marshal.ReadIntPtr(prop, 0).ToInt64();
        }
        finally
        {
            XFree(prop);
        }
    }

    private bool HasAtomInProperty(IntPtr window, IntPtr propertyAtom, IntPtr targetAtom)
    {
        if (window == IntPtr.Zero || propertyAtom == IntPtr.Zero || targetAtom == IntPtr.Zero)
        {
            return false;
        }

        foreach (IntPtr atom in ReadWindowArrayProperty(window, propertyAtom, _atomAtom))
        {
            if (atom == targetAtom)
            {
                return true;
            }
        }

        return false;
    }

    private static string? TryReadLinuxProcessName(int pid)
    {
        try
        {
            string commPath = $"/proc/{pid}/comm";
            if (File.Exists(commPath))
            {
                string comm = File.ReadAllText(commPath).Trim();
                if (!string.IsNullOrWhiteSpace(comm))
                {
                    return AppSettings.NormalizeProcessName(comm);
                }
            }

            string exePath = $"/proc/{pid}/exe";
            string? resolved = File.ResolveLinkTarget(exePath, returnFinalTarget: false)?.FullName;
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                return AppSettings.NormalizeProcessName(Path.GetFileName(resolved));
            }
        }
        catch
        {
            // Ignore processes that exited or deny access.
        }

        return null;
    }

    private void CloseDisplaySafe()
    {
        lock (_x11Sync)
        {
            if (_display != IntPtr.Zero)
            {
                try
                {
                    XCloseDisplay(_display);
                }
                catch
                {
                    // Best-effort X11 teardown.
                }
                _display = IntPtr.Zero;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        CloseDisplaySafe();
        _cts.Dispose();
    }
}


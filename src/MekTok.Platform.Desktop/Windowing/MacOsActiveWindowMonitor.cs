using System.Diagnostics;
using System.Runtime.InteropServices;
using MekTok.Core.Config;
using MekTok.Core.Platform;

namespace MekTok.Platform.Desktop.Windowing;

/// <summary>
/// Tracks the frontmost macOS application and running regular GUI applications via Objective-C
/// runtime calls to <c>NSWorkspace</c> (requires no special permissions).
/// </summary>
public sealed class MacOsActiveWindowMonitor : IActiveWindowMonitor
{
    private const string ObjCLib = "/usr/lib/libobjc.A.dylib";

    [DllImport(ObjCLib, EntryPoint = "objc_getClass", CharSet = CharSet.Ansi)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(ObjCLib, EntryPoint = "sel_registerName", CharSet = CharSet.Ansi)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend_nint(IntPtr receiver, IntPtr selector);

    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_ptr_nuint(IntPtr receiver, IntPtr selector, nuint index);

    private readonly CancellationTokenSource _cts = new();
    private readonly int _ownPid = Environment.ProcessId;

    private readonly IntPtr _nsWorkspaceClass;
    private readonly IntPtr _selSharedWorkspace;
    private readonly IntPtr _selFrontmostApplication;
    private readonly IntPtr _selRunningApplications;
    private readonly IntPtr _selProcessIdentifier;
    private readonly IntPtr _selLocalizedName;
    private readonly IntPtr _selActivationPolicy;
    private readonly IntPtr _selUtf8String;
    private readonly IntPtr _selCount;
    private readonly IntPtr _selObjectAtIndex;

    private IReadOnlySet<string> _excludedProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private bool _muteInFullscreen;
    private volatile bool _isAutoPaused;
    private volatile string? _autoPauseReason;
    private bool _disposed;

    public MacOsActiveWindowMonitor()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        try
        {
            _nsWorkspaceClass = objc_getClass("NSWorkspace");
            _selSharedWorkspace = sel_registerName("sharedWorkspace");
            _selFrontmostApplication = sel_registerName("frontmostApplication");
            _selRunningApplications = sel_registerName("runningApplications");
            _selProcessIdentifier = sel_registerName("processIdentifier");
            _selLocalizedName = sel_registerName("localizedName");
            _selActivationPolicy = sel_registerName("activationPolicy");
            _selUtf8String = sel_registerName("UTF8String");
            _selCount = sel_registerName("count");
            _selObjectAtIndex = sel_registerName("objectAtIndex:");

            if (_nsWorkspaceClass != IntPtr.Zero &&
                objc_msgSend(_nsWorkspaceClass, _selSharedWorkspace) != IntPtr.Zero)
            {
                IsSupported = true;
                _ = Task.Run(PollLoopAsync);
            }
        }
        catch
        {
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
        try
        {
            IntPtr workspace = objc_msgSend(_nsWorkspaceClass, _selSharedWorkspace);
            if (workspace == IntPtr.Zero) return Array.Empty<string>();

            IntPtr appsArray = objc_msgSend(workspace, _selRunningApplications);
            if (appsArray == IntPtr.Zero) return Array.Empty<string>();

            nint count = objc_msgSend_nint(appsArray, _selCount);
            for (nuint i = 0; i < (nuint)Math.Max(0, count); i++)
            {
                IntPtr app = objc_msgSend_ptr_nuint(appsArray, _selObjectAtIndex, i);
                if (app == IntPtr.Zero) continue;

                // NSApplicationActivationPolicyRegular == 0
                if (objc_msgSend_nint(app, _selActivationPolicy) != 0) continue;

                int pid = (int)objc_msgSend_nint(app, _selProcessIdentifier);
                if (pid <= 0 || pid == _ownPid) continue;

                string? name = ReadAppName(app, pid);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    names.Add(name);
                }
            }
        }
        catch
        {
            // Ignore transient ObjC query failures.
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

        if (_excludedProcesses.Count > 0)
        {
            try
            {
                IntPtr workspace = objc_msgSend(_nsWorkspaceClass, _selSharedWorkspace);
                IntPtr frontApp = workspace != IntPtr.Zero
                    ? objc_msgSend(workspace, _selFrontmostApplication)
                    : IntPtr.Zero;

                if (frontApp != IntPtr.Zero)
                {
                    int pid = (int)objc_msgSend_nint(frontApp, _selProcessIdentifier);
                    if (pid > 0 && pid != _ownPid)
                    {
                        string? name = ReadAppName(frontApp, pid);
                        if (!string.IsNullOrWhiteSpace(name) && _excludedProcesses.Contains(name))
                        {
                            paused = true;
                            reason = $"App: {name}";
                        }
                    }
                }
            }
            catch
            {
                // Keep monitoring cleanly.
            }
        }

        _ = _muteInFullscreen;

        if (paused != _isAutoPaused || !string.Equals(reason, _autoPauseReason, StringComparison.Ordinal))
        {
            _isAutoPaused = paused;
            _autoPauseReason = reason;
            AutoPauseStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private string? ReadAppName(IntPtr nsRunningApp, int pid)
    {
        try
        {
            using Process proc = Process.GetProcessById(pid);
            if (!string.IsNullOrWhiteSpace(proc.ProcessName))
            {
                return AppSettings.NormalizeProcessName(proc.ProcessName);
            }
        }
        catch
        {
            // Fallback to localizedName below.
        }

        IntPtr nsStr = objc_msgSend(nsRunningApp, _selLocalizedName);
        if (nsStr == IntPtr.Zero) return null;

        IntPtr utf8 = objc_msgSend(nsStr, _selUtf8String);
        string? localized = utf8 != IntPtr.Zero ? Marshal.PtrToStringUTF8(utf8) : null;
        return string.IsNullOrWhiteSpace(localized) ? null : AppSettings.NormalizeProcessName(localized);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        _cts.Dispose();
    }
}


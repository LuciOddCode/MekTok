using MekTok.Core.Platform;

namespace MekTok.Platform.Desktop.Windowing;

public sealed class NoOpActiveWindowMonitor : IActiveWindowMonitor
{
    public bool IsSupported => false;
    public bool IsAutoPaused => false;
    public string? AutoPauseReason => null;

    public event EventHandler? AutoPauseStateChanged;

    public void UpdateRules(IEnumerable<string> excludedProcessNames, bool muteInFullscreen)
    {
        // Active window inspection is not supported in this session.
    }

    public IReadOnlyList<string> GetRunningUserProcesses() => Array.Empty<string>();

    public void Dispose()
    {
        _ = AutoPauseStateChanged;
    }
}


namespace MekTok.Core.Platform;

public interface IActiveWindowMonitor : IDisposable
{
    bool IsSupported { get; }
    bool IsAutoPaused { get; }
    string? AutoPauseReason { get; }

    event EventHandler? AutoPauseStateChanged;

    void UpdateRules(IEnumerable<string> excludedProcessNames, bool muteInFullscreen);
    IReadOnlyList<string> GetRunningUserProcesses();
}


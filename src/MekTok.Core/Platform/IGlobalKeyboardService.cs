namespace MekTok.Core.Platform;

public interface IGlobalKeyboardService : IDisposable
{
    bool SupportsPerDeviceFiltering { get; }
    string? PermissionNotice { get; }

    event Action<RawKeyInputEvent>? KeyEvent;
    event EventHandler? KeyboardsChanged;

    void Start();
    IReadOnlyList<KeyboardDeviceInfo> GetConnectedKeyboards();
}


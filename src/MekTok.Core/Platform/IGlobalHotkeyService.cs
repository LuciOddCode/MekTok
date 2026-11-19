using MekTok.Core.Config;

namespace MekTok.Core.Platform;

public interface IGlobalHotkeyService : IDisposable
{
    event EventHandler? HotkeyPressed;

    (bool Success, string? ErrorMessage) ApplyHotkey(HotkeyConfig config);
    void Unregister();
}


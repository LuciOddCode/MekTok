using System.Runtime.InteropServices;
using MekTok.Core.Config;
using MekTok.Core.Keyboard;
using MekTok.Core.Platform;

namespace MekTok.App.Native;

/// <summary>
/// Registers a single system-wide mute hotkey on the shared Raw Input hidden window.
/// </summary>
public sealed class GlobalHotkeyService : IGlobalHotkeyService
{
    private const int MuteHotkeyId = 0x4D54; // "MT"
    private readonly RawInputKeyboardService _messageWindow;
    private bool _isRegistered;
    private bool _disposed;

    public GlobalHotkeyService(RawInputKeyboardService messageWindow)
    {
        _messageWindow = messageWindow ?? throw new ArgumentNullException(nameof(messageWindow));
        _messageWindow.CustomMessageReceived += OnWindowMessage;
    }

    public event EventHandler? HotkeyPressed;

    public (bool Success, string? ErrorMessage) ApplyHotkey(HotkeyConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        Unregister();

        if (!config.Enabled || config.Key == HotkeyKey.None)
        {
            return (true, null);
        }

        int vk = config.Key.ToWin32VirtualKey();
        if (vk <= 0)
        {
            return (false, $"Unsupported hotkey key: {config.Key}.");
        }

        uint modifiers = NativeMethods.MOD_NOREPEAT;
        if (config.Modifiers.HasFlag(HotkeyModifiers.Alt)) modifiers |= NativeMethods.MOD_ALT;
        if (config.Modifiers.HasFlag(HotkeyModifiers.Control)) modifiers |= NativeMethods.MOD_CONTROL;
        if (config.Modifiers.HasFlag(HotkeyModifiers.Shift)) modifiers |= NativeMethods.MOD_SHIFT;
        if (config.Modifiers.HasFlag(HotkeyModifiers.Meta)) modifiers |= NativeMethods.MOD_WIN;

        bool ok = NativeMethods.RegisterHotKey(
            _messageWindow.WindowHandle,
            MuteHotkeyId,
            modifiers,
            (uint)vk);

        if (ok)
        {
            _isRegistered = true;
            return (true, null);
        }

        int err = Marshal.GetLastWin32Error();
        string msg = err == 1409
            ? $"Shortcut {config.ToDisplayString()} is already in use by another app."
            : $"Could not register shortcut {config.ToDisplayString()} (Win32 error {err}).";
        return (false, msg);
    }

    public void Unregister()
    {
        if (!_isRegistered) return;
        NativeMethods.UnregisterHotKey(_messageWindow.WindowHandle, MuteHotkeyId);
        _isRegistered = false;
    }

    private void OnWindowMessage(IntPtr wParam, uint lParam, uint msg)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == MuteHotkeyId)
        {
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _messageWindow.CustomMessageReceived -= OnWindowMessage;
        Unregister();
    }
}

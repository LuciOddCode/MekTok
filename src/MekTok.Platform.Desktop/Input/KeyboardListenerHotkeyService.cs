using MekTok.Core.Config;
using MekTok.Core.Keyboard;
using MekTok.Core.Platform;

namespace MekTok.Platform.Desktop.Input;

/// <summary>
/// Matches the user's configured global mute hotkey directly inside the active
/// <see cref="IGlobalKeyboardService"/> stream (portable across X11, Wayland, macOS, and Windows).
/// </summary>
public sealed class KeyboardListenerHotkeyService : IGlobalHotkeyService
{
    private readonly IGlobalKeyboardService _keyboardService;
    private readonly HashSet<int> _heldScanCodes = [];
    private readonly object _sync = new();

    private HotkeyConfig _config = new() { Enabled = false };
    private int _targetScanCode;
    private bool _triggered;
    private bool _disposed;

    public KeyboardListenerHotkeyService(IGlobalKeyboardService keyboardService)
    {
        _keyboardService = keyboardService ?? throw new ArgumentNullException(nameof(keyboardService));
        _keyboardService.KeyEvent += OnKeyEvent;
    }

    public event EventHandler? HotkeyPressed;

    public (bool Success, string? ErrorMessage) ApplyHotkey(HotkeyConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        lock (_sync)
        {
            _config = new HotkeyConfig
            {
                Enabled = config.Enabled,
                Modifiers = config.Modifiers,
                Key = config.Key
            };
            _targetScanCode = config.Enabled ? config.Key.ToCanonicalScanCode() : 0;
            _triggered = false;

            if (config.Enabled && _targetScanCode == 0)
            {
                return (false, $"Key '{config.Key}' cannot be bound as a global shortcut.");
            }
        }

        return (true, null);
    }

    public void Unregister()
    {
        lock (_sync)
        {
            _config = new HotkeyConfig { Enabled = false, Key = HotkeyKey.None };
            _targetScanCode = 0;
            _triggered = false;
        }
    }

    private void OnKeyEvent(RawKeyInputEvent ev)
    {
        bool shouldFire = false;

        lock (_sync)
        {
            if (ev.IsKeyUp)
            {
                _heldScanCodes.Remove(ev.CanonicalScanCode);
                if (ev.CanonicalScanCode == _targetScanCode || IsModifierScanCode(ev.CanonicalScanCode))
                {
                    _triggered = false;
                }
                return;
            }

            bool isNewPress = _heldScanCodes.Add(ev.CanonicalScanCode);
            if (!isNewPress)
            {
                return; // Suppress OS key auto-repeat for hotkey toggling.
            }

            if (!_config.Enabled || _targetScanCode <= 0)
            {
                return;
            }

            if (ev.CanonicalScanCode == _targetScanCode && !_triggered)
            {
                HotkeyModifiers heldMods = ComputeHeldModifiers();
                if (heldMods == _config.Modifiers)
                {
                    _triggered = true;
                    shouldFire = true;
                }
            }
        }

        if (shouldFire)
        {
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
        }
    }

    private HotkeyModifiers ComputeHeldModifiers()
    {
        HotkeyModifiers mods = HotkeyModifiers.None;
        if (_heldScanCodes.Contains(0x001D) || _heldScanCodes.Contains(0xE01D)) mods |= HotkeyModifiers.Control;
        if (_heldScanCodes.Contains(0x0038) || _heldScanCodes.Contains(0xE038)) mods |= HotkeyModifiers.Alt;
        if (_heldScanCodes.Contains(0x002A) || _heldScanCodes.Contains(0x0036)) mods |= HotkeyModifiers.Shift;
        if (_heldScanCodes.Contains(0xE05B) || _heldScanCodes.Contains(0xE05C)) mods |= HotkeyModifiers.Meta;
        return mods;
    }

    private static bool IsModifierScanCode(int scanCode) =>
        scanCode is 0x001D or 0xE01D or 0x0038 or 0xE038 or 0x002A or 0x0036 or 0xE05B or 0xE05C;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _keyboardService.KeyEvent -= OnKeyEvent;
    }
}


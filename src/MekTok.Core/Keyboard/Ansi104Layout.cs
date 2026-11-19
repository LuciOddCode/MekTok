namespace MekTok.Core.Keyboard;

/// <summary>
/// Complete 104-key US ANSI layout with canonical PS/2 Set 1 scan codes and spatial geometry.
/// </summary>
public static class Ansi104Layout
{
    public const double FullWidthU = 22.5;
    public const double TklWidthU = 18.25;
    public const double TotalHeightU = 6.5;

    private static readonly IReadOnlyList<KeyDefinition> AllKeysInternal = BuildKeys();
    private static readonly IReadOnlyList<KeyDefinition> TklKeysInternal =
        AllKeysInternal.Where(k => !k.IsNumpad).ToArray();

    private static readonly Dictionary<int, KeyDefinition> ByScanCodeInternal =
        AllKeysInternal.ToDictionary(k => k.ScanCode);

    private static readonly Dictionary<string, KeyDefinition> ByKeyIdInternal =
        AllKeysInternal.ToDictionary(k => k.KeyId, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<KeyDefinition> AllKeys => AllKeysInternal;

    public static IReadOnlyDictionary<int, KeyDefinition> ByScanCode => ByScanCodeInternal;

    public static IReadOnlyDictionary<string, KeyDefinition> ByKeyId => ByKeyIdInternal;

    public static IReadOnlyList<KeyDefinition> GetKeys(bool includeNumpad) =>
        includeNumpad ? AllKeysInternal : TklKeysInternal;

    public static IReadOnlyList<KeyDefinition> GetKeysByGroup(KeyGroup group) =>
        AllKeysInternal.Where(k => k.Group == group).ToArray();

    public static KeyDefinition? TryGetByScanCode(int scanCode) =>
        ByScanCodeInternal.TryGetValue(scanCode, out var key) ? key : null;

    /// <summary>
    /// Converts Win32 RAWKEYBOARD makeCode/flags/vKey into a canonical scan code,
    /// returning null for synthetic prefix/overrun packets (VKey == 0xFF).
    /// </summary>
    public static int? NormalizeRawScanCode(ushort makeCode, ushort flags, ushort vKey)
    {
        const ushort VkPacketIgnore = 0xFF;
        const ushort VkPause = 0x13;
        const ushort VkNumLock = 0x90;
        const ushort VkSnapshot = 0x2C;
        const ushort RiKeyE0 = 0x02;
        const ushort RiKeyE1 = 0x04;

        if (vKey == VkPacketIgnore)
        {
            return null;
        }

        if ((flags & RiKeyE1) != 0 || vKey == VkPause)
        {
            return 0xE11D;
        }

        // Windows sets E0 inconsistently across drivers for NumLock and PrintScreen.
        if (vKey == VkNumLock)
        {
            return 0x0045;
        }

        if (vKey == VkSnapshot)
        {
            return 0xE037;
        }

        if (makeCode == 0)
        {
            return null;
        }

        return (flags & RiKeyE0) != 0 ? (0xE000 | makeCode) : makeCode;
    }

    private static IReadOnlyList<KeyDefinition> BuildKeys()
    {
        var list = new List<KeyDefinition>(104);

        void Add(
            int scanCode,
            string keyId,
            string label,
            KeyGroup group,
            double row,
            double col,
            double widthU = 1.0,
            double heightU = 1.0,
            bool isNumpad = false)
        {
            double centerU = col + (widthU * 0.5);
            // Center pan around the typing block (7.5U) spanning across full board for natural L/R balance.
            float pan = Math.Clamp((float)((centerU - (TklWidthU * 0.45)) / (FullWidthU * 0.5)), -1.0f, 1.0f);
            list.Add(new KeyDefinition(scanCode, keyId, label, group, row, col, widthU, heightU, isNumpad, pan));
        }

        // Row 0: Function row (y = 0, slight vertical gap handled in UI, stored as Row 0..5)
        Add(0x0001, "Esc", "Esc", KeyGroup.Function, 0, 0.0);
        Add(0x003B, "F1", "F1", KeyGroup.Function, 0, 2.0);
        Add(0x003C, "F2", "F2", KeyGroup.Function, 0, 3.0);
        Add(0x003D, "F3", "F3", KeyGroup.Function, 0, 4.0);
        Add(0x003E, "F4", "F4", KeyGroup.Function, 0, 5.0);
        Add(0x003F, "F5", "F5", KeyGroup.Function, 0, 6.5);
        Add(0x0040, "F6", "F6", KeyGroup.Function, 0, 7.5);
        Add(0x0041, "F7", "F7", KeyGroup.Function, 0, 8.5);
        Add(0x0042, "F8", "F8", KeyGroup.Function, 0, 9.5);
        Add(0x0043, "F9", "F9", KeyGroup.Function, 0, 11.0);
        Add(0x0044, "F10", "F10", KeyGroup.Function, 0, 12.0);
        Add(0x0057, "F11", "F11", KeyGroup.Function, 0, 13.0);
        Add(0x0058, "F12", "F12", KeyGroup.Function, 0, 14.0);

        Add(0xE037, "PrtSc", "PrtSc", KeyGroup.Navigation, 0, 15.25);
        Add(0x0046, "ScrLk", "ScrLk", KeyGroup.Navigation, 0, 16.25);
        Add(0xE11D, "Pause", "Pause", KeyGroup.Navigation, 0, 17.25);

        // Row 1: Number row
        Add(0x0029, "Backquote", "` ~", KeyGroup.AlphaNumeric, 1, 0.0);
        Add(0x0002, "Digit1", "1", KeyGroup.AlphaNumeric, 1, 1.0);
        Add(0x0003, "Digit2", "2", KeyGroup.AlphaNumeric, 1, 2.0);
        Add(0x0004, "Digit3", "3", KeyGroup.AlphaNumeric, 1, 3.0);
        Add(0x0005, "Digit4", "4", KeyGroup.AlphaNumeric, 1, 4.0);
        Add(0x0006, "Digit5", "5", KeyGroup.AlphaNumeric, 1, 5.0);
        Add(0x0007, "Digit6", "6", KeyGroup.AlphaNumeric, 1, 6.0);
        Add(0x0008, "Digit7", "7", KeyGroup.AlphaNumeric, 1, 7.0);
        Add(0x0009, "Digit8", "8", KeyGroup.AlphaNumeric, 1, 8.0);
        Add(0x000A, "Digit9", "9", KeyGroup.AlphaNumeric, 1, 9.0);
        Add(0x000B, "Digit0", "0", KeyGroup.AlphaNumeric, 1, 10.0);
        Add(0x000C, "Minus", "-", KeyGroup.AlphaNumeric, 1, 11.0);
        Add(0x000D, "Equal", "=", KeyGroup.AlphaNumeric, 1, 12.0);
        Add(0x000E, "Backspace", "Backspace", KeyGroup.Backspace, 1, 13.0, widthU: 2.0);

        Add(0xE052, "Insert", "Ins", KeyGroup.Navigation, 1, 15.25);
        Add(0xE047, "Home", "Home", KeyGroup.Navigation, 1, 16.25);
        Add(0xE049, "PageUp", "PgUp", KeyGroup.Navigation, 1, 17.25);

        Add(0x0045, "NumLock", "Num", KeyGroup.Numpad, 1, 18.5, isNumpad: true);
        Add(0xE035, "NumDivide", "/", KeyGroup.Numpad, 1, 19.5, isNumpad: true);
        Add(0x0037, "NumMultiply", "*", KeyGroup.Numpad, 1, 20.5, isNumpad: true);
        Add(0x004A, "NumSubtract", "-", KeyGroup.Numpad, 1, 21.5, isNumpad: true);

        // Row 2: QWERTY
        Add(0x000F, "Tab", "Tab", KeyGroup.TabCaps, 2, 0.0, widthU: 1.5);
        Add(0x0010, "KeyQ", "Q", KeyGroup.AlphaNumeric, 2, 1.5);
        Add(0x0011, "KeyW", "W", KeyGroup.AlphaNumeric, 2, 2.5);
        Add(0x0012, "KeyE", "E", KeyGroup.AlphaNumeric, 2, 3.5);
        Add(0x0013, "KeyR", "R", KeyGroup.AlphaNumeric, 2, 4.5);
        Add(0x0014, "KeyT", "T", KeyGroup.AlphaNumeric, 2, 5.5);
        Add(0x0015, "KeyY", "Y", KeyGroup.AlphaNumeric, 2, 6.5);
        Add(0x0016, "KeyU", "U", KeyGroup.AlphaNumeric, 2, 7.5);
        Add(0x0017, "KeyI", "I", KeyGroup.AlphaNumeric, 2, 8.5);
        Add(0x0018, "KeyO", "O", KeyGroup.AlphaNumeric, 2, 9.5);
        Add(0x0019, "KeyP", "P", KeyGroup.AlphaNumeric, 2, 10.5);
        Add(0x001A, "BracketLeft", "[", KeyGroup.AlphaNumeric, 2, 11.5);
        Add(0x001B, "BracketRight", "]", KeyGroup.AlphaNumeric, 2, 12.5);
        Add(0x002B, "Backslash", "\\", KeyGroup.AlphaNumeric, 2, 13.5, widthU: 1.5);

        Add(0xE053, "Delete", "Del", KeyGroup.Navigation, 2, 15.25);
        Add(0xE04F, "End", "End", KeyGroup.Navigation, 2, 16.25);
        Add(0xE051, "PageDown", "PgDn", KeyGroup.Navigation, 2, 17.25);

        Add(0x0047, "Num7", "7", KeyGroup.Numpad, 2, 18.5, isNumpad: true);
        Add(0x0048, "Num8", "8", KeyGroup.Numpad, 2, 19.5, isNumpad: true);
        Add(0x0049, "Num9", "9", KeyGroup.Numpad, 2, 20.5, isNumpad: true);
        Add(0x004E, "NumAdd", "+", KeyGroup.Numpad, 2, 21.5, heightU: 2.0, isNumpad: true);

        // Row 3: Home row
        Add(0x003A, "CapsLock", "Caps", KeyGroup.TabCaps, 3, 0.0, widthU: 1.75);
        Add(0x001E, "KeyA", "A", KeyGroup.AlphaNumeric, 3, 1.75);
        Add(0x001F, "KeyS", "S", KeyGroup.AlphaNumeric, 3, 2.75);
        Add(0x0020, "KeyD", "D", KeyGroup.AlphaNumeric, 3, 3.75);
        Add(0x0021, "KeyF", "F", KeyGroup.AlphaNumeric, 3, 4.75);
        Add(0x0022, "KeyG", "G", KeyGroup.AlphaNumeric, 3, 5.75);
        Add(0x0023, "KeyH", "H", KeyGroup.AlphaNumeric, 3, 6.75);
        Add(0x0024, "KeyJ", "J", KeyGroup.AlphaNumeric, 3, 7.75);
        Add(0x0025, "KeyK", "K", KeyGroup.AlphaNumeric, 3, 8.75);
        Add(0x0026, "KeyL", "L", KeyGroup.AlphaNumeric, 3, 9.75);
        Add(0x0027, "Semicolon", ";", KeyGroup.AlphaNumeric, 3, 10.75);
        Add(0x0028, "Quote", "'", KeyGroup.AlphaNumeric, 3, 11.75);
        Add(0x001C, "Enter", "Enter", KeyGroup.Enter, 3, 12.75, widthU: 2.25);

        Add(0x004B, "Num4", "4", KeyGroup.Numpad, 3, 18.5, isNumpad: true);
        Add(0x004C, "Num5", "5", KeyGroup.Numpad, 3, 19.5, isNumpad: true);
        Add(0x004D, "Num6", "6", KeyGroup.Numpad, 3, 20.5, isNumpad: true);

        // Row 4: Shift row
        Add(0x002A, "LShift", "Shift", KeyGroup.Modifiers, 4, 0.0, widthU: 2.25);
        Add(0x002C, "KeyZ", "Z", KeyGroup.AlphaNumeric, 4, 2.25);
        Add(0x002D, "KeyX", "X", KeyGroup.AlphaNumeric, 4, 3.25);
        Add(0x002E, "KeyC", "C", KeyGroup.AlphaNumeric, 4, 4.25);
        Add(0x002F, "KeyV", "V", KeyGroup.AlphaNumeric, 4, 5.25);
        Add(0x0030, "KeyB", "B", KeyGroup.AlphaNumeric, 4, 6.25);
        Add(0x0031, "KeyN", "N", KeyGroup.AlphaNumeric, 4, 7.25);
        Add(0x0032, "KeyM", "M", KeyGroup.AlphaNumeric, 4, 8.25);
        Add(0x0033, "Comma", ",", KeyGroup.AlphaNumeric, 4, 9.25);
        Add(0x0034, "Period", ".", KeyGroup.AlphaNumeric, 4, 10.25);
        Add(0x0035, "Slash", "/", KeyGroup.AlphaNumeric, 4, 11.25);
        Add(0x0036, "RShift", "Shift", KeyGroup.Modifiers, 4, 12.25, widthU: 2.75);

        Add(0xE048, "ArrowUp", "▲", KeyGroup.Navigation, 4, 16.25);

        Add(0x004F, "Num1", "1", KeyGroup.Numpad, 4, 18.5, isNumpad: true);
        Add(0x0050, "Num2", "2", KeyGroup.Numpad, 4, 19.5, isNumpad: true);
        Add(0x0051, "Num3", "3", KeyGroup.Numpad, 4, 20.5, isNumpad: true);
        Add(0xE01C, "NumEnter", "Enter", KeyGroup.Numpad, 4, 21.5, heightU: 2.0, isNumpad: true);

        // Row 5: Bottom row
        Add(0x001D, "LControl", "Ctrl", KeyGroup.Modifiers, 5, 0.0, widthU: 1.25);
        Add(0xE05B, "LWin", "Win", KeyGroup.Modifiers, 5, 1.25, widthU: 1.25);
        Add(0x0038, "LAlt", "Alt", KeyGroup.Modifiers, 5, 2.50, widthU: 1.25);
        Add(0x0039, "Space", "Space", KeyGroup.Spacebar, 5, 3.75, widthU: 6.25);
        Add(0xE038, "RAlt", "Alt", KeyGroup.Modifiers, 5, 10.0, widthU: 1.25);
        Add(0xE05C, "RWin", "Win", KeyGroup.Modifiers, 5, 11.25, widthU: 1.25);
        Add(0xE05D, "ContextMenu", "Menu", KeyGroup.Modifiers, 5, 12.50, widthU: 1.25);
        Add(0xE01D, "RControl", "Ctrl", KeyGroup.Modifiers, 5, 13.75, widthU: 1.25);

        Add(0xE04B, "ArrowLeft", "◀", KeyGroup.Navigation, 5, 15.25);
        Add(0xE050, "ArrowDown", "▼", KeyGroup.Navigation, 5, 16.25);
        Add(0xE04D, "ArrowRight", "▶", KeyGroup.Navigation, 5, 17.25);

        Add(0x0052, "Num0", "0", KeyGroup.Numpad, 5, 18.5, widthU: 2.0, isNumpad: true);
        Add(0x0053, "NumDecimal", ".", KeyGroup.Numpad, 5, 20.5, isNumpad: true);

        return list.AsReadOnly();
    }
}


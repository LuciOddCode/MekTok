using SharpHook.Data;

namespace MekTok.Platform.Desktop.Input;

/// <summary>
/// Maps cross-platform SharpHook / libuiohook <see cref="KeyCode"/> values
/// to MekTok's canonical PS/2 Set 1 scan codes defined in <see cref="MekTok.Core.Keyboard.Ansi104Layout"/>.
/// </summary>
public static class SharpHookKeyMap
{
    private static readonly Dictionary<KeyCode, int> KeyCodeToScanCode = new()
    {
        // Row 0: Function & System cluster
        [KeyCode.VcEscape] = 0x0001,
        [KeyCode.VcF1] = 0x003B,
        [KeyCode.VcF2] = 0x003C,
        [KeyCode.VcF3] = 0x003D,
        [KeyCode.VcF4] = 0x003E,
        [KeyCode.VcF5] = 0x003F,
        [KeyCode.VcF6] = 0x0040,
        [KeyCode.VcF7] = 0x0041,
        [KeyCode.VcF8] = 0x0042,
        [KeyCode.VcF9] = 0x0043,
        [KeyCode.VcF10] = 0x0044,
        [KeyCode.VcF11] = 0x0057,
        [KeyCode.VcF12] = 0x0058,
        [KeyCode.VcF13] = 0x0064,
        [KeyCode.VcF14] = 0x0065,
        [KeyCode.VcF15] = 0x0066,
        [KeyCode.VcF16] = 0x0067,
        [KeyCode.VcF17] = 0x0068,
        [KeyCode.VcF18] = 0x0069,
        [KeyCode.VcF19] = 0x006A,
        [KeyCode.VcF20] = 0x006B,
        [KeyCode.VcF21] = 0x006C,
        [KeyCode.VcF22] = 0x006D,
        [KeyCode.VcF23] = 0x006E,
        [KeyCode.VcF24] = 0x0076,
        [KeyCode.VcPrintScreen] = 0xE037,
        [KeyCode.VcScrollLock] = 0x0046,
        [KeyCode.VcPause] = 0xE11D,

        // Row 1: Number row & Navigation & Numpad top
        [KeyCode.VcBackQuote] = 0x0029,
        [KeyCode.Vc1] = 0x0002,
        [KeyCode.Vc2] = 0x0003,
        [KeyCode.Vc3] = 0x0004,
        [KeyCode.Vc4] = 0x0005,
        [KeyCode.Vc5] = 0x0006,
        [KeyCode.Vc6] = 0x0007,
        [KeyCode.Vc7] = 0x0008,
        [KeyCode.Vc8] = 0x0009,
        [KeyCode.Vc9] = 0x000A,
        [KeyCode.Vc0] = 0x000B,
        [KeyCode.VcMinus] = 0x000C,
        [KeyCode.VcEquals] = 0x000D,
        [KeyCode.VcBackspace] = 0x000E,
        [KeyCode.VcInsert] = 0xE052,
        [KeyCode.VcHome] = 0xE047,
        [KeyCode.VcPageUp] = 0xE049,
        [KeyCode.VcNumLock] = 0x0045,
        [KeyCode.VcNumPadClear] = 0x0045,
        [KeyCode.VcNumPadDivide] = 0xE035,
        [KeyCode.VcNumPadMultiply] = 0x0037,
        [KeyCode.VcNumPadSubtract] = 0x004A,
        [KeyCode.VcNumPadEquals] = 0x000D,

        // Row 2: QWERTY
        [KeyCode.VcTab] = 0x000F,
        [KeyCode.VcQ] = 0x0010,
        [KeyCode.VcW] = 0x0011,
        [KeyCode.VcE] = 0x0012,
        [KeyCode.VcR] = 0x0013,
        [KeyCode.VcT] = 0x0014,
        [KeyCode.VcY] = 0x0015,
        [KeyCode.VcU] = 0x0016,
        [KeyCode.VcI] = 0x0017,
        [KeyCode.VcO] = 0x0018,
        [KeyCode.VcP] = 0x0019,
        [KeyCode.VcOpenBracket] = 0x001A,
        [KeyCode.VcCloseBracket] = 0x001B,
        [KeyCode.VcBackslash] = 0x002B,
        [KeyCode.VcDelete] = 0xE053,
        [KeyCode.VcEnd] = 0xE04F,
        [KeyCode.VcPageDown] = 0xE051,
        [KeyCode.VcNumPad7] = 0x0047,
        [KeyCode.VcNumPad8] = 0x0048,
        [KeyCode.VcNumPad9] = 0x0049,
        [KeyCode.VcNumPadAdd] = 0x004E,

        // Row 3: Home row
        [KeyCode.VcCapsLock] = 0x003A,
        [KeyCode.VcA] = 0x001E,
        [KeyCode.VcS] = 0x001F,
        [KeyCode.VcD] = 0x0020,
        [KeyCode.VcF] = 0x0021,
        [KeyCode.VcG] = 0x0022,
        [KeyCode.VcH] = 0x0023,
        [KeyCode.VcJ] = 0x0024,
        [KeyCode.VcK] = 0x0025,
        [KeyCode.VcL] = 0x0026,
        [KeyCode.VcSemicolon] = 0x0027,
        [KeyCode.VcQuote] = 0x0028,
        [KeyCode.VcEnter] = 0x001C,
        [KeyCode.VcNumPad4] = 0x004B,
        [KeyCode.VcNumPad5] = 0x004C,
        [KeyCode.VcNumPad6] = 0x004D,

        // Row 4: Shift row
        [KeyCode.VcLeftShift] = 0x002A,
        [KeyCode.VcZ] = 0x002C,
        [KeyCode.VcX] = 0x002D,
        [KeyCode.VcC] = 0x002E,
        [KeyCode.VcV] = 0x002F,
        [KeyCode.VcB] = 0x0030,
        [KeyCode.VcN] = 0x0031,
        [KeyCode.VcM] = 0x0032,
        [KeyCode.VcComma] = 0x0033,
        [KeyCode.VcPeriod] = 0x0034,
        [KeyCode.VcSlash] = 0x0035,
        [KeyCode.VcRightShift] = 0x0036,
        [KeyCode.VcUp] = 0xE048,
        [KeyCode.VcNumPad1] = 0x004F,
        [KeyCode.VcNumPad2] = 0x0050,
        [KeyCode.VcNumPad3] = 0x0051,
        [KeyCode.VcNumPadEnter] = 0xE01C,

        // Row 5: Bottom row
        [KeyCode.VcLeftControl] = 0x001D,
        [KeyCode.VcLeftMeta] = 0xE05B,
        [KeyCode.VcLeftAlt] = 0x0038,
        [KeyCode.VcSpace] = 0x0039,
        [KeyCode.VcRightAlt] = 0xE038,
        [KeyCode.VcRightMeta] = 0xE05C,
        [KeyCode.VcContextMenu] = 0xE05D,
        [KeyCode.VcRightControl] = 0xE01D,
        [KeyCode.VcLeft] = 0xE04B,
        [KeyCode.VcDown] = 0xE050,
        [KeyCode.VcRight] = 0xE04D,
        [KeyCode.VcNumPad0] = 0x0052,
        [KeyCode.VcNumPadDecimal] = 0x0053,
        [KeyCode.VcNumPadSeparator] = 0x0053
    };

    public static IReadOnlyDictionary<KeyCode, int> AllMappings => KeyCodeToScanCode;

    public static bool TryMapKeyCode(KeyCode keyCode, out int canonicalScanCode) =>
        KeyCodeToScanCode.TryGetValue(keyCode, out canonicalScanCode);
}


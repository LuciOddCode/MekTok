namespace MekTok.Core.Keyboard;

/// <summary>
/// Maps Mechvibes / iohook / uiohook decimal keycodes from sound pack config.json
/// files into canonical ANSI scan codes used by MekTok.
/// </summary>
public static class MechvibesKeyMap
{
    private static readonly Dictionary<int, int> ExtendedCodeMap = new()
    {
        // iohook / libuiohook extended codes and common Mechvibes variants
        [3612] = 0xE01C, // NumEnter
        [3613] = 0xE01D, // RCtrl
        [3637] = 0xE035, // NumDivide
        [3639] = 0xE037, // PrintScreen
        [3640] = 0xE038, // RAlt
        [3653] = 0xE11D, // Pause
        [3655] = 0xE047, // Home
        [3657] = 0xE049, // PageUp
        [3663] = 0xE04F, // End
        [3665] = 0xE051, // PageDown
        [3666] = 0xE052, // Insert
        [3667] = 0xE053, // Delete
        [3675] = 0xE05B, // LWin
        [3676] = 0xE05C, // RWin
        [3677] = 0xE05D, // ContextMenu

        // uiohook / arrow & navigation variants (0xEE00-based / 61000-based)
        [60999] = 0xE047, // Home
        [61000] = 0xE048, // Up
        [61001] = 0xE049, // PageUp
        [61003] = 0xE04B, // Left
        [61005] = 0xE04D, // Right
        [61007] = 0xE04F, // End
        [61008] = 0xE050, // Down
        [61009] = 0xE051, // PageDown
        [61010] = 0xE052, // Insert
        [61011] = 0xE053, // Delete

        // Direct 0xE000 | makeCode decimal equivalents (57344 + makeCode)
        [57372] = 0xE01C,
        [57373] = 0xE01D,
        [57397] = 0xE035,
        [57399] = 0xE037,
        [57400] = 0xE038,
        [57415] = 0xE047,
        [57416] = 0xE048,
        [57417] = 0xE049,
        [57419] = 0xE04B,
        [57421] = 0xE04D,
        [57423] = 0xE04F,
        [57424] = 0xE050,
        [57425] = 0xE051,
        [57426] = 0xE052,
        [57427] = 0xE053,
        [57435] = 0xE05B,
        [57436] = 0xE05C,
        [57437] = 0xE05D,
        [57629] = 0xE11D
    };

    /// <summary>
    /// Attempts to resolve a Mechvibes config key string into a canonical scan code.
    /// </summary>
    public static bool TryMapMechvibesCode(string rawCode, out int canonicalScanCode)
    {
        canonicalScanCode = 0;
        if (!int.TryParse(rawCode, out int code) || code <= 0)
        {
            return false;
        }

        if (ExtendedCodeMap.TryGetValue(code, out int mapped))
        {
            canonicalScanCode = mapped;
            return true;
        }

        // Standard PS/2 Set 1 codes (1..88) match directly.
        if (Ansi104Layout.ByScanCode.ContainsKey(code))
        {
            canonicalScanCode = code;
            return true;
        }

        return false;
    }
}


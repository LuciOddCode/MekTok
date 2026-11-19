using Avalonia.Input;
using MekTok.Core.Config;
using MekTok.Core.Keyboard;

namespace MekTok.Desktop.UI;

internal static class AvaloniaKeyMapper
{
    public static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or
        Key.LeftAlt or Key.RightAlt or
        Key.LeftShift or Key.RightShift or
        Key.LWin or Key.RWin;

    public static HotkeyModifiers ToHotkeyModifiers(KeyModifiers modifiers)
    {
        HotkeyModifiers result = HotkeyModifiers.None;
        if (modifiers.HasFlag(KeyModifiers.Control)) result |= HotkeyModifiers.Control;
        if (modifiers.HasFlag(KeyModifiers.Alt)) result |= HotkeyModifiers.Alt;
        if (modifiers.HasFlag(KeyModifiers.Shift)) result |= HotkeyModifiers.Shift;
        if (modifiers.HasFlag(KeyModifiers.Meta)) result |= HotkeyModifiers.Meta;
        return result;
    }

    public static HotkeyKey ToHotkeyKey(Key key) => key switch
    {
        >= Key.A and <= Key.Z => HotkeyKey.A + (key - Key.A),
        >= Key.D0 and <= Key.D9 => HotkeyKey.D0 + (key - Key.D0),
        >= Key.F1 and <= Key.F24 => HotkeyKey.F1 + (key - Key.F1),
        Key.Space => HotkeyKey.Space,
        Key.Escape => HotkeyKey.Escape,
        Key.Enter => HotkeyKey.Enter,
        Key.Back => HotkeyKey.Backspace,
        Key.Tab => HotkeyKey.Tab,
        Key.Insert => HotkeyKey.Insert,
        Key.Delete => HotkeyKey.Delete,
        Key.Home => HotkeyKey.Home,
        Key.End => HotkeyKey.End,
        Key.PageUp => HotkeyKey.PageUp,
        Key.PageDown => HotkeyKey.PageDown,
        Key.Left => HotkeyKey.Left,
        Key.Up => HotkeyKey.Up,
        Key.Right => HotkeyKey.Right,
        Key.Down => HotkeyKey.Down,
        Key.OemTilde => HotkeyKey.OemGrave,
        Key.OemMinus => HotkeyKey.OemMinus,
        Key.OemPlus => HotkeyKey.OemEqual,
        Key.OemOpenBrackets => HotkeyKey.OemLeftBracket,
        Key.OemCloseBrackets => HotkeyKey.OemRightBracket,
        Key.OemPipe => HotkeyKey.OemBackslash,
        Key.OemSemicolon => HotkeyKey.OemSemicolon,
        Key.OemQuotes => HotkeyKey.OemQuote,
        Key.OemComma => HotkeyKey.OemComma,
        Key.OemPeriod => HotkeyKey.OemPeriod,
        Key.OemQuestion => HotkeyKey.OemSlash,
        _ => HotkeyKey.None
    };
}


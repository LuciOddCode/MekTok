namespace MekTok.Core.Keyboard;

/// <summary>
/// Logical acoustic and visual grouping of physical keys on an ANSI keyboard.
/// </summary>
public enum KeyGroup
{
    AlphaNumeric,
    Spacebar,
    Enter,
    Backspace,
    TabCaps,
    Modifiers,
    Navigation,
    Function,
    Numpad
}

/// <summary>
/// Presentation helpers for <see cref="KeyGroup"/>.
/// </summary>
public static class KeyGroupExtensions
{
    public static string ToDisplayName(this KeyGroup group) => group switch
    {
        KeyGroup.AlphaNumeric => "Alphas & Numbers",
        KeyGroup.Spacebar => "Spacebar",
        KeyGroup.Enter => "Enter",
        KeyGroup.Backspace => "Backspace",
        KeyGroup.TabCaps => "Tab & Caps Lock",
        KeyGroup.Modifiers => "Modifiers",
        KeyGroup.Navigation => "Navigation & Arrows",
        KeyGroup.Function => "Function & Esc",
        KeyGroup.Numpad => "Numpad",
        _ => group.ToString()
    };
}


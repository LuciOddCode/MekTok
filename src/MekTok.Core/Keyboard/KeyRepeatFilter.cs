namespace MekTok.Core.Keyboard;

/// <summary>
/// Thread-safe tracker for held keys so Windows auto-repeat packets do not trigger
/// artificial keystrokes unless explicitly enabled (or allowed for editing keys).
/// </summary>
public sealed class KeyRepeatFilter
{
    private static readonly HashSet<int> EditingRepeatScanCodes =
    [
        0x000E, // Backspace
        0xE053, // Delete
        0xE048, // ArrowUp
        0xE050, // ArrowDown
        0xE04B, // ArrowLeft
        0xE04D  // ArrowRight
    ];

    private readonly HashSet<int> _heldKeys = [];
    private readonly object _sync = new();

    public bool ShouldTrigger(
        int scanCode,
        bool isKeyUp,
        bool suppressHoldRepeat,
        bool allowEditingKeyRepeat)
    {
        lock (_sync)
        {
            if (isKeyUp)
            {
                return _heldKeys.Remove(scanCode);
            }

            bool isNewPress = _heldKeys.Add(scanCode);
            if (isNewPress)
            {
                return true;
            }

            if (!suppressHoldRepeat)
            {
                return true;
            }

            return allowEditingKeyRepeat && EditingRepeatScanCodes.Contains(scanCode);
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _heldKeys.Clear();
        }
    }
}


using MekTok.Core.Keyboard;

namespace MekTok.Core.Config;

/// <summary>
/// Configuration for a custom simultaneous key combination (chord) that triggers
/// an imported audio effect (e.g. WAV/MP3) and optionally suppresses normal switch sounds.
/// </summary>
public sealed class CustomKeyCombo
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Custom Combo";
    public bool IsEnabled { get; set; } = true;
    public List<int> ScanCodes { get; set; } = [];
    public string AudioFilePath { get; set; } = string.Empty;
    public float Volume { get; set; } = 1.0f;
    public bool SuppressSwitchSound { get; set; } = true;

    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            Id = Guid.NewGuid().ToString("N");
        }

        Name = string.IsNullOrWhiteSpace(Name) ? "Custom Combo" : Name.Trim();
        Volume = Math.Clamp(Volume, 0.0f, 1.5f);
        AudioFilePath = AudioFilePath?.Trim() ?? string.Empty;

        ScanCodes = (ScanCodes ?? [])
            .Where(c => c > 0)
            .Distinct()
            .OrderBy(c => c)
            .ToList();
    }

    public CustomKeyCombo Clone() => new()
    {
        Id = Id,
        Name = Name,
        IsEnabled = IsEnabled,
        ScanCodes = new List<int>(ScanCodes),
        AudioFilePath = AudioFilePath,
        Volume = Volume,
        SuppressSwitchSound = SuppressSwitchSound
    };

    /// <summary>
    /// Generates human-friendly key badges for this combo (e.g. "Shift + Y + 1").
    /// </summary>
    public string FormatKeysDisplay()
    {
        if (ScanCodes.Count == 0)
        {
            return "No keys assigned";
        }

        var labels = new List<string>(ScanCodes.Count);
        foreach (int code in ScanCodes)
        {
            labels.Add(FormatScanCode(code));
        }

        return string.Join(" + ", labels);
    }

    public static string FormatScanCode(int code)
    {
        KeyDefinition? def = Ansi104Layout.TryGetByScanCode(code);
        if (def is not null)
        {
            return def.Label;
        }

        return code switch
        {
            0x002A => "L-Shift",
            0x0036 => "R-Shift",
            0x001D => "L-Ctrl",
            0xE01D => "R-Ctrl",
            0x0038 => "L-Alt",
            0xE038 => "R-Alt",
            0xE05B => "Win",
            _ => $"Key 0x{code:X2}"
        };
    }
}


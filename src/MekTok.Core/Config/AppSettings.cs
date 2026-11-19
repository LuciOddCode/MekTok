using System.Text.Json;
using System.Text.Json.Serialization;
using MekTok.Core.Audio;
using MekTok.Core.Keyboard;

namespace MekTok.Core.Config;

public enum ThemePreference
{
    System,
    Dark,
    Light
}

[Flags]
[JsonConverter(typeof(HotkeyModifiersJsonConverter))]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Meta = 0x0008
}

public sealed class HotkeyModifiersJsonConverter : JsonConverter<HotkeyModifiers>
{
    public override HotkeyModifiers Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            return (HotkeyModifiers)reader.GetUInt32();
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            string? raw = reader.GetString();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return HotkeyModifiers.None;
            }

            HotkeyModifiers result = HotkeyModifiers.None;
            foreach (string part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (part.Equals("Win", StringComparison.OrdinalIgnoreCase) ||
                    part.Equals("Meta", StringComparison.OrdinalIgnoreCase) ||
                    part.Equals("Super", StringComparison.OrdinalIgnoreCase) ||
                    part.Equals("Cmd", StringComparison.OrdinalIgnoreCase) ||
                    part.Equals("Command", StringComparison.OrdinalIgnoreCase))
                {
                    result |= HotkeyModifiers.Meta;
                }
                else if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase))
                {
                    result |= HotkeyModifiers.Control;
                }
                else if (part.Equals("Option", StringComparison.OrdinalIgnoreCase))
                {
                    result |= HotkeyModifiers.Alt;
                }
                else if (Enum.TryParse<HotkeyModifiers>(part, ignoreCase: true, out HotkeyModifiers parsed))
                {
                    result |= parsed;
                }
            }

            return result;
        }

        throw new JsonException($"Unexpected token '{reader.TokenType}' for HotkeyModifiers.");
    }

    public override void Write(Utf8JsonWriter writer, HotkeyModifiers value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}

public sealed class HotkeyConfig
{
    private HotkeyKey _key = HotkeyKey.M;
    private bool _keyWasExplicitlySet;
    private int? _legacyVirtualKey;

    public bool Enabled { get; set; } = true;

    [JsonConverter(typeof(HotkeyModifiersJsonConverter))]
    public HotkeyModifiers Modifiers { get; set; } = HotkeyModifiers.Control | HotkeyModifiers.Alt;

    public HotkeyKey Key
    {
        get => _key;
        set
        {
            _key = value;
            _keyWasExplicitlySet = true;
        }
    }

    /// <summary>
    /// Legacy Win32 VirtualKey kept strictly for migrating existing Windows settings.json files.
    /// Always cleared during Normalize() so saved settings files remain platform-neutral.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? VirtualKey
    {
        get => _legacyVirtualKey;
        set => _legacyVirtualKey = value;
    }

    public void Normalize()
    {
        if (!_keyWasExplicitlySet && _legacyVirtualKey is int vk && vk > 0)
        {
            HotkeyKey migrated = HotkeyKeyExtensions.FromWin32VirtualKey(vk);
            if (migrated != HotkeyKey.None)
            {
                _key = migrated;
            }
            else
            {
                _key = HotkeyKey.None;
                Enabled = false;
            }
        }

        _legacyVirtualKey = null;
    }

    public string ToDisplayString()
    {
        if (!Enabled || Key == HotkeyKey.None)
        {
            return "Disabled";
        }

        var parts = new List<string>(5);
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Meta))
        {
            parts.Add(OperatingSystem.IsMacOS()
                ? "Cmd"
                : OperatingSystem.IsWindows()
                    ? "Win"
                    : "Super");
        }
        parts.Add(Key.ToDisplayString());
        return string.Join("+", parts);
    }
}

public sealed class AcousticSettings
{
    public bool EnableKeyUpSound { get; set; } = true;
    public float KeyUpVolume { get; set; } = 0.55f;
    public float HumanizeAmount { get; set; } = 0.35f;
    public bool EnableStereoPan { get; set; } = true;
    public float StereoPanWidth { get; set; } = 0.35f;
    public bool SuppressHoldRepeat { get; set; } = true;
    public bool AllowEditingKeyRepeat { get; set; } = true;
    public float PitchShiftSemitones { get; set; } = 0.0f;
    public float ThockAmount { get; set; } = 0.50f;
    public float BrightnessAmount { get; set; } = 0.50f;

    public void Normalize()
    {
        KeyUpVolume = Math.Clamp(KeyUpVolume, 0.0f, 1.5f);
        HumanizeAmount = Math.Clamp(HumanizeAmount, 0.0f, 1.0f);
        StereoPanWidth = Math.Clamp(StereoPanWidth, 0.0f, 1.0f);
        PitchShiftSemitones = Math.Clamp(PitchShiftSemitones, -6.0f, 6.0f);
        ThockAmount = Math.Clamp(ThockAmount, 0.0f, 1.0f);
        BrightnessAmount = Math.Clamp(BrightnessAmount, 0.0f, 1.0f);
    }
}

public sealed class SoundOverride
{
    public bool IsMuted { get; set; }
    public string? BuiltInSwitchOverrideId { get; set; }
    public string? CustomPressAudioPath { get; set; }
    public string? CustomReleaseAudioPath { get; set; }
    public float VolumeMultiplier { get; set; } = 1.0f;
    public float PitchOffsetSemitones { get; set; } = 0.0f;

    public bool IsEmpty =>
        !IsMuted
        && string.IsNullOrWhiteSpace(BuiltInSwitchOverrideId)
        && string.IsNullOrWhiteSpace(CustomPressAudioPath)
        && string.IsNullOrWhiteSpace(CustomReleaseAudioPath)
        && MathF.Abs(VolumeMultiplier - 1.0f) < 0.005f
        && MathF.Abs(PitchOffsetSemitones) < 0.01f;

    public void Normalize()
    {
        VolumeMultiplier = Math.Clamp(VolumeMultiplier, 0.0f, 2.0f);
        PitchOffsetSemitones = Math.Clamp(PitchOffsetSemitones, -12.0f, 12.0f);
        if (string.IsNullOrWhiteSpace(BuiltInSwitchOverrideId)) BuiltInSwitchOverrideId = null;
        if (string.IsNullOrWhiteSpace(CustomPressAudioPath)) CustomPressAudioPath = null;
        if (string.IsNullOrWhiteSpace(CustomReleaseAudioPath)) CustomReleaseAudioPath = null;
    }

    public SoundOverride Clone() => new()
    {
        IsMuted = IsMuted,
        BuiltInSwitchOverrideId = BuiltInSwitchOverrideId,
        CustomPressAudioPath = CustomPressAudioPath,
        CustomReleaseAudioPath = CustomReleaseAudioPath,
        VolumeMultiplier = VolumeMultiplier,
        PitchOffsetSemitones = PitchOffsetSemitones
    };
}

public sealed class ProfileCustomization
{
    public Dictionary<KeyGroup, SoundOverride> GroupOverrides { get; set; } = [];
    public Dictionary<int, SoundOverride> KeyOverrides { get; set; } = [];

    public void Normalize()
    {
        GroupOverrides ??= [];
        KeyOverrides ??= [];

        foreach (KeyGroup key in GroupOverrides.Keys.ToArray())
        {
            SoundOverride? item = GroupOverrides[key];
            if (item is null)
            {
                GroupOverrides.Remove(key);
                continue;
            }
            item.Normalize();
            if (item.IsEmpty)
            {
                GroupOverrides.Remove(key);
            }
        }

        foreach (int scanCode in KeyOverrides.Keys.ToArray())
        {
            SoundOverride? item = KeyOverrides[scanCode];
            if (item is null)
            {
                KeyOverrides.Remove(scanCode);
                continue;
            }
            item.Normalize();
            if (item.IsEmpty)
            {
                KeyOverrides.Remove(scanCode);
            }
        }
    }
}

public sealed class AppSettings
{
    public bool IsEnabled { get; set; } = true;
    public float MasterVolume { get; set; } = 0.75f;
    public string ActiveProfileId { get; set; } = BuiltInProfiles.CreamyThockId;
    public string? OutputDeviceId { get; set; }
    public AcousticSettings Acoustics { get; set; } = new();
    public HotkeyConfig MuteHotkey { get; set; } = new();
    public bool MuteInFullscreen { get; set; }
    public List<string> ExcludedProcessNames { get; set; } = [];
    public HashSet<string> DisabledKeyboardDeviceIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool StartWithWindows { get; set; }
    public bool HideNumpadInConfigurator { get; set; }
    public ThemePreference Theme { get; set; } = ThemePreference.System;
    public List<CustomKeyCombo> CustomCombos { get; set; } = [];
    public Dictionary<string, ProfileCustomization> ProfileCustomizations { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public ProfileCustomization GetOrCreateCustomization(string profileId)
    {
        if (!ProfileCustomizations.TryGetValue(profileId, out ProfileCustomization? customization))
        {
            customization = new ProfileCustomization();
            ProfileCustomizations[profileId] = customization;
        }
        return customization;
    }

    public void Normalize()
    {
        MasterVolume = Math.Clamp(MasterVolume, 0.0f, 1.0f);
        if (string.IsNullOrWhiteSpace(ActiveProfileId))
        {
            ActiveProfileId = BuiltInProfiles.CreamyThockId;
        }

        if (string.IsNullOrWhiteSpace(OutputDeviceId))
        {
            OutputDeviceId = null;
        }

        Acoustics ??= new AcousticSettings();
        Acoustics.Normalize();

        MuteHotkey ??= new HotkeyConfig();
        MuteHotkey.Normalize();

        ExcludedProcessNames = (ExcludedProcessNames ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(NormalizeProcessName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        DisabledKeyboardDeviceIds = new HashSet<string>(
            (DisabledKeyboardDeviceIds ?? []).Where(s => !string.IsNullOrWhiteSpace(s)),
            StringComparer.OrdinalIgnoreCase);

        CustomCombos ??= [];
        foreach (CustomKeyCombo combo in CustomCombos)
        {
            combo.Normalize();
        }

        ProfileCustomizations ??= new Dictionary<string, ProfileCustomization>(StringComparer.OrdinalIgnoreCase);
        var normalizedDict = new Dictionary<string, ProfileCustomization>(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, ProfileCustomization? val) in ProfileCustomizations)
        {
            if (string.IsNullOrWhiteSpace(key) || val is null) continue;
            val.Normalize();
            if (val.GroupOverrides.Count > 0 || val.KeyOverrides.Count > 0)
            {
                normalizedDict[key] = val;
            }
        }
        ProfileCustomizations = normalizedDict;
    }

    public static string NormalizeProcessName(string raw)
    {
        string trimmed = raw.Trim();
        if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^4];
        }
        return trimmed.ToLowerInvariant();
    }
}

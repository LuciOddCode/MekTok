namespace MekTok.Core.Audio;

public sealed record SwitchAcousticParams(
    float BodyFreqStartHz,
    float BodyFreqEndHz,
    float BodyDecayMs,
    float BodyGain,
    float CavityFreqHz,
    float CavityQ,
    float CavityGain,
    float HousingFreqHz,
    float HousingGain,
    float ClickFreqHz,
    float ClickGain,
    float ClickDelayMs,
    bool DoubleClick,
    float SpringPingFreqHz,
    float SpringPingGain,
    float LowPassCutoffHz,
    float UpstrokeFreqMultiplier,
    float UpstrokeGain);

public sealed record SwitchProfileDefinition(
    string Id,
    string Name,
    string ShortName,
    string Tagline,
    string Description,
    string AccentHex,
    SwitchAcousticParams Acoustic);

/// <summary>
/// Registry of MekTok's 9 built-in physically-modeled mechanical switch profiles.
/// </summary>
public static class BuiltInProfiles
{
    public const string MxRedId = "mx-red";
    public const string SmoothLinearId = MxRedId; // Legacy alias kept for backwards compatibility
    public const string TactileBrownId = "tactile-brown";
    public const string MxBlackId = "mx-black";
    public const string CrispClickyId = "crisp-clicky";
    public const string CreamyThockId = "creamy-thock";
    public const string GateronYellowId = "gateron-yellow";
    public const string BoxJadeId = "box-jade";
    public const string ToprePopId = "topre-pop";
    public const string RetroTypewriterId = "retro-typewriter";

    private static readonly IReadOnlyList<SwitchProfileDefinition> AllInternal =
    [
        new(
            Id: MxRedId,
            Name: "MX Red — Light Linear",
            ShortName: "Red",
            Tagline: "Light Linear • Fast & Soft",
            Description: "Crisp light-spring linear travel with a quick muted bottom-out and clean rebound.",
            AccentHex: "#E53935",
            Acoustic: new SwitchAcousticParams(
                BodyFreqStartHz: 370f,
                BodyFreqEndHz: 195f,
                BodyDecayMs: 8.5f,
                BodyGain: 0.72f,
                CavityFreqHz: 300f,
                CavityQ: 1.6f,
                CavityGain: 0.56f,
                HousingFreqHz: 1450f,
                HousingGain: 0.24f,
                ClickFreqHz: 1850f,
                ClickGain: 0.04f,
                ClickDelayMs: 0f,
                DoubleClick: false,
                SpringPingFreqHz: 0f,
                SpringPingGain: 0f,
                LowPassCutoffHz: 3600f,
                UpstrokeFreqMultiplier: 1.32f,
                UpstrokeGain: 0.44f)),

        new(
            Id: TactileBrownId,
            Name: "MX Brown — Soft Tactile",
            ShortName: "Brown",
            Tagline: "Tactile Bump • Balanced & Office-Friendly",
            Description: "Distinct mid-stroke tactile bump paired with a clean, balanced polycarbonate clack.",
            AccentHex: "#9C6644",
            Acoustic: new SwitchAcousticParams(
                BodyFreqStartHz: 390f,
                BodyFreqEndHz: 185f,
                BodyDecayMs: 11.0f,
                BodyGain: 0.72f,
                CavityFreqHz: 340f,
                CavityQ: 2.0f,
                CavityGain: 0.55f,
                HousingFreqHz: 2050f,
                HousingGain: 0.40f,
                ClickFreqHz: 2100f,
                ClickGain: 0.36f,
                ClickDelayMs: 2.8f,
                DoubleClick: false,
                SpringPingFreqHz: 0f,
                SpringPingGain: 0f,
                LowPassCutoffHz: 5600f,
                UpstrokeFreqMultiplier: 1.35f,
                UpstrokeGain: 0.52f)),

        new(
            Id: MxBlackId,
            Name: "MX Black — Heavy Linear",
            ShortName: "Black",
            Tagline: "Heavy Linear • Deep Firm Thud",
            Description: "Weighted heavy-spring linear with a low, dense bottom-out thud and warm dampened top-out.",
            AccentHex: "#4B5263",
            Acoustic: new SwitchAcousticParams(
                BodyFreqStartHz: 250f,
                BodyFreqEndHz: 118f,
                BodyDecayMs: 13.5f,
                BodyGain: 0.95f,
                CavityFreqHz: 195f,
                CavityQ: 2.0f,
                CavityGain: 0.78f,
                HousingFreqHz: 1120f,
                HousingGain: 0.18f,
                ClickFreqHz: 1500f,
                ClickGain: 0.03f,
                ClickDelayMs: 0f,
                DoubleClick: false,
                SpringPingFreqHz: 0f,
                SpringPingGain: 0f,
                LowPassCutoffHz: 2650f,
                UpstrokeFreqMultiplier: 1.24f,
                UpstrokeGain: 0.40f)),

        new(
            Id: CrispClickyId,
            Name: "MX Blue — Crisp Clicky",
            ShortName: "Blue",
            Tagline: "Click-Jacket • Sharp Double Snap",
            Description: "Bright dual-stage click-jacket snap modeled after classic Cherry MX Blue switches.",
            AccentHex: "#1E88E5",
            Acoustic: new SwitchAcousticParams(
                BodyFreqStartHz: 510f,
                BodyFreqEndHz: 240f,
                BodyDecayMs: 9.5f,
                BodyGain: 0.58f,
                CavityFreqHz: 480f,
                CavityQ: 1.9f,
                CavityGain: 0.45f,
                HousingFreqHz: 2900f,
                HousingGain: 0.45f,
                ClickFreqHz: 4600f,
                ClickGain: 0.92f,
                ClickDelayMs: 1.8f,
                DoubleClick: true,
                SpringPingFreqHz: 0f,
                SpringPingGain: 0f,
                LowPassCutoffHz: 9500f,
                UpstrokeFreqMultiplier: 1.42f,
                UpstrokeGain: 0.58f)),

        new(
            Id: CreamyThockId,
            Name: "Creamy Thock (Holy Panda)",
            ShortName: "Thock",
            Tagline: "Lubed PBT • Deep Marbly Thock",
            Description: "Deep, creamy bottom-out resonance with warm rolled-off highs and a lush hollow spacebar.",
            AccentHex: "#F5A623",
            Acoustic: new SwitchAcousticParams(
                BodyFreqStartHz: 295f,
                BodyFreqEndHz: 145f,
                BodyDecayMs: 14.5f,
                BodyGain: 0.90f,
                CavityFreqHz: 225f,
                CavityQ: 2.3f,
                CavityGain: 0.85f,
                HousingFreqHz: 1450f,
                HousingGain: 0.28f,
                ClickFreqHz: 2400f,
                ClickGain: 0.14f,
                ClickDelayMs: 0f,
                DoubleClick: false,
                SpringPingFreqHz: 0f,
                SpringPingGain: 0f,
                LowPassCutoffHz: 3400f,
                UpstrokeFreqMultiplier: 1.32f,
                UpstrokeGain: 0.50f)),

        new(
            Id: GateronYellowId,
            Name: "Gateron Yellow — Milky Smooth",
            ShortName: "Yellow",
            Tagline: "Milky Linear • Warm & Buttery",
            Description: "Beloved milky-housing linear tone with buttery mid-bass warmth and smooth keycap return.",
            AccentHex: "#FBC02D",
            Acoustic: new SwitchAcousticParams(
                BodyFreqStartHz: 310f,
                BodyFreqEndHz: 155f,
                BodyDecayMs: 11.5f,
                BodyGain: 0.84f,
                CavityFreqHz: 255f,
                CavityQ: 1.9f,
                CavityGain: 0.70f,
                HousingFreqHz: 1320f,
                HousingGain: 0.25f,
                ClickFreqHz: 1750f,
                ClickGain: 0.05f,
                ClickDelayMs: 0f,
                DoubleClick: false,
                SpringPingFreqHz: 0f,
                SpringPingGain: 0f,
                LowPassCutoffHz: 3300f,
                UpstrokeFreqMultiplier: 1.29f,
                UpstrokeGain: 0.46f)),

        new(
            Id: BoxJadeId,
            Name: "Box Jade — Thick Click",
            ShortName: "Jade",
            Tagline: "Thick Click-Bar • Punchy Snap",
            Description: "Snappy, heavy thick click-bar snap on both press and return for decisive mechanical feedback.",
            AccentHex: "#26A69A",
            Acoustic: new SwitchAcousticParams(
                BodyFreqStartHz: 460f,
                BodyFreqEndHz: 205f,
                BodyDecayMs: 10.5f,
                BodyGain: 0.68f,
                CavityFreqHz: 410f,
                CavityQ: 2.1f,
                CavityGain: 0.52f,
                HousingFreqHz: 2650f,
                HousingGain: 0.48f,
                ClickFreqHz: 3850f,
                ClickGain: 0.98f,
                ClickDelayMs: 1.4f,
                DoubleClick: true,
                SpringPingFreqHz: 0f,
                SpringPingGain: 0f,
                LowPassCutoffHz: 8600f,
                UpstrokeFreqMultiplier: 1.46f,
                UpstrokeGain: 0.68f)),

        new(
            Id: ToprePopId,
            Name: "Topre — Soft Thoc-Pop",
            ShortName: "Topre",
            Tagline: "Electro-Capacitive • Cushioned Pop",
            Description: "Signature rounded rubber-dome 'thoc-pop' pitch drop with a pillowy, cushioned top-out.",
            AccentHex: "#7E57C2",
            Acoustic: new SwitchAcousticParams(
                BodyFreqStartHz: 420f,
                BodyFreqEndHz: 150f,
                BodyDecayMs: 15.5f,
                BodyGain: 0.94f,
                CavityFreqHz: 285f,
                CavityQ: 2.6f,
                CavityGain: 0.78f,
                HousingFreqHz: 1600f,
                HousingGain: 0.24f,
                ClickFreqHz: 1950f,
                ClickGain: 0.16f,
                ClickDelayMs: 0f,
                DoubleClick: false,
                SpringPingFreqHz: 0f,
                SpringPingGain: 0f,
                LowPassCutoffHz: 3900f,
                UpstrokeFreqMultiplier: 1.48f,
                UpstrokeGain: 0.56f)),

        new(
            Id: RetroTypewriterId,
            Name: "Model M — Vintage Typewriter",
            ShortName: "Model M",
            Tagline: "Buckling Spring • Classic Metallic Ping",
            Description: "Authoritative vintage Model-M buckling-spring snap with subtle metallic spring ring.",
            AccentHex: "#78909C",
            Acoustic: new SwitchAcousticParams(
                BodyFreqStartHz: 560f,
                BodyFreqEndHz: 220f,
                BodyDecayMs: 12.5f,
                BodyGain: 0.78f,
                CavityFreqHz: 430f,
                CavityQ: 2.2f,
                CavityGain: 0.58f,
                HousingFreqHz: 3150f,
                HousingGain: 0.52f,
                ClickFreqHz: 5100f,
                ClickGain: 0.85f,
                ClickDelayMs: 1.4f,
                DoubleClick: true,
                SpringPingFreqHz: 2350f,
                SpringPingGain: 0.22f,
                LowPassCutoffHz: 10500f,
                UpstrokeFreqMultiplier: 1.38f,
                UpstrokeGain: 0.54f))
    ];

    private static readonly Dictionary<string, SwitchProfileDefinition> ByIdInternal =
        AllInternal.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<SwitchProfileDefinition> All => AllInternal;

    public static string NormalizeProfileId(string? id)
    {
        if (string.Equals(id, "smooth-linear", StringComparison.OrdinalIgnoreCase))
        {
            return MxRedId;
        }
        return id ?? string.Empty;
    }

    public static SwitchProfileDefinition GetByIdOrDefault(string? id)
    {
        string normalized = NormalizeProfileId(id);
        if (!string.IsNullOrWhiteSpace(normalized) && ByIdInternal.TryGetValue(normalized, out var found))
        {
            return found;
        }
        return ByIdInternal[CreamyThockId];
    }

    public static bool Exists(string? id)
    {
        string normalized = NormalizeProfileId(id);
        return !string.IsNullOrWhiteSpace(normalized) && ByIdInternal.ContainsKey(normalized);
    }
}

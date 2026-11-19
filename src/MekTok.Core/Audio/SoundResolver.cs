using System.Collections.Concurrent;
using MekTok.Core.Config;
using MekTok.Core.Keyboard;
using MekTok.Core.Packs;

namespace MekTok.Core.Audio;

public sealed record ResolvedKeySound(
    AudioClip Clip,
    float Volume,
    float PlaybackRate,
    float Pan);

/// <summary>
/// Resolves key press/release events through the 3-layer override hierarchy:
/// Per-Key Override -> Key Group Override -> Base Profile (Built-In or Imported Mechvibes Pack).
/// </summary>
public sealed class SoundResolver
{
    private readonly ConcurrentDictionary<string, SynthesizedProfileBank> _synthesizedBanks =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, AudioClip> _customClipCache =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, ImportedSoundPack> _importedPacks =
        new(StringComparer.OrdinalIgnoreCase);

    private int _variantCounter;

    public void RegisterImportedPacks(IEnumerable<ImportedSoundPack> packs)
    {
        _importedPacks.Clear();
        foreach (ImportedSoundPack pack in packs)
        {
            _importedPacks[pack.Id] = pack;
        }
    }

    public void AddOrUpdatePack(ImportedSoundPack pack)
    {
        _importedPacks[pack.Id] = pack;
    }

    public bool RemovePack(string packId) => _importedPacks.TryRemove(packId, out _);

    public IReadOnlyList<ImportedSoundPack> GetImportedPacks() =>
        _importedPacks.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToArray();

    public void RebuildSynthesizedBanks(AcousticSettings acoustics)
    {
        _synthesizedBanks.Clear();
        foreach (SwitchProfileDefinition profile in BuiltInProfiles.All)
        {
            _synthesizedBanks[profile.Id] = SwitchSynthesizer.SynthesizeBank(profile, acoustics);
        }
    }

    public void EnsureAllBanksSynthesized(AcousticSettings acoustics)
    {
        foreach (SwitchProfileDefinition profile in BuiltInProfiles.All)
        {
            _synthesizedBanks.GetOrAdd(profile.Id, _ => SwitchSynthesizer.SynthesizeBank(profile, acoustics));
        }
    }

    public void InvalidateCustomAudioCache(string? path = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            _customClipCache.Clear();
        }
        else
        {
            _customClipCache.TryRemove(path, out _);
        }
    }

    public ResolvedKeySound? Resolve(
        int scanCode,
        bool isKeyUp,
        AppSettings settings,
        Random? rng = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        KeyDefinition? keyDef = Ansi104Layout.TryGetByScanCode(scanCode);
        KeyGroup group = keyDef?.Group ?? KeyGroup.AlphaNumeric;

        SoundOverride? keyOverride = null;
        SoundOverride? groupOverride = null;
        if (settings.ProfileCustomizations.TryGetValue(settings.ActiveProfileId, out ProfileCustomization? custom))
        {
            custom.KeyOverrides.TryGetValue(scanCode, out keyOverride);
            custom.GroupOverrides.TryGetValue(group, out groupOverride);
        }

        // Resolution priority: Key Override -> Group Override -> Base Profile
        bool isMuted = keyOverride?.IsMuted ?? groupOverride?.IsMuted ?? false;
        if (isMuted)
        {
            return null;
        }

        string? customPath = isKeyUp
            ? keyOverride?.CustomReleaseAudioPath ?? groupOverride?.CustomReleaseAudioPath
            : keyOverride?.CustomPressAudioPath ?? groupOverride?.CustomPressAudioPath;

        string? switchOverrideId = keyOverride?.BuiltInSwitchOverrideId
            ?? groupOverride?.BuiltInSwitchOverrideId;

        float overrideVolume = (keyOverride?.VolumeMultiplier ?? 1.0f) *
                               (groupOverride?.VolumeMultiplier ?? 1.0f);

        float overridePitchSemitones = (keyOverride?.PitchOffsetSemitones ?? 0.0f) +
                                       (groupOverride?.PitchOffsetSemitones ?? 0.0f);

        AudioClip? clip = null;
        bool synthesizedSofterUpstroke = false;

        if (!string.IsNullOrWhiteSpace(customPath))
        {
            clip = TryLoadCustomClip(customPath);
        }

        if (clip is null && !string.IsNullOrWhiteSpace(switchOverrideId))
        {
            clip = GetSynthesizedClip(switchOverrideId, group, isKeyUp, settings.Acoustics);
            synthesizedSofterUpstroke = true;
        }

        if (clip is null)
        {
            if (_importedPacks.TryGetValue(settings.ActiveProfileId, out ImportedSoundPack? pack))
            {
                clip = pack.ResolveClip(scanCode, isKeyUp);
                if (clip is null && isKeyUp)
                {
                    // If an imported pack has no dedicated release audio, derive a gentle muted tap
                    // from its press sound by scaling volume/pitch lightly instead of dropping release completely.
                    clip = pack.ResolveClip(scanCode, isKeyUp: false);
                    if (clip is not null)
                    {
                        overrideVolume *= 0.36f;
                        overridePitchSemitones += 4.5f;
                    }
                }
            }
            else
            {
                clip = GetSynthesizedClip(settings.ActiveProfileId, group, isKeyUp, settings.Acoustics);
                synthesizedSofterUpstroke = true;
            }
        }

        if (clip is null || clip.Length == 0)
        {
            return null;
        }

        AcousticSettings acoustics = settings.Acoustics;
        rng ??= Random.Shared;

        // Micro-variation (stops robotic machine-gun repetition)
        float humanize = acoustics.HumanizeAmount;
        float pitchJitterSemitones = humanize > 0.001f
            ? (((float)rng.NextDouble() * 2f) - 1f) * (0.45f * humanize)
            : 0f;
        float volJitter = humanize > 0.001f
            ? 1.0f + ((((float)rng.NextDouble() * 2f) - 1f) * (0.10f * humanize))
            : 1.0f;

        float totalSemitones = acoustics.PitchShiftSemitones + overridePitchSemitones + pitchJitterSemitones;
        float playbackRate = Math.Clamp(MathF.Pow(2.0f, totalSemitones / 12.0f), 0.5f, 2.0f);

        float baseVolume = overrideVolume * volJitter;
        if (isKeyUp)
        {
            baseVolume *= acoustics.KeyUpVolume;
        }

        // Synthesized banks already scale downstroke vs upstroke cleanly; keep headroom safe.
        if (!synthesizedSofterUpstroke && isKeyUp)
        {
            baseVolume *= 0.85f;
        }

        float pan = acoustics.EnableStereoPan && keyDef is not null
            ? Math.Clamp(keyDef.PanPosition * acoustics.StereoPanWidth, -1.0f, 1.0f)
            : 0.0f;

        return new ResolvedKeySound(
            Clip: clip,
            Volume: Math.Clamp(baseVolume, 0.0f, 2.5f),
            PlaybackRate: playbackRate,
            Pan: pan);
    }

    private AudioClip GetSynthesizedClip(
        string profileId,
        KeyGroup group,
        bool isKeyUp,
        AcousticSettings acoustics)
    {
        SwitchProfileDefinition def = BuiltInProfiles.GetByIdOrDefault(profileId);
        SynthesizedProfileBank bank = _synthesizedBanks.GetOrAdd(
            def.Id,
            _ => SwitchSynthesizer.SynthesizeBank(def, acoustics));

        int variant = Interlocked.Increment(ref _variantCounter);
        return bank.GetClip(group, isKeyUp, variant);
    }

    private AudioClip? TryLoadCustomClip(string path)
    {
        if (_customClipCache.TryGetValue(path, out AudioClip? cached))
        {
            return cached;
        }

        try
        {
            if (!File.Exists(path)) return null;
            AudioClip loaded = AudioLoader.LoadClip(path);
            _customClipCache[path] = loaded;
            return loaded;
        }
        catch
        {
            return null;
        }
    }
}


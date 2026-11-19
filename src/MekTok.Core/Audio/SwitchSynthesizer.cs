using MekTok.Core.Config;
using MekTok.Core.Keyboard;

namespace MekTok.Core.Audio;

/// <summary>
/// Pre-rendered stereo/variant PCM bank for a single built-in switch profile.
/// </summary>
public sealed class SynthesizedProfileBank
{
    public SynthesizedProfileBank(
        string profileId,
        Dictionary<KeyGroup, AudioClip[]> downClips,
        Dictionary<KeyGroup, AudioClip[]> upClips)
    {
        ProfileId = profileId;
        DownClips = downClips;
        UpClips = upClips;
    }

    public string ProfileId { get; }
    public Dictionary<KeyGroup, AudioClip[]> DownClips { get; }
    public Dictionary<KeyGroup, AudioClip[]> UpClips { get; }

    public AudioClip GetClip(KeyGroup group, bool isKeyUp, int variantIndex)
    {
        var map = isKeyUp ? UpClips : DownClips;
        if (!map.TryGetValue(group, out AudioClip[]? variants) || variants.Length == 0)
        {
            variants = map[KeyGroup.AlphaNumeric];
        }

        int idx = Math.Abs(variantIndex) % variants.Length;
        return variants[idx];
    }
}

/// <summary>
/// Deterministic physical-modeling synthesizer generating multi-layered mechanical switch clips
/// (actuation click/tactile bump, bottom-out stem thump, keycap cavity, stabilizer hit, and release top-out).
/// </summary>
public static class SwitchSynthesizer
{
    private const int SampleRate = AudioClip.StandardSampleRate;
    private const int VariantsPerGroup = 4;

    public static SynthesizedProfileBank SynthesizeBank(
        SwitchProfileDefinition profile,
        AcousticSettings acoustics)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(acoustics);

        var downClips = new Dictionary<KeyGroup, AudioClip[]>();
        var upClips = new Dictionary<KeyGroup, AudioClip[]>();

        foreach (KeyGroup group in Enum.GetValues<KeyGroup>())
        {
            var downVariants = new AudioClip[VariantsPerGroup];
            var upVariants = new AudioClip[VariantsPerGroup];

            for (int v = 0; v < VariantsPerGroup; v++)
            {
                downVariants[v] = SynthesizeKeyClip(profile, group, isKeyUp: false, v, acoustics);
                upVariants[v] = SynthesizeKeyClip(profile, group, isKeyUp: true, v, acoustics);
            }

            downClips[group] = downVariants;
            upClips[group] = upVariants;
        }

        return new SynthesizedProfileBank(profile.Id, downClips, upClips);
    }

    private static AudioClip SynthesizeKeyClip(
        SwitchProfileDefinition profile,
        KeyGroup group,
        bool isKeyUp,
        int variant,
        AcousticSettings acoustics)
    {
        SwitchAcousticParams p = profile.Acoustic;
        int seed = HashCode.Combine(profile.Id, (int)group, isKeyUp ? 1 : 0, variant);
        var rng = new Random(seed);

        (float freqScale, float decayScale, float stabDelayMs, float stabGain) = GetGroupModifiers(group);

        // Subtle deterministic variance per variant so rapid typing sounds organic.
        float variantFreqJitter = 1.0f + ((float)rng.NextDouble() - 0.5f) * 0.045f;
        float freqMult = freqScale * variantFreqJitter * (isKeyUp ? p.UpstrokeFreqMultiplier : 1.0f);

        float thockScale = 0.55f + (acoustics.ThockAmount * 1.05f);
        float brightScale = 0.50f + (acoustics.BrightnessAmount * 1.10f);

        int durationMs = isKeyUp ? (int)(32 * Math.Min(1.2f, decayScale)) : (int)(78 * decayScale);
        int totalSamples = (SampleRate * durationMs) / 1000;
        float[] buffer = new float[totalSamples];

        // 1. Pre-impact click / tactile leaf transient (key-down or crisp clicky return)
        if (p.ClickGain > 0.01f)
        {
            float clickGain = p.ClickGain * brightScale * (isKeyUp ? 0.35f : 1.0f);
            AddFilteredNoiseImpulse(
                buffer,
                rng,
                startSample: 0,
                centerFreqHz: p.ClickFreqHz * (isKeyUp ? 1.15f : 1.0f) * variantFreqJitter,
                q: 3.4f,
                decayMs: isKeyUp ? 1.8f : 2.6f,
                gain: clickGain);

            if (p.DoubleClick && !isKeyUp)
            {
                int secondClickSample = (int)(0.0018f * SampleRate);
                AddFilteredNoiseImpulse(
                    buffer,
                    rng,
                    startSample: secondClickSample,
                    centerFreqHz: p.ClickFreqHz * 1.38f * variantFreqJitter,
                    q: 3.8f,
                    decayMs: 2.0f,
                    gain: clickGain * 0.80f);
            }
        }

        int impactSample = isKeyUp ? 0 : (int)(p.ClickDelayMs * 0.001f * SampleRate);

        // 2. Stem bottom-out / top-out pitch-swept damped body
        float bodyGain = p.BodyGain * (isKeyUp ? p.UpstrokeGain * 0.72f : thockScale);
        AddPitchSweptModalBody(
            buffer,
            startSample: impactSample,
            freqStartHz: p.BodyFreqStartHz * freqMult,
            freqEndHz: p.BodyFreqEndHz * freqMult,
            decayMs: (isKeyUp ? 7.5f : p.BodyDecayMs) * decayScale,
            gain: bodyGain);

        // 3. Keycap cavity resonance
        float cavityGain = p.CavityGain * (isKeyUp ? p.UpstrokeGain * 0.45f : thockScale);
        AddFilteredNoiseImpulse(
            buffer,
            rng,
            startSample: impactSample,
            centerFreqHz: p.CavityFreqHz * freqMult,
            q: p.CavityQ,
            decayMs: (isKeyUp ? 9.0f : 20.0f) * decayScale,
            gain: cavityGain);

        // 4. Upper housing plastic clack
        AddFilteredNoiseImpulse(
            buffer,
            rng,
            startSample: impactSample,
            centerFreqHz: p.HousingFreqHz * (isKeyUp ? 1.24f : 1.0f) * variantFreqJitter,
            q: 2.1f,
            decayMs: isKeyUp ? 4.5f : 6.5f,
            gain: p.HousingGain * brightScale * (isKeyUp ? 0.85f : 1.0f));

        // 5. Stabilizer secondary transient for Spacebar / Enter / Backspace
        if (!isKeyUp && stabDelayMs > 0f && stabGain > 0f)
        {
            int stabSample = impactSample + (int)(stabDelayMs * 0.001f * SampleRate);
            AddPitchSweptModalBody(
                buffer,
                startSample: stabSample,
                freqStartHz: p.BodyFreqStartHz * freqMult * 0.82f,
                freqEndHz: p.BodyFreqEndHz * freqMult * 0.78f,
                decayMs: p.BodyDecayMs * decayScale * 0.85f,
                gain: bodyGain * stabGain);
        }

        // 6. Optional metallic spring ping (Retro Typewriter)
        if (!isKeyUp && p.SpringPingGain > 0.01f && p.SpringPingFreqHz > 0f)
        {
            AddDampedHarmonic(
                buffer,
                startSample: impactSample,
                freqHz: p.SpringPingFreqHz * variantFreqJitter,
                decayMs: 14.0f,
                gain: p.SpringPingGain * brightScale);
        }

        // 7. Tone-shaping Low-Pass Filter + anti-click fade out + peak normalization
        float cutoff = Math.Clamp(p.LowPassCutoffHz * (0.65f + (acoustics.BrightnessAmount * 0.70f)), 1100f, 16000f);
        ApplySinglePoleLowPass(buffer, cutoff);
        ApplyTailFade(buffer, fadeSamples: (int)(SampleRate * 0.004));
        NormalizePeak(buffer, targetPeak: isKeyUp ? 0.65f : 0.90f);

        return new AudioClip(buffer, SampleRate);
    }

    private static (float freqScale, float decayScale, float stabDelayMs, float stabGain) GetGroupModifiers(KeyGroup group) =>
        group switch
        {
            KeyGroup.Spacebar => (0.62f, 1.35f, 4.2f, 0.44f),
            KeyGroup.Enter => (0.76f, 1.18f, 3.0f, 0.34f),
            KeyGroup.Backspace => (0.80f, 1.14f, 2.8f, 0.30f),
            KeyGroup.TabCaps => (0.88f, 1.08f, 0f, 0f),
            KeyGroup.Modifiers => (0.86f, 1.08f, 0f, 0f),
            KeyGroup.Numpad => (0.94f, 1.03f, 0f, 0f),
            KeyGroup.Navigation => (1.02f, 0.97f, 0f, 0f),
            KeyGroup.Function => (1.06f, 0.94f, 0f, 0f),
            _ => (1.0f, 1.0f, 0f, 0f)
        };

    private static void AddPitchSweptModalBody(
        float[] buffer,
        int startSample,
        float freqStartHz,
        float freqEndHz,
        float decayMs,
        float gain)
    {
        if (gain <= 0.001f || startSample >= buffer.Length) return;

        float tauSamples = Math.Max(8f, (decayMs * 0.001f) * SampleRate);
        float glideSamples = Math.Max(16f, SampleRate * 0.0065f);
        float phase = 0f;

        for (int i = startSample; i < buffer.Length; i++)
        {
            int n = i - startSample;
            float env = MathF.Exp(-n / tauSamples);
            if (env < 0.0005f) break;

            float attack = Math.Min(1.0f, n / 12.0f);
            float glideT = MathF.Exp(-n / glideSamples);
            float freq = freqEndHz + ((freqStartHz - freqEndHz) * glideT);
            phase += (2f * MathF.PI * freq) / SampleRate;

            buffer[i] += MathF.Sin(phase) * env * attack * gain;
        }
    }

    private static void AddDampedHarmonic(
        float[] buffer,
        int startSample,
        float freqHz,
        float decayMs,
        float gain)
    {
        if (gain <= 0.001f || startSample >= buffer.Length) return;

        float tauSamples = Math.Max(8f, (decayMs * 0.001f) * SampleRate);
        float phase1 = 0f;
        float phase2 = 0f;

        for (int i = startSample; i < buffer.Length; i++)
        {
            int n = i - startSample;
            float env = MathF.Exp(-n / tauSamples);
            if (env < 0.0005f) break;

            phase1 += (2f * MathF.PI * freqHz) / SampleRate;
            phase2 += (2f * MathF.PI * freqHz * 1.62f) / SampleRate;
            buffer[i] += ((0.7f * MathF.Sin(phase1)) + (0.3f * MathF.Sin(phase2))) * env * gain;
        }
    }

    private static void AddFilteredNoiseImpulse(
        float[] buffer,
        Random rng,
        int startSample,
        float centerFreqHz,
        float q,
        float decayMs,
        float gain)
    {
        if (gain <= 0.001f || startSample >= buffer.Length) return;

        float clampedFreq = Math.Clamp(centerFreqHz, 80f, SampleRate * 0.42f);
        float omega = (2f * MathF.PI * clampedFreq) / SampleRate;
        float alpha = MathF.Sin(omega) / (2f * Math.Max(0.4f, q));
        float cosW = MathF.Cos(omega);
        float a0 = 1f + alpha;

        float b0 = alpha / a0;
        float b1 = 0f;
        float b2 = -alpha / a0;
        float a1 = (-2f * cosW) / a0;
        float a2 = (1f - alpha) / a0;

        float x1 = 0f, x2 = 0f, y1 = 0f, y2 = 0f;
        float tauSamples = Math.Max(8f, (decayMs * 0.001f) * SampleRate);

        for (int i = startSample; i < buffer.Length; i++)
        {
            int n = i - startSample;
            float env = MathF.Exp(-n / tauSamples);
            if (env < 0.0002f && MathF.Abs(y1) < 0.0002f) break;

            float white = ((float)rng.NextDouble() * 2f) - 1f;
            float x0 = white * env;
            float y0 = (b0 * x0) + (b1 * x1) + (b2 * x2) - (a1 * y1) - (a2 * y2);

            x2 = x1;
            x1 = x0;
            y2 = y1;
            y1 = y0;

            buffer[i] += y0 * gain;
        }
    }

    private static void ApplySinglePoleLowPass(float[] buffer, float cutoffHz)
    {
        float rc = 1f / (2f * MathF.PI * cutoffHz);
        float dt = 1f / SampleRate;
        float alpha = dt / (rc + dt);

        float prev = 0f;
        for (int i = 0; i < buffer.Length; i++)
        {
            prev += alpha * (buffer[i] - prev);
            buffer[i] = prev;
        }
    }

    private static void ApplyTailFade(float[] buffer, int fadeSamples)
    {
        int count = Math.Min(fadeSamples, buffer.Length);
        for (int i = 0; i < count; i++)
        {
            float factor = (count - 1 - i) / (float)count;
            buffer[buffer.Length - count + i] *= factor;
        }
    }

    private static void NormalizePeak(float[] buffer, float targetPeak)
    {
        float maxAbs = 0f;
        for (int i = 0; i < buffer.Length; i++)
        {
            float sample = buffer[i];
            if (!float.IsFinite(sample))
            {
                buffer[i] = 0f;
                continue;
            }
            float abs = MathF.Abs(sample);
            if (abs > maxAbs) maxAbs = abs;
        }

        if (maxAbs < 1e-5f) return;

        float scale = targetPeak / maxAbs;
        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] = Math.Clamp(buffer[i] * scale, -1.0f, 1.0f);
        }
    }
}


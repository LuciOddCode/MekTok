using System.Collections.Concurrent;
using NAudio.Wave;

namespace MekTok.Core.Audio;

/// <summary>
/// Lock-free polyphonic stereo sample mixer implementing <see cref="ISampleProvider"/>.
/// Pre-allocates a fixed voice pool so WASAPI audio callbacks perform zero heap allocations.
/// </summary>
public sealed class KeyboardAudioMixer : ISampleProvider
{
    public const int MaxVoices = 48;

    private readonly struct VoiceTrigger
    {
        public VoiceTrigger(AudioClip clip, float leftGain, float rightGain, float playbackRate)
        {
            Clip = clip;
            LeftGain = leftGain;
            RightGain = rightGain;
            PlaybackRate = playbackRate;
        }

        public AudioClip Clip { get; }
        public float LeftGain { get; }
        public float RightGain { get; }
        public float PlaybackRate { get; }
    }

    private struct VoiceState
    {
        public AudioClip? Clip;
        public double Position;
        public double PlaybackRate;
        public float LeftGain;
        public float RightGain;
        public long Sequence;
        public bool Active;
    }

    private readonly ConcurrentQueue<VoiceTrigger> _pendingTriggers = new();
    private readonly VoiceState[] _voices = new VoiceState[MaxVoices];
    private long _sequenceCounter;
    private int _clearRequested;
    private volatile float _masterVolume = 0.75f;

    public KeyboardAudioMixer()
    {
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(AudioClip.StandardSampleRate, 2);
    }

    public WaveFormat WaveFormat { get; }

    public float MasterVolume
    {
        get => _masterVolume;
        set => _masterVolume = Math.Clamp(value, 0.0f, 1.0f);
    }

    public void Trigger(ResolvedKeySound sound)
    {
        ArgumentNullException.ThrowIfNull(sound);
        if (sound.Clip.Length == 0 || sound.Volume <= 0.0001f)
        {
            return;
        }

        // Equal-power stereo pan law: pan in [-1, 1] -> theta in [0, pi/2]
        float clampedPan = Math.Clamp(sound.Pan, -1.0f, 1.0f);
        float theta = (clampedPan + 1.0f) * (MathF.PI * 0.25f);
        float leftGain = MathF.Cos(theta) * sound.Volume;
        float rightGain = MathF.Sin(theta) * sound.Volume;

        _pendingTriggers.Enqueue(new VoiceTrigger(
            sound.Clip,
            leftGain,
            rightGain,
            Math.Clamp(sound.PlaybackRate, 0.5f, 2.0f)));
    }

    public void ClearAllVoices()
    {
        while (_pendingTriggers.TryDequeue(out _)) { }
        Interlocked.Exchange(ref _clearRequested, 1);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        // Do not use Array.Clear: NAudio's SampleToWaveProvider passes WaveBuffer.FloatBuffer,
        // a union-punned byte[] where Array.Clear only zeroes count bytes (25% of floats).
        for (int i = offset; i < offset + count; i++)
        {
            buffer[i] = 0f;
        }

        if (Interlocked.Exchange(ref _clearRequested, 0) != 0)
        {
            for (int i = 0; i < _voices.Length; i++)
            {
                _voices[i].Active = false;
                _voices[i].Clip = null;
            }
        }

        while (_pendingTriggers.TryDequeue(out VoiceTrigger trigger))
        {
            AllocateVoice(trigger);
        }

        int frameCount = count / 2;
        float master = _masterVolume;

        for (int v = 0; v < _voices.Length; v++)
        {
            ref VoiceState voice = ref _voices[v];
            AudioClip? clip = voice.Clip;
            if (!voice.Active || clip is null)
            {
                continue;
            }

            float[] samples = clip.Samples;
            int len = samples.Length;
            double pos = voice.Position;
            double rate = voice.PlaybackRate;
            float lGain = voice.LeftGain * master;
            float rGain = voice.RightGain * master;

            for (int f = 0; f < frameCount; f++)
            {
                int idx0 = (int)pos;
                if (idx0 >= len)
                {
                    voice.Active = false;
                    voice.Clip = null;
                    break;
                }

                float frac = (float)(pos - idx0);
                float s0 = samples[idx0];
                float s1 = (idx0 + 1 < len) ? samples[idx0 + 1] : 0f;
                float mono = s0 + ((s1 - s0) * frac);

                int outIdx = offset + (f * 2);
                buffer[outIdx] += mono * lGain;
                buffer[outIdx + 1] += mono * rGain;

                pos += rate;
            }

            if (voice.Active)
            {
                voice.Position = pos;
                if (pos >= len)
                {
                    voice.Active = false;
                    voice.Clip = null;
                }
            }
        }

        // Smooth soft-clipper on master stereo bus to protect speakers under heavy chords
        for (int i = offset; i < offset + count; i++)
        {
            float s = buffer[i];
            if (s > 0.85f || s < -0.85f)
            {
                buffer[i] = MathF.Tanh(s);
            }
        }

        return count;
    }

    private void AllocateVoice(in VoiceTrigger trigger)
    {
        int targetIndex = -1;
        long oldestSeq = long.MaxValue;

        for (int i = 0; i < _voices.Length; i++)
        {
            if (!_voices[i].Active)
            {
                targetIndex = i;
                break;
            }

            if (_voices[i].Sequence < oldestSeq)
            {
                oldestSeq = _voices[i].Sequence;
                targetIndex = i;
            }
        }

        if (targetIndex < 0) targetIndex = 0;

        ref VoiceState slot = ref _voices[targetIndex];
        slot.Clip = trigger.Clip;
        slot.Position = 0.0;
        slot.PlaybackRate = trigger.PlaybackRate;
        slot.LeftGain = trigger.LeftGain;
        slot.RightGain = trigger.RightGain;
        slot.Sequence = ++_sequenceCounter;
        slot.Active = true;
    }
}


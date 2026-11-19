using System.Collections.Concurrent;
using NAudio.Wave;

namespace MekTok.Core.Audio;

/// <summary>
/// Lock-free polyphonic stereo sample mixer implementing <see cref="ISampleProvider"/> and direct Span pull.
/// Pre-allocates a fixed voice pool so real-time audio callbacks perform zero heap allocations.
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

        EnqueueTrigger(sound.Clip, sound.Volume, sound.Pan, sound.PlaybackRate);
    }

    public void TriggerClip(AudioClip clip, float volume = 1f, float pan = 0f, float playbackRate = 1f)
    {
        ArgumentNullException.ThrowIfNull(clip);
        if (clip.Length == 0 || volume <= 0.0001f)
        {
            return;
        }

        EnqueueTrigger(clip, volume, pan, playbackRate);
    }

    private void EnqueueTrigger(AudioClip clip, float volume, float pan, float playbackRate)
    {
        // Equal-power stereo pan law: pan in [-1, 1] -> theta in [0, pi/2]
        float clampedPan = Math.Clamp(pan, -1.0f, 1.0f);
        float theta = (clampedPan + 1.0f) * (MathF.PI * 0.25f);
        float leftGain = MathF.Cos(theta) * volume;
        float rightGain = MathF.Sin(theta) * volume;

        _pendingTriggers.Enqueue(new VoiceTrigger(
            clip,
            leftGain,
            rightGain,
            Math.Clamp(playbackRate, 0.5f, 2.0f)));
    }

    public void ClearAllVoices()
    {
        while (_pendingTriggers.TryDequeue(out _)) { }
        Interlocked.Exchange(ref _clearRequested, 1);
    }

    public int Read(Span<float> destination)
    {
        destination.Clear();
        PrepareVoicesForRead();

        int frameCount = destination.Length / 2;
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

                int outIdx = f * 2;
                destination[outIdx] += mono * lGain;
                destination[outIdx + 1] += mono * rGain;

                pos += rate;
            }

            FinalizeVoicePosition(ref voice, pos, len);
        }

        for (int i = 0; i < destination.Length; i++)
        {
            float s = destination[i];
            if (s > 0.85f || s < -0.85f)
            {
                destination[i] = MathF.Tanh(s);
            }
        }

        return destination.Length;
    }

    public int Read(float[] buffer, int offset, int count) =>
        Read(buffer.AsSpan(offset, count));

    private void PrepareVoicesForRead()
    {
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
    }

    private static void FinalizeVoicePosition(ref VoiceState voice, double pos, int len)
    {
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

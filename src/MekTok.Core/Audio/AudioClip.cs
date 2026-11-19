using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NLayer.NAudioSupport;

namespace MekTok.Core.Audio;

/// <summary>
/// Immutable mono 48 kHz IEEE-float PCM audio buffer cached in RAM for zero-allocation mixing.
/// </summary>
public sealed class AudioClip
{
    public const int StandardSampleRate = 48000;

    public AudioClip(float[] samples, int sampleRate = StandardSampleRate)
    {
        Samples = samples ?? throw new ArgumentNullException(nameof(samples));
        SampleRate = sampleRate;
    }

    public float[] Samples { get; }
    public int SampleRate { get; }
    public int Length => Samples.Length;

    /// <summary>
    /// Extracts a millisecond slice from this clip with 2 ms edge ramps to avoid clicks.
    /// </summary>
    public AudioClip Slice(int startMs, int durationMs)
    {
        if (Samples.Length == 0 || durationMs <= 0)
        {
            return new AudioClip(Array.Empty<float>(), SampleRate);
        }

        int startSample = Math.Clamp((int)((long)startMs * SampleRate / 1000), 0, Samples.Length);
        int requestedSamples = (int)((long)durationMs * SampleRate / 1000);
        int count = Math.Clamp(requestedSamples, 0, Samples.Length - startSample);

        if (count <= 0)
        {
            return new AudioClip(Array.Empty<float>(), SampleRate);
        }

        float[] slice = new float[count];
        Array.Copy(Samples, startSample, slice, 0, count);

        int fadeSamples = Math.Min(count / 4, (int)(SampleRate * 0.002));
        for (int i = 0; i < fadeSamples; i++)
        {
            float gain = (i + 1) / (float)fadeSamples;
            slice[i] *= gain;
            slice[count - 1 - i] *= gain;
        }

        return new AudioClip(slice, SampleRate);
    }
}

/// <summary>
/// Decodes WAV, OGG Vorbis, and MP3 files into normalized mono 48 kHz <see cref="AudioClip"/> instances.
/// </summary>
public static class AudioLoader
{
    public const long DefaultMaxFileBytes = 50L * 1024 * 1024;

    public static AudioClip LoadClip(
        string filePath,
        int maxDurationMs = 5000,
        long maxFileBytes = DefaultMaxFileBytes)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Audio file path cannot be empty.", nameof(filePath));
        }

        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("Audio file not found.", filePath);
        }

        if (fileInfo.Length > maxFileBytes)
        {
            throw new InvalidOperationException(
                $"Audio file exceeds maximum allowed size ({maxFileBytes / (1024 * 1024)} MB).");
        }

        string ext = fileInfo.Extension.ToLowerInvariant();
        IDisposable? disposableReader = null;
        ISampleProvider provider;

        try
        {
            if (ext == ".ogg")
            {
                var vorbis = new VorbisWaveReader(filePath);
                disposableReader = vorbis;
                provider = vorbis;
            }
            else if (ext == ".wav")
            {
                var wave = new WaveFileReader(filePath);
                disposableReader = wave;
                provider = wave.ToSampleProvider();
            }
            else if (ext == ".mp3")
            {
                var mp3 = new Mp3FileReaderBase(
                    filePath,
                    wf => new Mp3FrameDecompressor(wf));
                disposableReader = mp3;
                provider = mp3.ToSampleProvider();
            }
            else
            {
                throw new NotSupportedException(
                    $"Unsupported audio format '{ext}'. Supported formats: .wav, .ogg, .mp3.");
            }

            if (provider.WaveFormat.SampleRate != AudioClip.StandardSampleRate)
            {
                provider = new WdlResamplingSampleProvider(provider, AudioClip.StandardSampleRate);
            }

            int channels = Math.Max(1, provider.WaveFormat.Channels);
            long maxFrames = Math.Max(1L, (long)maxDurationMs * AudioClip.StandardSampleRate / 1000L);
            var monoSamples = new List<float>((int)Math.Min(maxFrames, AudioClip.StandardSampleRate * 4));

            float[] readBuffer = new float[4096 * channels];
            while (monoSamples.Count < maxFrames)
            {
                int framesRemaining = (int)Math.Min(4096, maxFrames - monoSamples.Count);
                int samplesRead = provider.Read(readBuffer.AsSpan(0, framesRemaining * channels));
                if (samplesRead <= 0)
                {
                    break;
                }

                int framesRead = samplesRead / channels;
                for (int f = 0; f < framesRead; f++)
                {
                    int baseIdx = f * channels;
                    float sum = 0f;
                    for (int c = 0; c < channels; c++)
                    {
                        sum += readBuffer[baseIdx + c];
                    }
                    monoSamples.Add(Math.Clamp(sum / channels, -1.0f, 1.0f));
                }
            }

            return new AudioClip(monoSamples.ToArray(), AudioClip.StandardSampleRate);
        }
        finally
        {
            disposableReader?.Dispose();
        }
    }
}


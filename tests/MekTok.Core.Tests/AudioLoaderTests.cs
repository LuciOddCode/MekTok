using MekTok.Core.Audio;
using NAudio.Wave;
using Xunit;

namespace MekTok.Core.Tests;

public class AudioLoaderTests : IDisposable
{
    private readonly string _tempRoot;

    public AudioLoaderTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "MekTok_AudioLoaderTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public void LoadClip_DecodesWavAndResamplesTo48kHzMono()
    {
        string wavPath = Path.Combine(_tempRoot, "tone.wav");
        var format = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
        using (var writer = new WaveFileWriter(wavPath, format))
        {
            int frames = 4410; // 100 ms
            float[] stereo = new float[frames * 2];
            for (int f = 0; f < frames; f++)
            {
                float val = 0.4f * MathF.Sin(2f * MathF.PI * 440f * f / 44100f);
                stereo[f * 2] = val;
                stereo[(f * 2) + 1] = val;
            }
            writer.WriteSamples(stereo, 0, stereo.Length);
        }

        AudioClip clip = AudioLoader.LoadClip(wavPath);

        Assert.Equal(AudioClip.StandardSampleRate, clip.SampleRate);
        Assert.InRange(clip.Length, 4600, 5000);
        Assert.Contains(clip.Samples, s => MathF.Abs(s) > 0.1f);
    }

    [Fact]
    public void LoadClip_DecodesMp3PureManagedWithoutWindowsCodecs()
    {
        string mp3Path = Path.Combine(_tempRoot, "silent_valid.mp3");
        // Build 3 minimal valid MPEG-1 Layer III frames (44.1 kHz, 128 kbps, mono, 417 bytes/frame)
        const int frameBytes = 417;
        byte[] data = new byte[frameBytes * 3];
        for (int f = 0; f < 3; f++)
        {
            int offset = f * frameBytes;
            data[offset] = 0xFF;
            data[offset + 1] = 0xFB;
            data[offset + 2] = 0x90;
            data[offset + 3] = 0xC4;
        }
        File.WriteAllBytes(mp3Path, data);

        AudioClip clip = AudioLoader.LoadClip(mp3Path);

        Assert.Equal(AudioClip.StandardSampleRate, clip.SampleRate);
        Assert.True(clip.Length > 0);
        Assert.All(clip.Samples, s => Assert.True(float.IsFinite(s)));
    }

    [Fact]
    public void LoadClip_ThrowsNotSupportedForUnknownExtension()
    {
        string badPath = Path.Combine(_tempRoot, "clip.flac");
        File.WriteAllText(badPath, "fLaC");

        Assert.Throws<NotSupportedException>(() => AudioLoader.LoadClip(badPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }
}


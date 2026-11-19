using MekTok.Core.Audio;
using NAudio.Wave.SampleProviders;
using Xunit;

namespace MekTok.Core.Tests;

public class KeyboardAudioMixerTests
{
    [Fact]
    public void Read_MixesVoicesWithStereoPanningAndReturnsSilenceAfterFinish()
    {
        var mixer = new KeyboardAudioMixer { MasterVolume = 1.0f };
        float[] samples = Enumerable.Repeat(0.5f, 128).ToArray();
        var clip = new AudioClip(samples);

        // Hard left pan (-1.0)
        mixer.Trigger(new ResolvedKeySound(clip, Volume: 1.0f, PlaybackRate: 1.0f, Pan: -1.0f));

        float[] outBuf = new float[256]; // 128 stereo frames
        int read = mixer.Read(outBuf, 0, outBuf.Length);

        Assert.Equal(outBuf.Length, read);
        Assert.True(outBuf[0] > 0.4f);           // Left channel active
        Assert.True(MathF.Abs(outBuf[1]) < 0.001f); // Right channel near silent

        // Next buffer should be completely silent once voice finishes
        float[] silentBuf = new float[256];
        mixer.Read(silentBuf, 0, silentBuf.Length);
        Assert.All(silentBuf, s => Assert.Equal(0f, s));
    }

    [Fact]
    public void Read_SoftClipperPreventsClippingUnderHeavyPolyphony()
    {
        var mixer = new KeyboardAudioMixer { MasterVolume = 1.0f };
        float[] samples = Enumerable.Repeat(0.95f, 256).ToArray();
        var clip = new AudioClip(samples);

        for (int i = 0; i < 12; i++)
        {
            mixer.Trigger(new ResolvedKeySound(clip, Volume: 1.0f, PlaybackRate: 1.0f, Pan: 0.0f));
        }

        float[] outBuf = new float[512];
        mixer.Read(outBuf, 0, outBuf.Length);

        Assert.All(outBuf, s => Assert.InRange(s, -1.0f, 1.0f));
    }

    [Fact]
    public void SampleToWaveProvider_ClearsEntireReusedByteBufferAfterVoiceCompletes()
    {
        var mixer = new KeyboardAudioMixer { MasterVolume = 1.0f };
        var waveProvider = new SampleToWaveProvider(mixer);

        float[] samples = Enumerable.Repeat(0.6f, 64).ToArray();
        var clip = new AudioClip(samples);
        mixer.Trigger(new ResolvedKeySound(clip, Volume: 1.0f, PlaybackRate: 1.0f, Pan: 0.0f));

        byte[] reusedBytes = new byte[1024]; // 128 stereo float frames (8 bytes/frame)
        int firstRead = waveProvider.Read(reusedBytes, 0, reusedBytes.Length);
        Assert.Equal(reusedBytes.Length, firstRead);
        Assert.Contains(reusedBytes, b => b != 0);

        // Next read into the SAME reused byte[] must zero 100% of bytes, including the upper 75%.
        int secondRead = waveProvider.Read(reusedBytes, 0, reusedBytes.Length);
        Assert.Equal(reusedBytes.Length, secondRead);
        Assert.All(reusedBytes, b => Assert.Equal((byte)0, b));
    }
}


using MekTok.Core.Audio;
using MekTok.Core.Config;
using MekTok.Core.Keyboard;
using Xunit;

namespace MekTok.Core.Tests;

public class SoundResolverTests
{
    [Fact]
    public void Resolve_FollowsKeyOverGroupOverProfilePrecedence()
    {
        var settings = new AppSettings
        {
            ActiveProfileId = BuiltInProfiles.CreamyThockId,
            Acoustics = new AcousticSettings
            {
                HumanizeAmount = 0f,
                EnableStereoPan = true,
                StereoPanWidth = 0.5f
            }
        };

        ProfileCustomization cust = settings.GetOrCreateCustomization(settings.ActiveProfileId);
        cust.GroupOverrides[KeyGroup.Modifiers] = new SoundOverride
        {
            VolumeMultiplier = 1.4f,
            PitchOffsetSemitones = -2.0f
        };

        const int lShift = 0x002A;
        const int rShift = 0x0036;
        cust.KeyOverrides[lShift] = new SoundOverride
        {
            VolumeMultiplier = 0.6f,
            PitchOffsetSemitones = 5.0f
        };

        var resolver = new SoundResolver();
        resolver.RebuildSynthesizedBanks(settings.Acoustics);

        ResolvedKeySound? lShiftSound = resolver.Resolve(lShift, isKeyUp: false, settings, new Random(1));
        ResolvedKeySound? rShiftSound = resolver.Resolve(rShift, isKeyUp: false, settings, new Random(1));

        Assert.NotNull(lShiftSound);
        Assert.NotNull(rShiftSound);

        // KeyOverride pitch (+5) stacks on GroupOverride pitch (-2) -> +3 semitones vs RShift's -2 semitones
        Assert.True(lShiftSound!.PlaybackRate > rShiftSound!.PlaybackRate);
        // LShift is on left, RShift is on right -> stereo pan signs oppose
        Assert.True(lShiftSound.Pan < 0f);
        Assert.True(rShiftSound.Pan > 0f);
    }

    [Fact]
    public void Resolve_ReturnsNullWhenKeyOrGroupIsMuted()
    {
        var settings = new AppSettings();
        ProfileCustomization cust = settings.GetOrCreateCustomization(settings.ActiveProfileId);
        cust.GroupOverrides[KeyGroup.Numpad] = new SoundOverride { IsMuted = true };

        var resolver = new SoundResolver();
        resolver.RebuildSynthesizedBanks(settings.Acoustics);

        Assert.Null(resolver.Resolve(0x0047, isKeyUp: false, settings)); // Num7 muted via group
        Assert.NotNull(resolver.Resolve(0x001E, isKeyUp: false, settings)); // KeyA unaffected
    }

    [Fact]
    public void Resolve_ZerosPanWhenStereoDisabled()
    {
        var settings = new AppSettings();
        settings.Acoustics.EnableStereoPan = false;

        var resolver = new SoundResolver();
        resolver.RebuildSynthesizedBanks(settings.Acoustics);

        ResolvedKeySound? esc = resolver.Resolve(0x0001, isKeyUp: false, settings);
        Assert.NotNull(esc);
        Assert.Equal(0f, esc!.Pan);
    }
}


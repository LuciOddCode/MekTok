using MekTok.Core.Audio;
using MekTok.Core.Config;
using MekTok.Core.Keyboard;
using Xunit;

namespace MekTok.Core.Tests;

public class SwitchSynthesizerTests
{
    [Fact]
    public void SynthesizeBank_AllNineProfilesProduceFiniteBoundedBuffers()
    {
        Assert.Equal(9, BuiltInProfiles.All.Count);
        var acoustics = new AcousticSettings();

        foreach (SwitchProfileDefinition profile in BuiltInProfiles.All)
        {
            SynthesizedProfileBank bank = SwitchSynthesizer.SynthesizeBank(profile, acoustics);

            foreach (KeyGroup group in Enum.GetValues<KeyGroup>())
            {
                AudioClip down = bank.GetClip(group, isKeyUp: false, 0);
                AudioClip up = bank.GetClip(group, isKeyUp: true, 0);

                Assert.NotEmpty(down.Samples);
                Assert.NotEmpty(up.Samples);
                Assert.True(up.Length < down.Length);

                foreach (float sample in down.Samples)
                {
                    Assert.True(float.IsFinite(sample));
                    Assert.InRange(sample, -1.0f, 1.0f);
                }

                foreach (float sample in up.Samples)
                {
                    Assert.True(float.IsFinite(sample));
                    Assert.InRange(sample, -1.0f, 1.0f);
                }
            }
        }
    }

    [Fact]
    public void BuiltInProfiles_IncludesClassicSwitchStylesAndLegacyAlias()
    {
        string[] expectedIds =
        [
            BuiltInProfiles.MxRedId,
            BuiltInProfiles.TactileBrownId,
            BuiltInProfiles.MxBlackId,
            BuiltInProfiles.CrispClickyId,
            BuiltInProfiles.CreamyThockId,
            BuiltInProfiles.GateronYellowId,
            BuiltInProfiles.BoxJadeId,
            BuiltInProfiles.ToprePopId,
            BuiltInProfiles.RetroTypewriterId
        ];

        foreach (string id in expectedIds)
        {
            Assert.True(BuiltInProfiles.Exists(id));
            SwitchProfileDefinition def = BuiltInProfiles.GetByIdOrDefault(id);
            Assert.False(string.IsNullOrWhiteSpace(def.ShortName));
            Assert.False(string.IsNullOrWhiteSpace(def.Name));
        }

        // Legacy smooth-linear ID aliases cleanly to MX Red
        Assert.True(BuiltInProfiles.Exists("smooth-linear"));
        Assert.Equal(BuiltInProfiles.MxRedId, BuiltInProfiles.GetByIdOrDefault("smooth-linear").Id);
    }

    [Fact]
    public void SynthesizeBank_SpacebarIsLongerThanAlphaAndExtremeSlidersStaySafe()
    {
        var extremeAcoustics = new AcousticSettings
        {
            ThockAmount = 1.0f,
            BrightnessAmount = 1.0f
        };

        SynthesizedProfileBank bank = SwitchSynthesizer.SynthesizeBank(
            BuiltInProfiles.GetByIdOrDefault(BuiltInProfiles.CreamyThockId),
            extremeAcoustics);

        AudioClip alpha = bank.GetClip(KeyGroup.AlphaNumeric, isKeyUp: false, 0);
        AudioClip space = bank.GetClip(KeyGroup.Spacebar, isKeyUp: false, 0);

        Assert.True(space.Length > alpha.Length);
        Assert.All(space.Samples, s => Assert.True(float.IsFinite(s) && s is >= -1.0f and <= 1.0f));
    }
}

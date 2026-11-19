using MekTok.Core.Keyboard;
using Xunit;

namespace MekTok.Core.Tests;

public class Ansi104LayoutTests
{
    [Fact]
    public void AllKeys_ContainsExact104UniqueKeysAndTklIs87()
    {
        Assert.Equal(104, Ansi104Layout.AllKeys.Count);
        Assert.Equal(87, Ansi104Layout.GetKeys(includeNumpad: false).Count);
        Assert.Equal(104, Ansi104Layout.AllKeys.Select(k => k.ScanCode).Distinct().Count());
        Assert.Equal(104, Ansi104Layout.AllKeys.Select(k => k.KeyId).Distinct().Count());
    }

    [Fact]
    public void AllKeys_HavePanPositionsWithinNormalizedRange()
    {
        foreach (KeyDefinition key in Ansi104Layout.AllKeys)
        {
            Assert.InRange(key.PanPosition, -1.0f, 1.0f);
        }

        KeyDefinition esc = Ansi104Layout.ByKeyId["Esc"];
        KeyDefinition numEnter = Ansi104Layout.ByKeyId["NumEnter"];
        Assert.True(esc.PanPosition < 0f);
        Assert.True(numEnter.PanPosition > 0f);
    }

    [Fact]
    public void NormalizeRawScanCode_FiltersOverrunAndEncodesExtendedKeys()
    {
        // Fake Windows overrun / E0 shift prefix packet must be ignored
        Assert.Null(Ansi104Layout.NormalizeRawScanCode(0x2A, 0x00, 0xFF));

        // Standard key A
        Assert.Equal(0x001E, Ansi104Layout.NormalizeRawScanCode(0x1E, 0x00, 0x41));

        // Extended key ArrowUp (RI_KEY_E0 = 0x02)
        Assert.Equal(0xE048, Ansi104Layout.NormalizeRawScanCode(0x48, 0x02, 0x26));

        // Pause/Break (RI_KEY_E1 = 0x04)
        Assert.Equal(0xE11D, Ansi104Layout.NormalizeRawScanCode(0x1D, 0x04, 0x13));

        // NumLock regardless of E0 quirk
        Assert.Equal(0x0045, Ansi104Layout.NormalizeRawScanCode(0x45, 0x02, 0x90));
    }
}


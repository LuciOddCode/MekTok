using MekTok.Core.Keyboard;
using MekTok.Platform.Desktop.Input;
using SharpHook.Data;
using Xunit;

namespace MekTok.Core.Tests;

public class SharpHookKeyMapTests
{
    [Fact]
    public void SharpHookKeyMap_MapsEveryKeyInAnsi104Layout()
    {
        var mappedScanCodes = new HashSet<int>(SharpHookKeyMap.AllMappings.Values);

        foreach (KeyDefinition key in Ansi104Layout.AllKeys)
        {
            Assert.True(
                mappedScanCodes.Contains(key.ScanCode),
                $"Ansi104Layout key '{key.KeyId}' (0x{key.ScanCode:X4}) has no SharpHook KeyCode mapping.");
        }
    }

    [Theory]
    [InlineData(KeyCode.VcA, "30")]            // KeyA
    [InlineData(KeyCode.VcSpace, "57")]        // Space
    [InlineData(KeyCode.VcEnter, "28")]        // Enter
    [InlineData(KeyCode.VcBackspace, "14")]    // Backspace
    [InlineData(KeyCode.VcLeftShift, "42")]    // LShift
    [InlineData(KeyCode.VcRightControl, "3613")] // Mechvibes extended RCtrl
    [InlineData(KeyCode.VcUp, "61000")]        // Mechvibes / uiohook Up arrow
    [InlineData(KeyCode.VcLeftMeta, "3675")]   // LWin / Super / Cmd
    [InlineData(KeyCode.VcNumPadEnter, "3612")] // NumEnter
    public void SharpHookKeyMap_AlignsWithMechvibesKeyMap(KeyCode sharpHookKey, string mechvibesCode)
    {
        Assert.True(SharpHookKeyMap.TryMapKeyCode(sharpHookKey, out int sharpHookScanCode));
        Assert.True(MechvibesKeyMap.TryMapMechvibesCode(mechvibesCode, out int mechvibesScanCode));
        Assert.Equal(mechvibesScanCode, sharpHookScanCode);
    }
}


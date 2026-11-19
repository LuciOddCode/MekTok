using MekTok.Core.Config;
using MekTok.Core.Keyboard;
using Xunit;

namespace MekTok.Core.Tests;

public class CustomKeyComboTests
{
    [Fact]
    public void Normalize_SanitizesFieldsAndRemovesDuplicates()
    {
        var combo = new CustomKeyCombo
        {
            Id = "",
            Name = "   ",
            Volume = 2.5f,
            ScanCodes = [0x0015, 0x002A, 0x0015, 0x0002, -5, 0]
        };

        combo.Normalize();

        Assert.False(string.IsNullOrWhiteSpace(combo.Id));
        Assert.Equal("Custom Combo", combo.Name);
        Assert.Equal(1.5f, combo.Volume);
        // Deduplicated, filtered positive scan codes in ascending order: [0x0002, 0x0015, 0x002A]
        Assert.Equal(3, combo.ScanCodes.Count);
        Assert.Equal(0x0002, combo.ScanCodes[0]);
        Assert.Equal(0x0015, combo.ScanCodes[1]);
        Assert.Equal(0x002A, combo.ScanCodes[2]);
    }

    [Fact]
    public void FormatKeysDisplay_ReturnsExpectedBadges()
    {
        var combo = new CustomKeyCombo
        {
            ScanCodes = [0x002A, 0x0015, 0x0002] // Shift (Left), Y, 1
        };

        string formatted = combo.FormatKeysDisplay();

        Assert.Contains("Shift", formatted);
        Assert.Contains("Y", formatted);
        Assert.Contains("1", formatted);
    }

    [Fact]
    public void Settings_Serialization_PreservesCustomCombos()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "MekTok_ComboTest_" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SettingsStore(tempDir);
            var settings = store.Load();

            var combo = new CustomKeyCombo
            {
                Name = "YEAH! Chime",
                Volume = 1.2f,
                SuppressSwitchSound = true,
                ScanCodes = [0x002A, 0x0015] // Shift + Y
            };
            settings.CustomCombos.Add(combo);

            store.Save(settings);

            var reloaded = store.Load();
            Assert.Single(reloaded.CustomCombos);
            Assert.Equal("YEAH! Chime", reloaded.CustomCombos[0].Name);
            Assert.Equal(1.2f, reloaded.CustomCombos[0].Volume);
            Assert.True(reloaded.CustomCombos[0].SuppressSwitchSound);
            Assert.Equal(2, reloaded.CustomCombos[0].ScanCodes.Count);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void SettingsStore_CopyComboAudioFile_CreatesCopyInCombosDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "MekTok_CopyTest_" + Guid.NewGuid().ToString("N"));
        var dummyAudioFile = Path.Combine(Path.GetTempPath(), "temp_sample.wav");
        try
        {
            File.WriteAllText(dummyAudioFile, "RIFF_DUMMY_DATA");
            var store = new SettingsStore(tempDir);

            string copied = store.CopyComboAudioFile(dummyAudioFile);

            Assert.True(File.Exists(copied));
            Assert.StartsWith(store.CombosDirectory, copied, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(dummyAudioFile)) File.Delete(dummyAudioFile);
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }
}


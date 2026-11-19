using MekTok.Core.Audio;
using MekTok.Core.Config;
using MekTok.Core.Keyboard;
using Xunit;

namespace MekTok.Core.Tests;

public class SettingsStoreTests : IDisposable
{
    private readonly string _tempRoot;

    public SettingsStoreTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "MekTok_SettingsTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsSettingsAndOverrides()
    {
        var store = new SettingsStore(_tempRoot);
        var settings = new AppSettings
        {
            IsEnabled = false,
            MasterVolume = 0.42f,
            ActiveProfileId = BuiltInProfiles.ToprePopId,
            MuteInFullscreen = true,
            ExcludedProcessNames = ["Valorant.exe", "zoom"],
            Theme = ThemePreference.Light
        };

        ProfileCustomization custom = settings.GetOrCreateCustomization(BuiltInProfiles.ToprePopId);
        custom.GroupOverrides[KeyGroup.Spacebar] = new SoundOverride
        {
            VolumeMultiplier = 1.25f,
            PitchOffsetSemitones = -1.5f
        };
        custom.KeyOverrides[0x001C] = new SoundOverride { IsMuted = true };

        store.Save(settings);
        AppSettings loaded = store.Load();

        Assert.False(loaded.IsEnabled);
        Assert.Equal(0.42f, loaded.MasterVolume, 3);
        Assert.Equal(BuiltInProfiles.ToprePopId, loaded.ActiveProfileId);
        Assert.True(loaded.MuteInFullscreen);
        Assert.Contains("valorant", loaded.ExcludedProcessNames);
        Assert.Contains("zoom", loaded.ExcludedProcessNames);
        Assert.Equal(ThemePreference.Light, loaded.Theme);

        ProfileCustomization loadedCust = loaded.GetOrCreateCustomization(BuiltInProfiles.ToprePopId);
        Assert.Equal(1.25f, loadedCust.GroupOverrides[KeyGroup.Spacebar].VolumeMultiplier, 3);
        Assert.True(loadedCust.KeyOverrides[0x001C].IsMuted);
    }

    [Fact]
    public void Load_RecoversGracefullyFromCorruptJson()
    {
        var store = new SettingsStore(_tempRoot);
        store.EnsureDirectories();
        File.WriteAllText(store.SettingsFilePath, "{ broken json !!");

        AppSettings recovered = store.Load();

        Assert.True(recovered.IsEnabled);
        Assert.Equal(BuiltInProfiles.CreamyThockId, recovered.ActiveProfileId);
        Assert.True(File.Exists(store.SettingsFilePath + ".corrupt"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors.
        }
    }
}


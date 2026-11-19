using System.Text.Json;
using MekTok.Core.Config;
using MekTok.Core.Keyboard;
using Xunit;

namespace MekTok.Core.Tests;

public class HotkeyConfigTests : IDisposable
{
    private readonly string _tempRoot;

    public HotkeyConfigTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "MekTok_HotkeyTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public void SettingsStore_MigratesLegacyWindowsVirtualKeyAndWinModifier()
    {
        var store = new SettingsStore(_tempRoot);
        store.EnsureDirectories();

        // Legacy Windows v1.x settings.json format: uses virtualKey (0x4B = 'K') and "Win" modifier
        File.WriteAllText(
            store.SettingsFilePath,
            """
            {
              "muteHotkey": {
                "enabled": true,
                "modifiers": "Control, Alt, Win",
                "virtualKey": 75
              }
            }
            """);

        AppSettings loaded = store.Load();

        Assert.False(File.Exists(store.SettingsFilePath + ".corrupt"));
        Assert.True(loaded.MuteHotkey.Enabled);
        Assert.Equal(HotkeyKey.K, loaded.MuteHotkey.Key);
        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Meta, loaded.MuteHotkey.Modifiers);
        Assert.Null(loaded.MuteHotkey.VirtualKey);

        store.Save(loaded);
        string savedJson = File.ReadAllText(store.SettingsFilePath);
        Assert.DoesNotContain("virtualKey", savedJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Meta", savedJson, StringComparison.Ordinal);
        Assert.Contains("\"K\"", savedJson, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsStore_RoundTripsPlatformNeutralHotkey()
    {
        var store = new SettingsStore(_tempRoot);
        var settings = new AppSettings
        {
            MuteHotkey = new HotkeyConfig
            {
                Enabled = true,
                Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.Meta,
                Key = HotkeyKey.F9
            }
        };

        store.Save(settings);
        AppSettings reloaded = store.Load();

        Assert.Equal(HotkeyKey.F9, reloaded.MuteHotkey.Key);
        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.Meta, reloaded.MuteHotkey.Modifiers);
        Assert.Null(reloaded.MuteHotkey.VirtualKey);
    }

    [Fact]
    public void HotkeyModifiersJsonConverter_ParsesNumericAndAliasStrings()
    {
        string json = """{"enabled":true,"modifiers":"Ctrl, Option, Super","key":"Space"}""";
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        HotkeyConfig? parsed = JsonSerializer.Deserialize<HotkeyConfig>(json, options);

        Assert.NotNull(parsed);
        Assert.Equal(HotkeyKey.Space, parsed!.Key);
        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Meta, parsed.Modifiers);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }
}

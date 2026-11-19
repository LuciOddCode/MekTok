using System.Text.Json;
using System.Text.Json.Serialization;

namespace MekTok.Core.Config;

/// <summary>
/// Persists MekTok settings atomically under %AppData%\MekTok (or an injected root for tests).
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly object _ioLock = new();

    public SettingsStore(string? rootDirectory = null)
    {
        RootDirectory = string.IsNullOrWhiteSpace(rootDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MekTok")
            : Path.GetFullPath(rootDirectory);

        SettingsFilePath = Path.Combine(RootDirectory, "settings.json");
        PacksDirectory = Path.Combine(RootDirectory, "packs");
        CustomAudioDirectory = Path.Combine(RootDirectory, "custom-audio");
        LogsDirectory = Path.Combine(RootDirectory, "logs");
    }

    public string RootDirectory { get; }
    public string SettingsFilePath { get; }
    public string PacksDirectory { get; }
    public string CustomAudioDirectory { get; }
    public string LogsDirectory { get; }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(PacksDirectory);
        Directory.CreateDirectory(CustomAudioDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }

    public AppSettings Load()
    {
        lock (_ioLock)
        {
            EnsureDirectories();
            if (!File.Exists(SettingsFilePath))
            {
                var defaults = new AppSettings();
                defaults.Normalize();
                return defaults;
            }

            try
            {
                string json = File.ReadAllText(SettingsFilePath);
                AppSettings? loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (loaded is null)
                {
                    return CreateDefaults();
                }

                loaded.Normalize();
                return loaded;
            }
            catch (Exception)
            {
                TryBackupCorruptSettings();
                return CreateDefaults();
            }
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        lock (_ioLock)
        {
            EnsureDirectories();
            settings.Normalize();

            string tempPath = SettingsFilePath + ".tmp";
            string json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, SettingsFilePath, overwrite: true);
        }
    }

    private void TryBackupCorruptSettings()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                string backup = SettingsFilePath + ".corrupt";
                File.Copy(SettingsFilePath, backup, overwrite: true);
            }
        }
        catch
        {
            // Ignore backup errors during recovery.
        }
    }

    private static AppSettings CreateDefaults()
    {
        var defaults = new AppSettings();
        defaults.Normalize();
        return defaults;
    }
}


using System.IO.Compression;
using System.Text.Json;
using MekTok.Core.Audio;
using MekTok.Core.Keyboard;

namespace MekTok.Core.Packs;

/// <summary>
/// Imports and loads Mechvibes sound packs (both single-sprite and multi-file config.json formats)
/// with strict path-traversal and size-limit checks.
/// </summary>
public static class MechvibesPackImporter
{
    private const long MaxZipEntryBytes = AudioLoader.DefaultMaxFileBytes;
    private const long MaxZipTotalBytes = 250L * 1024 * 1024;
    private const int MaxSpriteDurationMs = 180_000;

    public static async Task<ImportedSoundPack> ImportFromFolderAsync(
        string sourceFolder,
        string packsRootDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceFolder) || !Directory.Exists(sourceFolder))
        {
            throw new DirectoryNotFoundException("Selected sound pack folder does not exist.");
        }

        string sourceFull = Path.GetFullPath(sourceFolder);
        string configPath = FindConfigFile(sourceFull)
            ?? throw new FileNotFoundException("No config.json found in the selected sound pack folder.");
        string configDir = Path.GetDirectoryName(configPath)!;

        // Validate config and load clips from source before copying into AppData.
        ImportedSoundPack validated = await Task.Run(
            () => LoadPackFromDirectory(configDir, idOverride: null),
            cancellationToken);

        Directory.CreateDirectory(packsRootDirectory);
        string targetDir = AllocatePackDirectory(packsRootDirectory, validated.Name);
        CopyDirectoryRecursively(configDir, targetDir);

        return LoadPackFromDirectory(targetDir, idOverride: Path.GetFileName(targetDir));
    }

    public static async Task<ImportedSoundPack> ImportFromZipAsync(
        string zipPath,
        string packsRootDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
        {
            throw new FileNotFoundException("Selected ZIP archive does not exist.", zipPath);
        }

        string stagingDir = Path.Combine(
            Path.GetTempPath(),
            "MekTok_PackStage_" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(stagingDir);
            string normalizedStagingRoot = EnsureTrailingSeparator(Path.GetFullPath(stagingDir));

            await Task.Run(() =>
            {
                using ZipArchive archive = ZipFile.OpenRead(zipPath);
                long totalExtracted = 0;

                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        continue; // Directory entry
                    }

                    if (entry.Length > MaxZipEntryBytes)
                    {
                        throw new InvalidOperationException(
                            $"Archive entry '{entry.Name}' exceeds maximum allowed file size.");
                    }

                    totalExtracted += entry.Length;
                    if (totalExtracted > MaxZipTotalBytes)
                    {
                        throw new InvalidOperationException(
                            "Archive exceeds maximum allowed extracted pack size (250 MB).");
                    }

                    string destPath = Path.GetFullPath(Path.Combine(stagingDir, entry.FullName));
                    if (!destPath.StartsWith(normalizedStagingRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            $"Blocked unsafe archive entry path: '{entry.FullName}'.");
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                    entry.ExtractToFile(destPath, overwrite: true);
                }
            }, cancellationToken);

            return await ImportFromFolderAsync(stagingDir, packsRootDirectory, cancellationToken);
        }
        finally
        {
            TryDeleteDirectory(stagingDir);
        }
    }

    public static IReadOnlyList<ImportedSoundPack> LoadInstalledPacks(string packsRootDirectory)
    {
        var loaded = new List<ImportedSoundPack>();
        if (!Directory.Exists(packsRootDirectory))
        {
            return loaded;
        }

        foreach (string dir in Directory.GetDirectories(packsRootDirectory))
        {
            try
            {
                string? configFile = FindConfigFile(dir);
                if (configFile is null) continue;

                string packDir = Path.GetDirectoryName(configFile)!;
                string id = Path.GetFileName(dir);
                ImportedSoundPack pack = LoadPackFromDirectory(packDir, idOverride: id);
                if (pack.PressClips.Count > 0)
                {
                    loaded.Add(pack);
                }
            }
            catch
            {
                // Skip broken user pack folders rather than failing app startup.
            }
        }

        return loaded;
    }

    public static ImportedSoundPack LoadPackFromDirectory(string packDirectory, string? idOverride = null)
    {
        string normalizedPackRoot = EnsureTrailingSeparator(Path.GetFullPath(packDirectory));
        string configPath = Path.Combine(packDirectory, "config.json");
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException("Mechvibes config.json not found.", configPath);
        }

        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(configPath));
        JsonElement root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Invalid Mechvibes config.json root object.");
        }

        string name = root.TryGetProperty("name", out JsonElement nameProp) && nameProp.ValueKind == JsonValueKind.String
            ? (nameProp.GetString()?.Trim() ?? Path.GetFileName(packDirectory))
            : Path.GetFileName(packDirectory);

        if (string.IsNullOrWhiteSpace(name))
        {
            name = "Imported Pack";
        }

        string? defineType = root.TryGetProperty("key_define_type", out JsonElement typeProp) &&
                             typeProp.ValueKind == JsonValueKind.String
            ? typeProp.GetString()?.Trim().ToLowerInvariant()
            : null;

        if (!root.TryGetProperty("defines", out JsonElement definesProp) ||
            definesProp.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Mechvibes config.json is missing a valid 'defines' object.");
        }

        // Infer mode if key_define_type is omitted (Mechvibes v2 style)
        if (string.IsNullOrEmpty(defineType))
        {
            defineType = InferDefineType(definesProp);
        }

        var pressClips = new Dictionary<int, AudioClip>();
        var releaseClips = new Dictionary<int, AudioClip>();

        if (defineType == "single")
        {
            string? soundFile = root.TryGetProperty("sound", out JsonElement soundProp) &&
                                soundProp.ValueKind == JsonValueKind.String
                ? soundProp.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(soundFile))
            {
                throw new InvalidDataException("Single-file Mechvibes config.json is missing 'sound'.");
            }

            string resolvedSound = ResolveSafeChildPath(normalizedPackRoot, packDirectory, soundFile);
            AudioClip masterSprite = AudioLoader.LoadClip(resolvedSound, maxDurationMs: MaxSpriteDurationMs);

            AudioClip? masterReleaseSprite = null;
            if (root.TryGetProperty("soundup", out JsonElement soundUpProp) &&
                soundUpProp.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(soundUpProp.GetString()))
            {
                string resolvedUp = ResolveSafeChildPath(normalizedPackRoot, packDirectory, soundUpProp.GetString()!);
                if (File.Exists(resolvedUp))
                {
                    masterReleaseSprite = AudioLoader.LoadClip(resolvedUp, maxDurationMs: MaxSpriteDurationMs);
                }
            }

            foreach (JsonProperty entry in definesProp.EnumerateObject())
            {
                if (entry.Value.ValueKind != JsonValueKind.Array) continue;
                if (!MechvibesKeyMap.TryMapMechvibesCode(entry.Name, out int scanCode)) continue;

                if (!TryReadTimingArray(entry.Value, out int startMs, out int durationMs)) continue;

                AudioClip slice = masterSprite.Slice(startMs, durationMs);
                if (slice.Length > 0)
                {
                    pressClips[scanCode] = slice;
                }

                if (masterReleaseSprite is not null)
                {
                    AudioClip upSlice = masterReleaseSprite.Slice(startMs, durationMs);
                    if (upSlice.Length > 0)
                    {
                        releaseClips[scanCode] = upSlice;
                    }
                }
            }
        }
        else if (defineType == "multi")
        {
            var clipCache = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);

            foreach (JsonProperty entry in definesProp.EnumerateObject())
            {
                if (entry.Value.ValueKind != JsonValueKind.String) continue;
                string? relFile = entry.Value.GetString();
                if (string.IsNullOrWhiteSpace(relFile)) continue;
                if (!MechvibesKeyMap.TryMapMechvibesCode(entry.Name, out int scanCode)) continue;

                string resolvedFile = ResolveSafeChildPath(normalizedPackRoot, packDirectory, relFile);
                if (!File.Exists(resolvedFile)) continue;

                if (!clipCache.TryGetValue(resolvedFile, out AudioClip? clip))
                {
                    clip = AudioLoader.LoadClip(resolvedFile);
                    clipCache[resolvedFile] = clip;
                }

                if (clip.Length > 0)
                {
                    pressClips[scanCode] = clip;
                }
            }
        }
        else
        {
            throw new InvalidDataException($"Unsupported Mechvibes key_define_type: '{defineType}'.");
        }

        if (pressClips.Count == 0)
        {
            throw new InvalidDataException("Sound pack contained no valid mapped key sounds.");
        }

        AudioClip? fallbackPress = pressClips.TryGetValue(0x001E, out var keyA)
            ? keyA
            : pressClips.Values.FirstOrDefault();

        AudioClip? fallbackRelease = releaseClips.TryGetValue(0x001E, out var keyAUp)
            ? keyAUp
            : releaseClips.Values.FirstOrDefault();

        string id = string.IsNullOrWhiteSpace(idOverride)
            ? "pack-" + SanitizeId(name)
            : idOverride;

        return new ImportedSoundPack(
            id: id,
            name: name,
            directoryPath: packDirectory,
            pressClips: pressClips,
            releaseClips: releaseClips,
            fallbackPressClip: fallbackPress,
            fallbackReleaseClip: fallbackRelease);
    }

    public static void DeletePackDirectory(ImportedSoundPack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        TryDeleteDirectory(pack.DirectoryPath);
    }

    private static string InferDefineType(JsonElement definesProp)
    {
        foreach (JsonProperty entry in definesProp.EnumerateObject())
        {
            if (entry.Value.ValueKind == JsonValueKind.Array) return "single";
            if (entry.Value.ValueKind == JsonValueKind.String) return "multi";
        }
        return "multi";
    }

    private static bool TryReadTimingArray(JsonElement arr, out int startMs, out int durationMs)
    {
        startMs = 0;
        durationMs = 0;
        if (arr.GetArrayLength() < 2) return false;

        JsonElement first = arr[0];
        JsonElement second = arr[1];
        if (first.ValueKind != JsonValueKind.Number || second.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        startMs = (int)Math.Round(first.GetDouble());
        durationMs = (int)Math.Round(second.GetDouble());
        return startMs >= 0 && durationMs > 0;
    }

    private static string ResolveSafeChildPath(
        string normalizedPackRoot,
        string packDirectory,
        string relativePath)
    {
        string combined = Path.GetFullPath(Path.Combine(packDirectory, relativePath));
        if (!combined.StartsWith(normalizedPackRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Blocked unsafe file path outside pack directory: '{relativePath}'.");
        }
        return combined;
    }

    private static string? FindConfigFile(string folder)
    {
        string direct = Path.Combine(folder, "config.json");
        if (File.Exists(direct)) return direct;

        foreach (string subDir in Directory.GetDirectories(folder))
        {
            string nested = Path.Combine(subDir, "config.json");
            if (File.Exists(nested)) return nested;
        }
        return null;
    }

    private static string AllocatePackDirectory(string packsRoot, string packName)
    {
        string baseSlug = "pack-" + SanitizeId(packName);
        string candidate = Path.Combine(packsRoot, baseSlug);
        int suffix = 2;
        while (Directory.Exists(candidate))
        {
            candidate = Path.Combine(packsRoot, $"{baseSlug}-{suffix}");
            suffix++;
        }
        Directory.CreateDirectory(candidate);
        return candidate;
    }

    private static string SanitizeId(string raw)
    {
        var chars = raw.Trim()
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();

        string slug = new string(chars).Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(slug) ? "custom-pack" : slug[..Math.Min(slug.Length, 48)];
    }

    private static void CopyDirectoryRecursively(string sourceDir, string targetDir)
    {
        string normalizedSource = EnsureTrailingSeparator(Path.GetFullPath(sourceDir));
        foreach (string file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(normalizedSource, file);
            string dest = Path.Combine(targetDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private static string EnsureTrailingSeparator(string fullPath) =>
        fullPath.EndsWith(Path.DirectorySeparatorChar) || fullPath.EndsWith(Path.AltDirectorySeparatorChar)
            ? fullPath
            : fullPath + Path.DirectorySeparatorChar;
}


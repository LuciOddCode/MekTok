using System.IO.Compression;
using MekTok.Core.Packs;
using NAudio.Wave;
using Xunit;

namespace MekTok.Core.Tests;

public class MechvibesPackImporterTests : IDisposable
{
    private readonly string _tempRoot;

    public MechvibesPackImporterTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "MekTok_ImporterTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public async Task ImportFromFolderAsync_ParsesSingleSpritePackAndSlicesCorrectly()
    {
        string srcDir = Path.Combine(_tempRoot, "single-src");
        string packsDir = Path.Combine(_tempRoot, "packs");
        Directory.CreateDirectory(srcDir);

        CreateTestWav(Path.Combine(srcDir, "sound.wav"), durationMs: 600);
        File.WriteAllText(
            Path.Combine(srcDir, "config.json"),
            """
            {
              "name": "Test Single Pack",
              "key_define_type": "single",
              "sound": "sound.wav",
              "defines": {
                "30": [0, 120],
                "57": [200, 150]
              }
            }
            """);

        ImportedSoundPack pack = await MechvibesPackImporter.ImportFromFolderAsync(srcDir, packsDir);

        Assert.Equal("Test Single Pack", pack.Name);
        Assert.True(pack.PressClips.ContainsKey(0x001E)); // KeyA
        Assert.True(pack.PressClips.ContainsKey(0x0039)); // Space
        Assert.True(pack.PressClips[0x0039].Length > pack.PressClips[0x001E].Length);
    }

    [Fact]
    public async Task ImportFromZipAsync_ParsesMultiFilePack()
    {
        string stageDir = Path.Combine(_tempRoot, "zip-stage");
        string packsDir = Path.Combine(_tempRoot, "packs-zip");
        string zipPath = Path.Combine(_tempRoot, "testpack.zip");
        Directory.CreateDirectory(stageDir);

        CreateTestWav(Path.Combine(stageDir, "alpha.wav"), durationMs: 90);
        CreateTestWav(Path.Combine(stageDir, "space.wav"), durationMs: 140);
        File.WriteAllText(
            Path.Combine(stageDir, "config.json"),
            """
            {
              "name": "Multi Zip Pack",
              "key_define_type": "multi",
              "defines": {
                "30": "alpha.wav",
                "57": "space.wav"
              }
            }
            """);

        ZipFile.CreateFromDirectory(stageDir, zipPath);

        ImportedSoundPack pack = await MechvibesPackImporter.ImportFromZipAsync(zipPath, packsDir);

        Assert.Equal("Multi Zip Pack", pack.Name);
        Assert.Equal(2, pack.PressClips.Count);
    }

    [Fact]
    public async Task ImportFromFolderAsync_BlocksPathTraversalOutsidePackDirectory()
    {
        string outsideWav = Path.Combine(_tempRoot, "secret.wav");
        CreateTestWav(outsideWav, durationMs: 100);

        string srcDir = Path.Combine(_tempRoot, "evil-pack");
        string packsDir = Path.Combine(_tempRoot, "packs-evil");
        Directory.CreateDirectory(srcDir);

        File.WriteAllText(
            Path.Combine(srcDir, "config.json"),
            """
            {
              "name": "Evil Traversal Pack",
              "key_define_type": "multi",
              "defines": {
                "30": "../secret.wav"
              }
            }
            """);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => MechvibesPackImporter.ImportFromFolderAsync(srcDir, packsDir));
    }

    [Fact]
    public async Task ImportFromZipAsync_BlocksZipSlipEntries()
    {
        string zipPath = Path.Combine(_tempRoot, "zipslip.zip");
        string packsDir = Path.Combine(_tempRoot, "packs-slip");

        using (ZipArchive archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            ZipArchiveEntry entry = archive.CreateEntry("../../escaped.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("malicious");
        }

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => MechvibesPackImporter.ImportFromZipAsync(zipPath, packsDir));
    }

    private static void CreateTestWav(string path, int durationMs)
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 1);
        int sampleCount = (48000 * durationMs) / 1000;
        using var writer = new WaveFileWriter(path, format);
        float[] data = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            data[i] = 0.25f * MathF.Sin(2f * MathF.PI * 440f * i / 48000f);
        }
        writer.WriteSamples(data, 0, data.Length);
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


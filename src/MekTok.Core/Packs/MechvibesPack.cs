using MekTok.Core.Audio;

namespace MekTok.Core.Packs;

/// <summary>
/// Loaded Mechvibes custom sound pack with decoded press/release PCM clips keyed by canonical scan code.
/// </summary>
public sealed class ImportedSoundPack
{
    public ImportedSoundPack(
        string id,
        string name,
        string directoryPath,
        Dictionary<int, AudioClip> pressClips,
        Dictionary<int, AudioClip> releaseClips,
        AudioClip? fallbackPressClip,
        AudioClip? fallbackReleaseClip)
    {
        Id = id;
        Name = name;
        DirectoryPath = directoryPath;
        PressClips = pressClips;
        ReleaseClips = releaseClips;
        FallbackPressClip = fallbackPressClip;
        FallbackReleaseClip = fallbackReleaseClip;
    }

    public string Id { get; }
    public string Name { get; }
    public string DirectoryPath { get; }
    public Dictionary<int, AudioClip> PressClips { get; }
    public Dictionary<int, AudioClip> ReleaseClips { get; }
    public AudioClip? FallbackPressClip { get; }
    public AudioClip? FallbackReleaseClip { get; }

    public AudioClip? ResolveClip(int scanCode, bool isKeyUp)
    {
        if (isKeyUp)
        {
            if (ReleaseClips.TryGetValue(scanCode, out var rel)) return rel;
            return FallbackReleaseClip;
        }

        if (PressClips.TryGetValue(scanCode, out var press)) return press;
        return FallbackPressClip;
    }
}


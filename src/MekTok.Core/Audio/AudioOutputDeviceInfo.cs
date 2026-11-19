namespace MekTok.Core.Audio;

public sealed record AudioOutputDeviceInfo(
    string Id,
    string FriendlyName,
    bool IsDefault);


namespace MekTok.Core.Platform;

public sealed record KeyboardDeviceInfo(string DeviceId, string DisplayName);

public readonly record struct RawKeyInputEvent(int CanonicalScanCode, bool IsKeyUp, string DeviceId);


using MekTok.Core.Audio;

namespace MekTok.Core.Platform;

public interface IAudioOutputEngine : IDisposable
{
    KeyboardAudioMixer Mixer { get; }
    bool HasActiveOutput { get; }
    string ActiveDeviceName { get; }
    double? EstimatedLatencyMs { get; }

    event EventHandler? DevicesChanged;
    event EventHandler? ActiveDeviceChanged;

    void Start(string? preferredDeviceId);
    void SetPreferredDevice(string? deviceId);
    IReadOnlyList<AudioOutputDeviceInfo> ListOutputDevices();
}


using System.Diagnostics;
using MekTok.Core.Audio;
using MekTok.Core.Platform;
using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Backends.MiniAudio.Devices;
using SoundFlow.Backends.MiniAudio.Enums;
using SoundFlow.Enums;
using SoundFlow.Structs;

namespace MekTok.Platform.Desktop.Audio;

public sealed record AudioLatencyDiagnostics(
    bool HasActiveOutput,
    string DeviceName,
    string BackendName,
    long CallbackCount,
    int LastCallbackFrames,
    double AverageCallbackIntervalMs,
    double? EstimatedLatencyMs);

/// <summary>
/// Cross-platform low-latency audio playback engine powered by SoundFlow / miniaudio
/// (PipeWire/PulseAudio/ALSA on Linux, CoreAudio on macOS, WASAPI on Windows).
/// Pulls directly from <see cref="KeyboardAudioMixer.Read(Span{float})"/> with zero callback allocations.
/// </summary>
public sealed class MiniAudioOutputEngine : IAudioOutputEngine
{
    private static readonly AudioFormat PlaybackFormat = new()
    {
        Format = SampleFormat.F32,
        SampleRate = AudioClip.StandardSampleRate,
        Channels = 2,
        Layout = AudioFormat.GetLayoutFromChannels(2)
    };

    private sealed class MixerSoundComponent : SoundComponent
    {
        private readonly KeyboardAudioMixer _mixer;
        private long _callbackCount;
        private int _lastCallbackFrames;
        private long _lastTimestamp;
        private double _avgIntervalMs;

        public MixerSoundComponent(AudioEngine engine, AudioFormat format, KeyboardAudioMixer mixer)
            : base(engine, format)
        {
            _mixer = mixer;
            Name = "MekTokKeyboardMixer";
        }

        public long CallbackCount => Interlocked.Read(ref _callbackCount);
        public int LastCallbackFrames => Volatile.Read(ref _lastCallbackFrames);
        public double AverageCallbackIntervalMs => Volatile.Read(ref _avgIntervalMs);

        public void ResetStats()
        {
            Interlocked.Exchange(ref _callbackCount, 0);
            Volatile.Write(ref _lastCallbackFrames, 0);
            Interlocked.Exchange(ref _lastTimestamp, 0);
            Volatile.Write(ref _avgIntervalMs, 0.0);
        }

        protected override void GenerateAudio(Span<float> buffer, int channels)
        {
            long now = Stopwatch.GetTimestamp();
            long prev = Interlocked.Exchange(ref _lastTimestamp, now);
            if (prev != 0)
            {
                double elapsedMs = (now - prev) * 1000.0 / Stopwatch.Frequency;
                double current = Volatile.Read(ref _avgIntervalMs);
                double next = current <= 0.001 ? elapsedMs : (current * 0.88) + (elapsedMs * 0.12);
                Volatile.Write(ref _avgIntervalMs, next);
            }

            int ch = channels > 0 ? channels : 2;
            Volatile.Write(ref _lastCallbackFrames, buffer.Length / ch);
            Interlocked.Increment(ref _callbackCount);

            _mixer.Read(buffer);
        }
    }

    private readonly SemaphoreSlim _reinitLock = new(1, 1);
    private MiniAudioEngine? _engine;
    private AudioPlaybackDevice? _playbackDevice;
    private MixerSoundComponent? _mixerComponent;
    private string? _preferredDeviceId;
    private double? _configuredBufferLatencyMs;
    private bool _disposed;

    public MiniAudioOutputEngine()
    {
        Mixer = new KeyboardAudioMixer();
        try
        {
            _engine = new MiniAudioEngine();
        }
        catch
        {
            _engine = null;
        }
    }

    public KeyboardAudioMixer Mixer { get; }
    public bool HasActiveOutput { get; private set; }
    public string ActiveDeviceName { get; private set; } = "No Audio Output";

    public double? EstimatedLatencyMs
    {
        get
        {
            if (!HasActiveOutput) return null;
            double avg = _mixerComponent?.AverageCallbackIntervalMs ?? 0;
            return avg > 0.1 ? avg : _configuredBufferLatencyMs;
        }
    }

    public event EventHandler? DevicesChanged;
    public event EventHandler? ActiveDeviceChanged;

    public void Start(string? preferredDeviceId)
    {
        _preferredDeviceId = string.IsNullOrWhiteSpace(preferredDeviceId) ? null : preferredDeviceId;
        ReinitializeOutput();
    }

    public void SetPreferredDevice(string? deviceId)
    {
        _preferredDeviceId = string.IsNullOrWhiteSpace(deviceId) ? null : deviceId;
        _ = Task.Run(ReinitializeOutput);
    }

    public IReadOnlyList<AudioOutputDeviceInfo> ListOutputDevices()
    {
        if (_engine is null || _disposed)
        {
            return Array.Empty<AudioOutputDeviceInfo>();
        }

        try
        {
            _engine.UpdateAudioDevicesInfo();
            DeviceInfo[] devices = _engine.PlaybackDevices;
            if (devices.Length == 0)
            {
                return Array.Empty<AudioOutputDeviceInfo>();
            }

            var list = new List<AudioOutputDeviceInfo>(devices.Length);
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (DeviceInfo dev in devices)
            {
                string name = string.IsNullOrWhiteSpace(dev.Name) ? "Audio Device" : dev.Name.Trim();
                if (!seenNames.Add(name))
                {
                    continue;
                }

                list.Add(new AudioOutputDeviceInfo(
                    Id: name,
                    FriendlyName: name,
                    IsDefault: dev.IsDefault));
            }

            return list;
        }
        catch
        {
            return Array.Empty<AudioOutputDeviceInfo>();
        }
    }

    public AudioLatencyDiagnostics GetLatencyDiagnostics()
    {
        string backend = _engine?.ActiveBackend.ToString() ?? "Unavailable";
        long callbacks = _mixerComponent?.CallbackCount ?? 0;
        int frames = _mixerComponent?.LastCallbackFrames ?? 0;
        double avgInterval = _mixerComponent?.AverageCallbackIntervalMs ?? 0;
        return new AudioLatencyDiagnostics(
            HasActiveOutput,
            ActiveDeviceName,
            backend,
            callbacks,
            frames,
            avgInterval,
            EstimatedLatencyMs);
    }

    private void ReinitializeOutput()
    {
        if (_disposed) return;

        _reinitLock.Wait();
        try
        {
            if (_disposed) return;
            StopCurrentOutput();

            if (_engine is null)
            {
                HasActiveOutput = false;
                ActiveDeviceName = "Audio Backend Unavailable";
                _configuredBufferLatencyMs = null;
                ActiveDeviceChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            try
            {
                _engine.UpdateAudioDevicesInfo();
            }
            catch
            {
                // Ignore refresh errors and try default initialization.
            }

            DeviceInfo[] devices = _engine.PlaybackDevices;
            DeviceInfo? target = ResolveTargetDevice(devices, _preferredDeviceId);

            if (TryStartDevice(target, periodFrames: 480, periods: 2, out double latencyMs) ||
                TryStartDevice(target, periodFrames: 960, periods: 2, out latencyMs) ||
                TryStartDevice(target, periodFrames: 0, periods: 0, out latencyMs))
            {
                HasActiveOutput = true;
                ActiveDeviceName = _playbackDevice?.Info?.Name ?? target?.Name ?? "System Default Output";
                _configuredBufferLatencyMs = latencyMs > 0 ? latencyMs : null;
            }
            else
            {
                HasActiveOutput = false;
                ActiveDeviceName = devices.Length == 0 ? "No Output Device" : "Audio Device Unavailable";
                _configuredBufferLatencyMs = null;
            }

            ActiveDeviceChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _reinitLock.Release();
        }
    }

    private static DeviceInfo? ResolveTargetDevice(DeviceInfo[] devices, string? preferredId)
    {
        if (devices.Length == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(preferredId))
        {
            foreach (DeviceInfo dev in devices)
            {
                if (string.Equals(dev.Name?.Trim(), preferredId.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return dev;
                }
            }
        }

        foreach (DeviceInfo dev in devices)
        {
            if (dev.IsDefault)
            {
                return dev;
            }
        }

        return devices[0];
    }

    private bool TryStartDevice(DeviceInfo? targetDevice, uint periodFrames, uint periods, out double configuredLatencyMs)
    {
        configuredLatencyMs = 0;
        if (_engine is null) return false;

        AudioPlaybackDevice? device = null;
        MixerSoundComponent? component = null;
        try
        {
            var config = new MiniAudioDeviceConfig
            {
                PeriodSizeInFrames = periodFrames,
                Periods = periods,
                Playback = new DeviceSubConfig { ShareMode = ShareMode.Shared }
            };

            device = _engine.InitializePlaybackDevice(targetDevice, PlaybackFormat, config);
            component = new MixerSoundComponent(_engine, PlaybackFormat, Mixer);
            device.MasterMixer.AddComponent(component);
            device.Start();

            _playbackDevice = device;
            _mixerComponent = component;
            configuredLatencyMs = periodFrames > 0 && periods > 0
                ? (periodFrames * periods * 1000.0) / AudioClip.StandardSampleRate
                : 20.0;
            return true;
        }
        catch
        {
            try
            {
                if (component is not null && device is not null)
                {
                    device.MasterMixer.RemoveComponent(component);
                }
                component?.Dispose();
                device?.Stop();
                device?.Dispose();
            }
            catch
            {
                // Ignore cleanup failure on fallback path.
            }

            return false;
        }
    }

    private void StopCurrentOutput()
    {
        try
        {
            if (_mixerComponent is not null && _playbackDevice is not null)
            {
                _playbackDevice.MasterMixer.RemoveComponent(_mixerComponent);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }

        try
        {
            _mixerComponent?.Dispose();
        }
        catch
        {
            // Best-effort cleanup.
        }
        finally
        {
            _mixerComponent = null;
        }

        try
        {
            _playbackDevice?.Stop();
            _playbackDevice?.Dispose();
        }
        catch
        {
            // Best-effort cleanup.
        }
        finally
        {
            _playbackDevice = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _reinitLock.Wait();
        try
        {
            StopCurrentOutput();
            _engine?.Dispose();
            _engine = null;
        }
        finally
        {
            _reinitLock.Release();
            _reinitLock.Dispose();
        }

        _ = DevicesChanged;
    }
}


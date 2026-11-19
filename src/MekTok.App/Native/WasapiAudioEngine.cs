using System.Runtime.InteropServices;
using MekTok.Core.Audio;
using MekTok.Core.Platform;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MekTok.App.Native;

/// <summary>
/// Low-latency WASAPI playback engine that follows Windows default audio output or
/// pins a specific output device, safely handling hot-plugging and device switches off COM threads.
/// </summary>
public sealed class WasapiAudioEngine : IAudioOutputEngine
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly MMDeviceNotificationClient? _notificationClient;
    private readonly SemaphoreSlim _reinitLock = new(1, 1);
    private WasapiPlayer? _wasapiPlayer;
    private MMDevice? _activeDevice;
    private string? _preferredDeviceId;
    private int _reinitScheduled;
    private bool _disposed;

    public WasapiAudioEngine()
    {
        Mixer = new KeyboardAudioMixer();
        try
        {
            _notificationClient = _enumerator.CreateNotificationClient(useSynchronizationContext: false);
            _notificationClient.DeviceStateChanged += OnDeviceStateChanged;
            _notificationClient.DeviceAdded += OnDeviceAdded;
            _notificationClient.DeviceRemoved += OnDeviceRemoved;
            _notificationClient.DefaultDeviceChanged += OnDefaultDeviceChanged;
        }
        catch
        {
            // Continue gracefully if notification registration is unavailable.
        }
    }

    public KeyboardAudioMixer Mixer { get; }
    public bool HasActiveOutput { get; private set; }
    public string ActiveDeviceName { get; private set; } = "No Audio Output";
    public double? EstimatedLatencyMs { get; private set; }

    public event EventHandler? DevicesChanged;
    public event EventHandler? ActiveDeviceChanged;

    public void Start(string? preferredDeviceId)
    {
        _preferredDeviceId = preferredDeviceId;
        ReinitializeOutput();
    }

    public void SetPreferredDevice(string? deviceId)
    {
        _preferredDeviceId = string.IsNullOrWhiteSpace(deviceId) ? null : deviceId;
        _ = Task.Run(ReinitializeOutput);
    }

    public IReadOnlyList<AudioOutputDeviceInfo> ListOutputDevices()
    {
        var result = new List<AudioOutputDeviceInfo>();
        try
        {
            string? defaultId = null;
            try
            {
                using MMDevice defaultDev = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                defaultId = defaultDev.ID;
            }
            catch (COMException)
            {
                // No default render device currently active.
            }

            foreach (MMDevice dev in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                try
                {
                    result.Add(new AudioOutputDeviceInfo(
                        dev.ID,
                        dev.FriendlyName,
                        string.Equals(dev.ID, defaultId, StringComparison.OrdinalIgnoreCase)));
                }
                finally
                {
                    dev.Dispose();
                }
            }
        }
        catch
        {
            // Return whatever devices were collected cleanly.
        }

        return result;
    }

    private void ReinitializeOutput()
    {
        if (_disposed) return;

        _reinitLock.Wait();
        try
        {
            if (_disposed) return;
            StopCurrentOutput();

            MMDevice? selected = ResolveDevice(_preferredDeviceId);
            if (selected is null)
            {
                HasActiveOutput = false;
                ActiveDeviceName = "No Output Device";
                EstimatedLatencyMs = null;
                ActiveDeviceChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (TryStartDevice(selected, latencyMs: 25) || TryStartDevice(selected, latencyMs: 50))
            {
                _activeDevice = selected;
                HasActiveOutput = true;
                ActiveDeviceName = selected.FriendlyName;
                EstimatedLatencyMs = _wasapiPlayer?.LatencyMilliseconds > 0
                    ? _wasapiPlayer.LatencyMilliseconds
                    : 25;
            }
            else
            {
                selected.Dispose();
                HasActiveOutput = false;
                ActiveDeviceName = "Audio Device Unavailable";
                EstimatedLatencyMs = null;
            }

            ActiveDeviceChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _reinitLock.Release();
        }
    }

    private bool TryStartDevice(MMDevice device, int latencyMs)
    {
        WasapiPlayer? candidate = null;
        try
        {
            candidate = new WasapiPlayerBuilder()
                .WithDevice(device)
                .WithSharedMode()
                .WithEventSync()
                .WithLatency(latencyMs)
                .WithLowLatency(required: false)
                .WithMmcssThreadPriority("Pro Audio")
                .Build();

            candidate.Init(Mixer);
            candidate.Play();
            _wasapiPlayer = candidate;
            return true;
        }
        catch
        {
            try
            {
                candidate?.Dispose();
            }
            catch
            {
                // Ignore cleanup error on failed init attempt.
            }
            return false;
        }
    }

    private MMDevice? ResolveDevice(string? preferredDeviceId)
    {
        if (!string.IsNullOrWhiteSpace(preferredDeviceId))
        {
            try
            {
                MMDevice dev = _enumerator.GetDevice(preferredDeviceId);
                if (dev.State == DeviceState.Active && dev.DataFlow == DataFlow.Render)
                {
                    return dev;
                }
                dev.Dispose();
            }
            catch
            {
                // Fallback to system default render endpoint.
            }
        }

        try
        {
            return _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }
        catch (COMException)
        {
            return null;
        }
    }

    private void StopCurrentOutput()
    {
        try
        {
            _wasapiPlayer?.Stop();
            _wasapiPlayer?.Dispose();
        }
        catch
        {
            // Best-effort disposal on device switch.
        }
        finally
        {
            _wasapiPlayer = null;
        }

        try
        {
            _activeDevice?.Dispose();
        }
        catch
        {
            // Best-effort disposal.
        }
        finally
        {
            _activeDevice = null;
        }
    }

    private void ScheduleReinitAsync()
    {
        if (_disposed) return;
        if (Interlocked.Exchange(ref _reinitScheduled, 1) == 1) return;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(250);
                Interlocked.Exchange(ref _reinitScheduled, 0);
                ReinitializeOutput();
                DevicesChanged?.Invoke(this, EventArgs.Empty);
            }
            catch
            {
                Interlocked.Exchange(ref _reinitScheduled, 0);
            }
        });
    }

    private void OnDeviceStateChanged(object? sender, DeviceStateChangedEventArgs e) =>
        ScheduleReinitAsync();

    private void OnDeviceAdded(object? sender, DeviceNotificationEventArgs e) =>
        ScheduleReinitAsync();

    private void OnDeviceRemoved(object? sender, DeviceNotificationEventArgs e) =>
        ScheduleReinitAsync();

    private void OnDefaultDeviceChanged(object? sender, DefaultDeviceChangedEventArgs e)
    {
        if (e.Flow == DataFlow.Render && e.Role == Role.Multimedia)
        {
            ScheduleReinitAsync();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_notificationClient is not null)
        {
            _notificationClient.DeviceStateChanged -= OnDeviceStateChanged;
            _notificationClient.DeviceAdded -= OnDeviceAdded;
            _notificationClient.DeviceRemoved -= OnDeviceRemoved;
            _notificationClient.DefaultDeviceChanged -= OnDefaultDeviceChanged;
            try
            {
                _notificationClient.Dispose();
            }
            catch
            {
                // Ignore during shutdown.
            }
        }

        _reinitLock.Wait();
        try
        {
            StopCurrentOutput();
            _enumerator.Dispose();
        }
        finally
        {
            _reinitLock.Release();
            _reinitLock.Dispose();
        }
    }
}

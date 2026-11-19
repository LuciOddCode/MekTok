using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace MekTok.Core.Audio;

public sealed record AudioOutputDeviceInfo(
    string Id,
    string FriendlyName,
    bool IsDefault);

/// <summary>
/// Low-latency WASAPI playback engine that follows Windows default audio output or
/// pins a specific output device, safely handling hot-plugging and device switches off COM threads.
/// </summary>
public sealed class WasapiAudioEngine : IMMNotificationClient, IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly SemaphoreSlim _reinitLock = new(1, 1);
    private WasapiOut? _wasapiOut;
    private MMDevice? _activeDevice;
    private string? _preferredDeviceId;
    private int _reinitScheduled;
    private bool _disposed;

    public WasapiAudioEngine()
    {
        Mixer = new KeyboardAudioMixer();
        try
        {
            _enumerator.RegisterEndpointNotificationCallback(this);
        }
        catch
        {
            // Continue gracefully if notification registration is unavailable.
        }
    }

    public KeyboardAudioMixer Mixer { get; }
    public bool HasActiveOutput { get; private set; }
    public string ActiveDeviceName { get; private set; } = "No Audio Output";

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
                ActiveDeviceChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (TryStartDevice(selected, latencyMs: 25) || TryStartDevice(selected, latencyMs: 50))
            {
                _activeDevice = selected;
                HasActiveOutput = true;
                ActiveDeviceName = selected.FriendlyName;
            }
            else
            {
                selected.Dispose();
                HasActiveOutput = false;
                ActiveDeviceName = "Audio Device Unavailable";
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
        WasapiOut? candidate = null;
        try
        {
            candidate = new WasapiOut(device, AudioClientShareMode.Shared, useEventSync: true, latency: latencyMs);
            candidate.Init(Mixer);
            candidate.Play();
            _wasapiOut = candidate;
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
            _wasapiOut?.Stop();
            _wasapiOut?.Dispose();
        }
        catch
        {
            // Best-effort disposal on device switch.
        }
        finally
        {
            _wasapiOut = null;
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

    void IMMNotificationClient.OnDeviceStateChanged(string deviceId, DeviceState newState) =>
        ScheduleReinitAsync();

    void IMMNotificationClient.OnDeviceAdded(string pwstrDeviceId) =>
        ScheduleReinitAsync();

    void IMMNotificationClient.OnDeviceRemoved(string deviceId) =>
        ScheduleReinitAsync();

    void IMMNotificationClient.OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow == DataFlow.Render && role == Role.Multimedia)
        {
            ScheduleReinitAsync();
        }
    }

    void IMMNotificationClient.OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
    {
        // No action required on property updates.
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _enumerator.UnregisterEndpointNotificationCallback(this);
        }
        catch
        {
            // Ignore during shutdown.
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


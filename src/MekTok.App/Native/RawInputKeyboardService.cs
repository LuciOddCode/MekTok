using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;
using MekTok.Core.Keyboard;
using Microsoft.Win32;

namespace MekTok.App.Native;

public sealed record KeyboardDeviceInfo(string DeviceId, string DisplayName);

public readonly record struct RawKeyInputEvent(int CanonicalScanCode, bool IsKeyUp, string DeviceId);

/// <summary>
/// Captures global hardware keyboard events via Win32 Raw Input (RIDEV_INPUTSINK) on a hidden
/// message-only window so typing latency is never blocked and per-keyboard filtering works cleanly.
/// Keystrokes are never logged or stored.
/// </summary>
public sealed class RawInputKeyboardService : IDisposable
{
    private const string SyntheticDeviceId = "SYNTHETIC_OR_UNKNOWN";
    private const ushort RiKeyBreak = 0x0001;

    private readonly HwndSource _hwndSource;
    private readonly ConcurrentDictionary<IntPtr, string> _deviceIdCache = new();
    private readonly uint _headerSize = (uint)Marshal.SizeOf<NativeMethods.RAWINPUTHEADER>();
    private IntPtr _rawBuffer;
    private uint _rawBufferCapacity = 256;
    private bool _disposed;

    public RawInputKeyboardService()
    {
        _rawBuffer = Marshal.AllocHGlobal((int)_rawBufferCapacity);

        var parameters = new HwndSourceParameters("MekTok_RawInput_Sink")
        {
            ParentWindow = NativeMethods.HWND_MESSAGE,
            Width = 0,
            Height = 0,
            WindowStyle = 0
        };

        _hwndSource = new HwndSource(parameters);
        _hwndSource.AddHook(WndProc);

        RegisterForKeyboardInput();
        RefreshDeviceCache();
    }

    public IntPtr WindowHandle => _hwndSource.Handle;

    public event Action<RawKeyInputEvent>? KeyEvent;
    public event EventHandler? KeyboardsChanged;
    public event Action<IntPtr, uint, uint>? CustomMessageReceived;

    private void RegisterForKeyboardInput()
    {
        var devices = new NativeMethods.RAWINPUTDEVICE[]
        {
            new()
            {
                usUsagePage = 0x01, // Generic Desktop Controls
                usUsage = 0x06,     // Keyboard
                dwFlags = NativeMethods.RIDEV_INPUTSINK | NativeMethods.RIDEV_DEVNOTIFY,
                hwndTarget = _hwndSource.Handle
            }
        };

        NativeMethods.RegisterRawInputDevices(
            devices,
            (uint)devices.Length,
            (uint)Marshal.SizeOf<NativeMethods.RAWINPUTDEVICE>());
    }

    public IReadOnlyList<KeyboardDeviceInfo> GetConnectedKeyboards()
    {
        var list = new List<KeyboardDeviceInfo>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        uint count = 0;
        uint itemSize = (uint)Marshal.SizeOf<NativeMethods.RAWINPUTDEVICELIST>();
        if (NativeMethods.GetRawInputDeviceList(null, ref count, itemSize) == unchecked((uint)-1) || count == 0)
        {
            return list;
        }

        var devices = new NativeMethods.RAWINPUTDEVICELIST[count];
        if (NativeMethods.GetRawInputDeviceList(devices, ref count, itemSize) == unchecked((uint)-1))
        {
            return list;
        }

        for (int i = 0; i < count; i++)
        {
            if (devices[i].dwType != NativeMethods.RIM_TYPEKEYBOARD) continue;

            string rawName = QueryDeviceName(devices[i].hDevice);
            if (string.IsNullOrWhiteSpace(rawName)) continue;

            _deviceIdCache[devices[i].hDevice] = rawName;

            if (!seenIds.Add(rawName)) continue;
            string friendly = ResolveFriendlyName(rawName);
            list.Add(new KeyboardDeviceInfo(rawName, friendly));
        }

        return list.OrderBy(k => k.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_INPUT)
        {
            ProcessRawInput(lParam);
            return IntPtr.Zero;
        }

        if (msg == NativeMethods.WM_INPUT_DEVICE_CHANGE)
        {
            RefreshDeviceCache();
            KeyboardsChanged?.Invoke(this, EventArgs.Empty);
            return IntPtr.Zero;
        }

        if (msg == NativeMethods.WM_HOTKEY)
        {
            CustomMessageReceived?.Invoke(wParam, (uint)lParam.ToInt64(), (uint)msg);
            return IntPtr.Zero;
        }

        return IntPtr.Zero;
    }

    private void ProcessRawInput(IntPtr hRawInput)
    {
        uint size = 0;
        if (NativeMethods.GetRawInputData(hRawInput, NativeMethods.RID_INPUT, IntPtr.Zero, ref size, _headerSize) != 0 ||
            size == 0)
        {
            return;
        }

        if (size > _rawBufferCapacity)
        {
            Marshal.FreeHGlobal(_rawBuffer);
            _rawBufferCapacity = size;
            _rawBuffer = Marshal.AllocHGlobal((int)_rawBufferCapacity);
        }

        if (NativeMethods.GetRawInputData(hRawInput, NativeMethods.RID_INPUT, _rawBuffer, ref size, _headerSize) != size)
        {
            return;
        }

        var packet = Marshal.PtrToStructure<NativeMethods.RAWINPUT_KEYBOARD>(_rawBuffer);
        if (packet.Header.dwType != NativeMethods.RIM_TYPEKEYBOARD)
        {
            return;
        }

        int? scanCode = Ansi104Layout.NormalizeRawScanCode(
            packet.Keyboard.MakeCode,
            packet.Keyboard.Flags,
            packet.Keyboard.VKey);

        if (scanCode is null)
        {
            return;
        }

        bool isKeyUp = (packet.Keyboard.Flags & RiKeyBreak) != 0;
        string deviceId = ResolveCachedDeviceId(packet.Header.hDevice);

        KeyEvent?.Invoke(new RawKeyInputEvent(scanCode.Value, isKeyUp, deviceId));
    }

    private string ResolveCachedDeviceId(IntPtr hDevice)
    {
        if (hDevice == IntPtr.Zero)
        {
            return SyntheticDeviceId;
        }

        return _deviceIdCache.GetOrAdd(hDevice, h =>
        {
            string name = QueryDeviceName(h);
            return string.IsNullOrWhiteSpace(name) ? SyntheticDeviceId : name;
        });
    }

    private void RefreshDeviceCache()
    {
        _deviceIdCache.Clear();
        _ = GetConnectedKeyboards();
    }

    private static string QueryDeviceName(IntPtr hDevice)
    {
        if (hDevice == IntPtr.Zero) return string.Empty;

        uint charCount = 0;
        NativeMethods.GetRawInputDeviceInfo(hDevice, NativeMethods.RIDI_DEVICENAME, null, ref charCount);
        if (charCount == 0 || charCount > 2048)
        {
            return string.Empty;
        }

        uint bufChars = charCount + 2;
        var sb = new StringBuilder((int)bufChars);
        uint res = NativeMethods.GetRawInputDeviceInfo(hDevice, NativeMethods.RIDI_DEVICENAME, sb, ref bufChars);
        return res == unchecked((uint)-1) ? string.Empty : sb.ToString();
    }

    private static string ResolveFriendlyName(string rawDevicePath)
    {
        // Format: \\?\HID#VID_046D&PID_C31C&MI_00#7&1234abcd&0&0000#{guid}
        try
        {
            string trimmed = rawDevicePath;
            if (trimmed.StartsWith(@"\\?\", StringComparison.Ordinal))
            {
                trimmed = trimmed[4..];
            }

            string[] parts = trimmed.Split('#');
            if (parts.Length >= 3)
            {
                string bus = parts[0];
                string hwId = parts[1];
                string instance = parts[2];

                string regPath = $@"SYSTEM\CurrentControlSet\Enum\{bus}\{hwId}\{instance}";
                using RegistryKey? key = Registry.LocalMachine.OpenSubKey(regPath);
                if (key is not null)
                {
                    string? rawDesc = (key.GetValue("FriendlyName") as string)
                        ?? (key.GetValue("DeviceDesc") as string);

                    if (!string.IsNullOrWhiteSpace(rawDesc))
                    {
                        int semi = rawDesc.LastIndexOf(';');
                        string clean = semi >= 0 && semi + 1 < rawDesc.Length
                            ? rawDesc[(semi + 1)..]
                            : rawDesc;

                        if (!string.IsNullOrWhiteSpace(clean))
                        {
                            return $"{clean.Trim()} ({ShortHardwareTag(hwId)})";
                        }
                    }
                }

                return $"{bus} Keyboard ({ShortHardwareTag(hwId)})";
            }
        }
        catch
        {
            // Fallback below on unexpected device path format.
        }

        return "Keyboard Device";
    }

    private static string ShortHardwareTag(string hwId)
    {
        int miIdx = hwId.IndexOf("&MI_", StringComparison.OrdinalIgnoreCase);
        return miIdx > 0 ? hwId[..miIdx] : hwId;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        var remove = new NativeMethods.RAWINPUTDEVICE[]
        {
            new()
            {
                usUsagePage = 0x01,
                usUsage = 0x06,
                dwFlags = NativeMethods.RIDEV_REMOVE,
                hwndTarget = IntPtr.Zero
            }
        };

        try
        {
            NativeMethods.RegisterRawInputDevices(
                remove,
                1,
                (uint)Marshal.SizeOf<NativeMethods.RAWINPUTDEVICE>());
        }
        catch
        {
            // Ignore shutdown deregistration failure.
        }

        _hwndSource.RemoveHook(WndProc);
        _hwndSource.Dispose();

        if (_rawBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_rawBuffer);
            _rawBuffer = IntPtr.Zero;
        }
    }
}


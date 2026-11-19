using System.Net.Sockets;
using System.Text;
using MekTok.Core.Platform;

namespace MekTok.Platform.Desktop.Lifecycle;

/// <summary>
/// Cross-platform single-instance lockfile + Unix Domain Socket wake-up guard
/// (avoids named EventWaitHandle which throws PlatformNotSupportedException on Unix).
/// </summary>
public sealed class UnixSingleInstanceGuard : ISingleInstanceGuard
{
    private readonly string _runtimeDirectory;
    private readonly string _lockFilePath;
    private readonly string _socketPath;
    private readonly CancellationTokenSource _cts = new();

    private FileStream? _lockStream;
    private Socket? _listenerSocket;
    private bool _disposed;

    public UnixSingleInstanceGuard(string? runtimeDirectory = null)
    {
        _runtimeDirectory = string.IsNullOrWhiteSpace(runtimeDirectory)
            ? ResolveDefaultRuntimeDirectory()
            : Path.GetFullPath(runtimeDirectory);

        Directory.CreateDirectory(_runtimeDirectory);
        _lockFilePath = Path.Combine(_runtimeDirectory, "mektok.lock");
        _socketPath = Path.Combine(_runtimeDirectory, "mektok.sock");

        try
        {
            _lockStream = new FileStream(
                _lockFilePath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);

            IsPrimary = true;
            StartSocketServer();
        }
        catch (IOException)
        {
            IsPrimary = false;
        }
    }

    public bool IsPrimary { get; }

    public event EventHandler? SecondInstanceSignaled;

    public void NotifyPrimaryInstance()
    {
        if (IsPrimary || _disposed || !File.Exists(_socketPath))
        {
            return;
        }

        try
        {
            using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            client.Connect(new UnixDomainSocketEndPoint(_socketPath));
            byte[] payload = Encoding.UTF8.GetBytes("SHOW\n");
            client.Send(payload);
        }
        catch
        {
            // Ignore transient socket connection failures if primary is exiting.
        }
    }

    private void StartSocketServer()
    {
        try
        {
            if (File.Exists(_socketPath))
            {
                File.Delete(_socketPath);
            }

            _listenerSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            _listenerSocket.Bind(new UnixDomainSocketEndPoint(_socketPath));
            _listenerSocket.Listen(4);
            _ = Task.Run(AcceptLoopAsync);
        }
        catch
        {
            try
            {
                _listenerSocket?.Dispose();
            }
            catch
            {
                // Ignore cleanup errors.
            }
            _listenerSocket = null;
        }
    }

    private async Task AcceptLoopAsync()
    {
        byte[] buffer = new byte[64];
        while (!_cts.IsCancellationRequested && _listenerSocket is not null)
        {
            try
            {
                using Socket client = await _listenerSocket.AcceptAsync(_cts.Token);
                int bytes = await client.ReceiveAsync(buffer, SocketFlags.None, _cts.Token);
                string message = bytes > 0
                    ? Encoding.UTF8.GetString(buffer, 0, bytes).Trim()
                    : "SHOW";

                if (message.StartsWith("SHOW", StringComparison.OrdinalIgnoreCase))
                {
                    SecondInstanceSignaled?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                if (_cts.IsCancellationRequested) break;
                await Task.Delay(50);
            }
        }
    }

    private static string ResolveDefaultRuntimeDirectory()
    {
        string? xdgRuntime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrWhiteSpace(xdgRuntime) && Directory.Exists(xdgRuntime))
        {
            return Path.Combine(xdgRuntime, "mektok");
        }

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(appData))
        {
            return Path.Combine(appData, "MekTok");
        }

        return Path.Combine(Path.GetTempPath(), $"mektok-{Environment.UserName}");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();

        try
        {
            _listenerSocket?.Dispose();
        }
        catch
        {
            // Ignore socket shutdown error.
        }
        _listenerSocket = null;

        if (IsPrimary)
        {
            try
            {
                if (File.Exists(_socketPath))
                {
                    File.Delete(_socketPath);
                }
            }
            catch
            {
                // Best-effort cleanup.
            }
        }

        try
        {
            _lockStream?.Dispose();
        }
        catch
        {
            // Ignore lock release failure.
        }
        _lockStream = null;
        _cts.Dispose();
    }
}


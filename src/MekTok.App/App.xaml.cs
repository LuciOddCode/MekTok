using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using MekTok.App.Services;
using MekTok.App.UI;
using MekTok.Core.Config;

namespace MekTok.App;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\MekTok_SingleInstance_Mutex";
    private const string ShowSignalEventName = @"Local\MekTok_ShowFlyout_Event";

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int dwProcessId);

    private Mutex? _instanceMutex;
    private EventWaitHandle? _showSignalEvent;
    private RegisteredWaitHandle? _signalWaitHandle;
    private MekTokController? _controller;
    private TrayIconManager? _trayManager;
    private TrayFlyoutWindow? _flyoutWindow;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Any(a => string.Equals(a, "--self-test", StringComparison.OrdinalIgnoreCase)))
        {
            if (!Console.IsOutputRedirected)
            {
                AttachConsole(-1);
            }

            bool ok = MekTokController.RunHeadlessSelfTest(out string summary);
            Console.WriteLine(summary);
            Environment.ExitCode = ok ? 0 : 1;
            Shutdown(ok ? 0 : 1);
            return;
        }

        _instanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool isPrimary);
        _showSignalEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalEventName);

        if (!isPrimary)
        {
            _showSignalEvent.Set();
            Shutdown(0);
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        _controller = new MekTokController();
        _controller.ApplyInitialTheme();

        _mainWindow = new MainWindow(_controller);
        _flyoutWindow = new TrayFlyoutWindow(_controller);
        _trayManager = new TrayIconManager(_controller);

        _flyoutWindow.OpenConfiguratorRequested += (_, _) => ShowMainWindow();
        _trayManager.ToggleFlyoutRequested += (_, _) => _flyoutWindow.ToggleNearTaskbar();
        _trayManager.OpenConfiguratorRequested += (_, _) => ShowMainWindow();
        _trayManager.ExitRequested += (_, _) => ExitApplication();

        _signalWaitHandle = ThreadPool.RegisterWaitForSingleObject(
            _showSignalEvent,
            (_, timedOut) =>
            {
                if (!timedOut)
                {
                    Dispatcher.BeginInvoke(ShowMainWindow);
                }
            },
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);

        bool startMinimized = e.Args.Any(a => string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase));
        if (!startMinimized)
        {
            ShowMainWindow();
        }
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null) return;

        if (!_mainWindow.IsVisible)
        {
            _mainWindow.Show();
        }

        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        _mainWindow.Activate();
    }

    private void ExitApplication()
    {
        if (_mainWindow is not null)
        {
            _mainWindow.AllowRealClose = true;
            _mainWindow.Close();
        }
        Shutdown(0);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _signalWaitHandle?.Unregister(null);
        _trayManager?.Dispose();
        _flyoutWindow?.Close();
        _controller?.Dispose();
        _showSignalEvent?.Dispose();

        try
        {
            _instanceMutex?.ReleaseMutex();
        }
        catch
        {
            // Ignore if not owned.
        }
        _instanceMutex?.Dispose();

        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogError(e.Exception);
        MessageBox.Show(
            "MekTok encountered an unexpected error and needs to recover. Details were written to the MekTok logs folder.",
            "MekTok",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogError(ex);
        }
    }

    private static void LogError(Exception ex)
    {
        try
        {
            var store = new SettingsStore();
            store.EnsureDirectories();
            string logFile = Path.Combine(store.LogsDirectory, "error.log");
            string entry = $"[{DateTime.UtcNow:O}] {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}{Environment.NewLine}";
            File.AppendAllText(logFile, entry);
        }
        catch
        {
            // Never throw from error logger.
        }
    }
}


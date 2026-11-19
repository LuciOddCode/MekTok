using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using MekTok.Core.Config;
using MekTok.Core.Platform;
using MekTok.Core.Services;
using MekTok.Desktop.UI;
using MekTok.Platform.Desktop;

namespace MekTok.Desktop;

public partial class App : Application
{
    public static ISingleInstanceGuard? SingleInstanceGuard { get; set; }
    public static bool StartMinimized { get; set; }

    private MekTokController? _controller;
    private MainWindow? _mainWindow;
    private TrayIcon? _trayIcon;
    private WindowIcon? _activeTrayIcon;
    private WindowIcon? _mutedTrayIcon;

    private NativeMenuItem? _toggleSoundsItem;
    private NativeMenuItem? _profilesParentItem;
    private NativeMenuItem? _keyUpItem;
    private NativeMenuItem? _stereoPanItem;
    private NativeMenuItem? _startupItem;
    private bool _isRefreshingMenu;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += OnDesktopExit;

            _controller = DesktopPlatformFactory.CreateController(action => Dispatcher.UIThread.Post(action));
            ApplyTheme(_controller.Settings.Theme);

            _mainWindow = new MainWindow(_controller, ApplyTheme);
            desktop.MainWindow = _mainWindow;

            SetupTrayIcon();

            _controller.StateChanged += (_, _) => Dispatcher.UIThread.Post(RefreshTrayState);
            _controller.ProfilesChanged += (_, _) => Dispatcher.UIThread.Post(RefreshTrayState);

            if (SingleInstanceGuard is not null)
            {
                SingleInstanceGuard.SecondInstanceSignaled += (_, _) =>
                    Dispatcher.UIThread.Post(ShowMainWindow);
            }

            if (!StartMinimized)
            {
                ShowMainWindow();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    public void ApplyTheme(ThemePreference preference)
    {
        RequestedThemeVariant = preference switch
        {
            ThemePreference.Dark => ThemeVariant.Dark,
            ThemePreference.Light => ThemeVariant.Light,
            _ => ThemeVariant.Default
        };
    }

    private void SetupTrayIcon()
    {
        if (_controller is null) return;

        _activeTrayIcon = new WindowIcon(AssetLoader.Open(new Uri("avares://MekTok/Assets/mektok-active.png")));
        _mutedTrayIcon = new WindowIcon(AssetLoader.Open(new Uri("avares://MekTok/Assets/mektok-muted.png")));

        _toggleSoundsItem = new NativeMenuItem();
        _toggleSoundsItem.Click += (_, _) => _controller.ToggleEnabled();

        _profilesParentItem = new NativeMenuItem("Switch Profile")
        {
            Menu = new NativeMenu()
        };

        _keyUpItem = new NativeMenuItem("Key-Up Release Sounds")
        {
            ToggleType = MenuItemToggleType.CheckBox
        };
        _keyUpItem.Click += (_, _) =>
        {
            if (_isRefreshingMenu) return;
            bool next = !_controller.Settings.Acoustics.EnableKeyUpSound;
            _controller.UpdateAcoustics(a => a.EnableKeyUpSound = next, rebuildSynthBanks: false);
        };

        _stereoPanItem = new NativeMenuItem("Spatial Stereo Panning")
        {
            ToggleType = MenuItemToggleType.CheckBox
        };
        _stereoPanItem.Click += (_, _) =>
        {
            if (_isRefreshingMenu) return;
            bool next = !_controller.Settings.Acoustics.EnableStereoPan;
            _controller.UpdateAcoustics(a => a.EnableStereoPan = next, rebuildSynthBanks: false);
        };

        var testSoundItem = new NativeMenuItem("Test Active Profile Sound");
        testSoundItem.Click += (_, _) => _controller.AuditionProfile(_controller.Settings.ActiveProfileId);

        var openConfigItem = new NativeMenuItem("Configure Keys & Settings...");
        openConfigItem.Click += (_, _) => ShowMainWindow();

        _startupItem = new NativeMenuItem("Start on Login")
        {
            ToggleType = MenuItemToggleType.CheckBox
        };
        _startupItem.Click += (_, _) =>
        {
            if (_isRefreshingMenu) return;
            _controller.SetStartOnLogin(!_controller.Settings.StartWithWindows);
        };

        var exitItem = new NativeMenuItem("Exit MekTok");
        exitItem.Click += (_, _) => ExitApplication();

        var menu = new NativeMenu();
        menu.Items.Add(_toggleSoundsItem);
        menu.Items.Add(_profilesParentItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(_keyUpItem);
        menu.Items.Add(_stereoPanItem);
        menu.Items.Add(testSoundItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(openConfigItem);
        menu.Items.Add(_startupItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(exitItem);

        _trayIcon = new TrayIcon
        {
            Icon = _activeTrayIcon,
            ToolTipText = "MekTok",
            Menu = menu,
            IsVisible = true
        };
        _trayIcon.Clicked += (_, _) => ShowMainWindow();

        TrayIcon.SetIcons(this, new TrayIcons { _trayIcon });
        RefreshTrayState();
    }

    private void RefreshTrayState()
    {
        if (_controller is null || _trayIcon is null) return;

        _isRefreshingMenu = true;
        try
        {
            bool active = _controller.IsEffectivelyActive;
            _trayIcon.Icon = active ? _activeTrayIcon : _mutedTrayIcon;

            IReadOnlyList<ProfileChoiceItem> profiles = _controller.GetAllProfiles();
            string profileName = profiles
                .FirstOrDefault(p => string.Equals(p.Id, _controller.Settings.ActiveProfileId, StringComparison.OrdinalIgnoreCase))
                ?.DisplayName ?? "Creamy Thock (Holy Panda)";

            string status = !_controller.Settings.IsEnabled
                ? "Muted"
                : _controller.IsAutoPaused
                    ? $"Paused ({_controller.AutoPauseReason})"
                    : "Active";

            string tip = $"MekTok • {status} • {profileName}";
            _trayIcon.ToolTipText = tip.Length > 64 ? tip[..64] : tip;

            if (_toggleSoundsItem is not null)
            {
                string hotkeyHint = _controller.Settings.MuteHotkey.Enabled
                    ? $" ({_controller.Settings.MuteHotkey.ToDisplayString()})"
                    : string.Empty;
                _toggleSoundsItem.Header = _controller.Settings.IsEnabled
                    ? $"Mute Sounds{hotkeyHint}"
                    : $"Enable Sounds{hotkeyHint}";
            }

            if (_keyUpItem is not null)
            {
                _keyUpItem.IsChecked = _controller.Settings.Acoustics.EnableKeyUpSound;
            }

            if (_stereoPanItem is not null)
            {
                _stereoPanItem.IsChecked = _controller.Settings.Acoustics.EnableStereoPan;
            }

            if (_startupItem is not null)
            {
                _startupItem.IsChecked = _controller.Settings.StartWithWindows;
            }

            if (_profilesParentItem?.Menu is { } profilesMenu)
            {
                profilesMenu.Items.Clear();
                foreach (ProfileChoiceItem profile in profiles)
                {
                    var item = new NativeMenuItem(profile.DisplayName)
                    {
                        ToggleType = MenuItemToggleType.Radio,
                        IsChecked = string.Equals(profile.Id, _controller.Settings.ActiveProfileId, StringComparison.OrdinalIgnoreCase)
                    };
                    string capturedId = profile.Id;
                    item.Click += (_, _) => _controller.SelectProfile(capturedId);
                    profilesMenu.Items.Add(item);
                }
            }
        }
        finally
        {
            _isRefreshingMenu = false;
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

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown(0);
        }
    }

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        if (_trayIcon is not null)
        {
            _trayIcon.IsVisible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        _controller?.Dispose();
        _controller = null;

        SingleInstanceGuard?.Dispose();
        SingleInstanceGuard = null;
    }
}

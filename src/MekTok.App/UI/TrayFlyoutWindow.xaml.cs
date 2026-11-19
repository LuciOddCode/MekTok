using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using MekTok.Core.Audio;
using MekTok.Core.Services;

namespace MekTok.App.UI;

public partial class TrayFlyoutWindow : Window
{
    private sealed record DeviceComboItem(string Id, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    private static readonly string[] QuickSwitchIds =
    [
        BuiltInProfiles.MxRedId,
        BuiltInProfiles.TactileBrownId,
        BuiltInProfiles.MxBlackId,
        BuiltInProfiles.CrispClickyId,
        BuiltInProfiles.CreamyThockId,
        BuiltInProfiles.GateronYellowId
    ];

    private readonly MekTokController _controller;
    private readonly Dictionary<string, ToggleButton> _quickSwitchButtons = new(StringComparer.OrdinalIgnoreCase);
    private bool _isRefreshing;
    private DateTime _lastDeactivatedUtc = DateTime.MinValue;

    public TrayFlyoutWindow(MekTokController controller)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _isRefreshing = true;
        InitializeComponent();
        BuildQuickSwitchChips();
        _isRefreshing = false;

        _controller.StateChanged += (_, _) => Dispatcher.BeginInvoke(RefreshFromState);
        _controller.ProfilesChanged += (_, _) => Dispatcher.BeginInvoke(RefreshFromState);
        _controller.AudioDevicesChanged += (_, _) => Dispatcher.BeginInvoke(RefreshDeviceList);

        Deactivated += (_, _) =>
        {
            _lastDeactivatedUtc = DateTime.UtcNow;
            Hide();
        };

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Hide();
                e.Handled = true;
            }
        };
    }

    public event EventHandler? OpenConfiguratorRequested;

    public void ToggleNearTaskbar()
    {
        if (IsVisible)
        {
            Hide();
            return;
        }

        // Prevent immediate reopen when user clicks the tray icon to dismiss the flyout.
        if ((DateTime.UtcNow - _lastDeactivatedUtc).TotalMilliseconds < 220)
        {
            return;
        }

        RefreshFromState();
        RefreshDeviceList();

        // Force measure so ActualWidth/ActualHeight are accurate before positioning.
        Opacity = 0;
        Show();
        UpdateLayout();
        PositionAboveTaskbar();
        Opacity = 1;
        Activate();
    }

    private void BuildQuickSwitchChips()
    {
        QuickSwitchesPanel.Children.Clear();
        _quickSwitchButtons.Clear();

        Style chipStyle = (Style)FindResource("FluentMiniSwitchChip");
        foreach (string id in QuickSwitchIds)
        {
            SwitchProfileDefinition def = BuiltInProfiles.GetByIdOrDefault(id);

            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            var dotColor = (Color)ColorConverter.ConvertFromString(def.AccentHex);
            content.Children.Add(new Ellipse
            {
                Width = 7,
                Height = 7,
                Fill = new SolidColorBrush(dotColor),
                Margin = new Thickness(0, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            content.Children.Add(new TextBlock
            {
                Text = def.ShortName,
                VerticalAlignment = VerticalAlignment.Center
            });

            var btn = new ToggleButton
            {
                Style = chipStyle,
                Content = content,
                ToolTip = def.Name
            };

            string capturedId = def.Id;
            btn.Click += (_, _) =>
            {
                _controller.SelectProfile(capturedId);
                _controller.AuditionKey(0x001E);
            };

            QuickSwitchesPanel.Children.Add(btn);
            _quickSwitchButtons[def.Id] = btn;
        }
    }

    private void PositionAboveTaskbar()
    {
        Rect workArea = SystemParameters.WorkArea;
        double width = ActualWidth > 0 ? ActualWidth : Width;
        double height = ActualHeight > 0 ? ActualHeight : 410;

        double left = workArea.Right - width - 6;
        double top = workArea.Bottom - height - 6;

        Left = Math.Max(workArea.Left + 4, Math.Min(left, workArea.Right - width - 4));
        Top = Math.Max(workArea.Top + 4, Math.Min(top, workArea.Bottom - height - 4));
    }

    private void RefreshFromState()
    {
        _isRefreshing = true;
        try
        {
            MasterToggle.IsChecked = _controller.Settings.IsEnabled;

            int volPct = (int)Math.Round(_controller.Settings.MasterVolume * 100);
            VolumeSlider.Value = volPct;
            VolumePercentText.Text = $"{volPct}%";

            if (!_controller.Settings.IsEnabled)
            {
                StatusText.Text = "Muted";
                StatusText.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
                StatusBadge.SetResourceReference(Border.BackgroundProperty, "SurfaceElevatedBrush");
                SpeakerIconPath.SetResourceReference(Shape.FillProperty, "DangerBrush");
            }
            else if (_controller.IsAutoPaused)
            {
                StatusText.Text = "Paused";
                StatusText.SetResourceReference(TextBlock.ForegroundProperty, "WarningBrush");
                StatusBadge.SetResourceReference(Border.BackgroundProperty, "SurfaceElevatedBrush");
                SpeakerIconPath.SetResourceReference(Shape.FillProperty, "WarningBrush");
            }
            else
            {
                StatusText.Text = "Active";
                StatusText.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
                StatusBadge.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
                SpeakerIconPath.SetResourceReference(Shape.FillProperty, "AccentBrush");
            }

            HotkeySubtitle.Text = _controller.Settings.MuteHotkey.Enabled
                ? $"Shortcut: {_controller.Settings.MuteHotkey.ToDisplayString()}"
                : "Shortcut: Disabled";

            IReadOnlyList<ProfileChoiceItem> profiles = _controller.GetAllProfiles();
            ProfileCombo.ItemsSource = profiles;
            ProfileCombo.SelectedValue = _controller.Settings.ActiveProfileId;

            foreach ((string id, ToggleButton chip) in _quickSwitchButtons)
            {
                chip.IsChecked = string.Equals(
                    id,
                    _controller.Settings.ActiveProfileId,
                    StringComparison.OrdinalIgnoreCase);
            }

            KeyUpChip.IsChecked = _controller.Settings.Acoustics.EnableKeyUpSound;
            StereoPanChip.IsChecked = _controller.Settings.Acoustics.EnableStereoPan;
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void RefreshDeviceList()
    {
        _isRefreshing = true;
        try
        {
            var items = new List<DeviceComboItem>
            {
                new(string.Empty, MekTokController.FormatFriendlyAudioDeviceName(_controller.ActiveAudioDeviceName, isDefaultPrefix: true))
            };

            foreach (AudioOutputDeviceInfo dev in _controller.GetOutputDevices())
            {
                string friendly = MekTokController.FormatFriendlyAudioDeviceName(dev.FriendlyName);
                string label = dev.IsDefault ? $"{friendly} (Default)" : friendly;
                items.Add(new DeviceComboItem(dev.Id, label));
            }

            DeviceCombo.ItemsSource = items;
            string current = _controller.Settings.OutputDeviceId ?? string.Empty;
            DeviceCombo.SelectedValue = items.Any(i => string.Equals(i.Id, current, StringComparison.OrdinalIgnoreCase))
                ? current
                : string.Empty;
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void OnMasterToggleChanged(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing) return;
        _controller.SetEnabled(MasterToggle.IsChecked == true);
    }

    private void OnSpeakerMuteClick(object sender, RoutedEventArgs e)
    {
        _controller.ToggleEnabled();
    }

    private void OnVolumeSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isRefreshing) return;
        int pct = (int)Math.Round(e.NewValue);
        VolumePercentText.Text = $"{pct}%";
        _controller.SetMasterVolume(pct / 100f);
    }

    private void OnProfileComboChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshing) return;
        if (ProfileCombo.SelectedValue is string id && !string.IsNullOrWhiteSpace(id))
        {
            _controller.SelectProfile(id);
        }
    }

    private void OnDeviceComboChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshing) return;
        if (DeviceCombo.SelectedValue is string id)
        {
            _controller.SelectOutputDevice(string.IsNullOrWhiteSpace(id) ? null : id);
        }
    }

    private void OnTestSoundClick(object sender, RoutedEventArgs e)
    {
        _controller.AuditionProfile(_controller.Settings.ActiveProfileId);
    }

    private void OnKeyUpChipClick(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing) return;
        bool val = KeyUpChip.IsChecked == true;
        _controller.UpdateAcoustics(a => a.EnableKeyUpSound = val, rebuildSynthBanks: false);
    }

    private void OnStereoPanChipClick(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing) return;
        bool val = StereoPanChip.IsChecked == true;
        _controller.UpdateAcoustics(a => a.EnableStereoPan = val, rebuildSynthBanks: false);
    }

    private void OnConfigureKeysClick(object sender, RoutedEventArgs e)
    {
        Hide();
        OpenConfiguratorRequested?.Invoke(this, EventArgs.Empty);
    }
}

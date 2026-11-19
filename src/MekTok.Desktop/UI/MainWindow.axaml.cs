using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MekTok.Core.Audio;
using MekTok.Core.Config;
using MekTok.Core.Keyboard;
using MekTok.Core.Packs;
using MekTok.Core.Platform;
using MekTok.Core.Services;

namespace MekTok.Desktop.UI;

public sealed record SwitchOptionItem(string Id, string DisplayName, string AccentHex)
{
    public override string ToString() => DisplayName;
}

public sealed record OutputDeviceItem(string Id, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed record ThemeChoiceItem(ThemePreference Preference, string Label)
{
    public override string ToString() => Label;
}

public partial class MainWindow : Window
{
    private readonly MekTokController _controller;
    private readonly Action<ThemePreference> _applyTheme;
    private readonly Dictionary<KeyGroup, ToggleButton> _groupChips = new();
    private readonly IReadOnlyList<ThemeChoiceItem> _themeChoices =
    [
        new(ThemePreference.System, "Follow System Theme"),
        new(ThemePreference.Dark, "Dark (Fluent)"),
        new(ThemePreference.Light, "Light (Fluent)")
    ];

    private KeyDefinition? _selectedKey = Ansi104Layout.TryGetByScanCode(0x0039); // Default to Spacebar
    private KeyGroup _selectedGroup = KeyGroup.Spacebar;
    private bool _editingGroupMode;
    private bool _isRefreshing;
    private bool _isCapturingHotkey;

    private string? _recordingComboId;
    private readonly HashSet<int> _recordedComboScanCodes = [];
    private TextBlock? _activeRecordingKeysBlock;

    public MainWindow()
    {
        _controller = null!;
        _applyTheme = _ => { };
        InitializeComponent();
    }

    public MainWindow(MekTokController controller, Action<ThemePreference> applyTheme)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _applyTheme = applyTheme ?? throw new ArgumentNullException(nameof(applyTheme));

        _isRefreshing = true;
        InitializeComponent();

        VisualKeyboard.BindController(_controller);
        VisualKeyboard.KeyClicked += OnVisualKeyClicked;

        BuildGroupChips();
        PopulateInspectorSwitchOptions();
        ThemeCombo.ItemsSource = _themeChoices;
        PopulateOsPrivacyCopy();

        _controller.StateChanged += (_, _) => Dispatcher.UIThread.Post(RefreshAllState);
        _controller.ProfilesChanged += (_, _) => Dispatcher.UIThread.Post(RefreshProfilesAndPacks);
        _controller.AudioDevicesChanged += (_, _) => Dispatcher.UIThread.Post(RefreshDevices);
        _controller.KeyboardsChanged += (_, _) => Dispatcher.UIThread.Post(RefreshKeyboards);
        _controller.CombosChanged += (_, _) => Dispatcher.UIThread.Post(RefreshCombosList);
        _controller.KeyActivityObserved += OnKeyActivityObserved;

        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty)
            {
                _controller.NotifyConfiguratorVisibility(IsVisible);
                if (IsVisible)
                {
                    RefreshAllState();
                    RefreshRunningProcesses();
                    RefreshKeyboards();
                    RefreshDevices();
                }
            }
        };

        RefreshAllState();
        RefreshDevices();
        RefreshKeyboards();
        RefreshRunningProcesses();
    }

    public bool AllowRealClose { get; set; }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!AllowRealClose)
        {
            e.Cancel = true;
            Hide();
            _controller.NotifyConfiguratorVisibility(false);
            return;
        }
        base.OnClosing(e);
    }

    private void PopulateOsPrivacyCopy()
    {
        if (OperatingSystem.IsMacOS())
        {
            PrivacyBehaviorText.Text =
                "• Zero Keystroke Logging: Hardware key events are mapped in memory to play the matching switch sound and discarded immediately. Nothing is ever logged or sent over the network.\n\n" +
                "• macOS Permissions & Secure Input: Requires Input Monitoring (and Accessibility) in System Settings → Privacy & Security. Password prompts and Secure Input fields stay silent by macOS design.";
        }
        else if (OperatingSystem.IsLinux())
        {
            PrivacyBehaviorText.Text =
                "• Zero Keystroke Logging: Key events are matched strictly in memory to trigger low-latency audio and discarded immediately. Zero telemetry, zero disk logging.\n\n" +
                "• Linux Desktop Behavior: Runs with standard user permissions on X11 (Linux Mint Cinnamon default). Under Wayland sessions, native compositor security restricts global key capture outside XWayland.";
        }
        else
        {
            PrivacyBehaviorText.Text =
                "• Zero Keystroke Logging: Key events are inspected strictly in memory to play the matching audio clip and discarded immediately. Nothing is ever logged or sent over the network.";
        }
    }

    private void BuildGroupChips()
    {
        GroupChipsPanel.Children.Clear();
        _groupChips.Clear();

        foreach (KeyGroup group in Enum.GetValues<KeyGroup>())
        {
            var btn = new ToggleButton
            {
                Content = group.ToDisplayName(),
                Margin = new Thickness(0, 0, 6, 4)
            };
            btn.Classes.Add("chip");

            KeyGroup captured = group;
            btn.Click += (_, _) => OnGroupChipClicked(captured);
            GroupChipsPanel.Children.Add(btn);
            _groupChips[group] = btn;
        }
    }

    private void PopulateInspectorSwitchOptions()
    {
        var items = new List<SwitchOptionItem>
        {
            new(string.Empty, "Inherit from Active Profile", "#848B9B")
        };

        foreach (SwitchProfileDefinition def in BuiltInProfiles.All)
        {
            items.Add(new SwitchOptionItem(def.Id, def.Name, def.AccentHex));
        }

        InspectorSwitchCombo.ItemsSource = items;
    }

    private void OnNavChanged(object? sender, RoutedEventArgs e)
    {
        if (ViewConfigurator is null || ViewAcoustics is null || ViewSettings is null || ViewCombos is null) return;

        ViewConfigurator.IsVisible = NavConfigurator.IsChecked == true;
        ViewAcoustics.IsVisible = NavAcoustics.IsChecked == true;
        ViewCombos.IsVisible = NavCombos.IsChecked == true;
        ViewSettings.IsVisible = NavSettings.IsChecked == true;
    }

    private void OnHideToTrayClick(object? sender, RoutedEventArgs e) => Hide();

    private void OnKeyActivityObserved(int scanCode, bool isKeyUp)
    {
        VisualKeyboard.SetKeyPressed(scanCode, !isKeyUp);

        if (_recordingComboId is not null && !isKeyUp)
        {
            _recordedComboScanCodes.Add(scanCode);
            if (_activeRecordingKeysBlock is not null)
            {
                IEnumerable<string> labels = _recordedComboScanCodes
                    .OrderBy(c => c)
                    .Select(CustomKeyCombo.FormatScanCode);
                _activeRecordingKeysBlock.Text = string.Join(" + ", labels);
            }
        }
    }

    private void RefreshAllState()
    {
        _isRefreshing = true;
        try
        {
            AppSettings s = _controller.Settings;
            SidebarMasterToggle.IsChecked = s.IsEnabled;

            IReadOnlyList<ProfileChoiceItem> profiles = _controller.GetAllProfiles();
            string activeProfileName = profiles
                .FirstOrDefault(p => string.Equals(p.Id, s.ActiveProfileId, StringComparison.OrdinalIgnoreCase))
                ?.DisplayName ?? "Creamy Thock (Holy Panda)";

            string statusPrefix = !s.IsEnabled
                ? "Muted"
                : _controller.IsAutoPaused
                    ? $"Paused ({_controller.AutoPauseReason})"
                    : "Active";

            SidebarStatusText.Text = $"{statusPrefix} • {activeProfileName}";
            SidebarDeviceText.Text = MekTokController.FormatFriendlyAudioDeviceName(_controller.ActiveAudioDeviceName);

            double? latencyMs = _controller.AudioLatencyMs;
            AudioLatencyText.Text = _controller.HasActiveAudioOutput
                ? (latencyMs.HasValue ? $"Audio output active (~{latencyMs.Value:F1} ms buffer)" : "Audio output active")
                : "Audio output unavailable";

            ConfigProfileCombo.ItemsSource = profiles;
            ConfigProfileCombo.SelectedItem = profiles.FirstOrDefault(p =>
                string.Equals(p.Id, s.ActiveProfileId, StringComparison.OrdinalIgnoreCase));

            HideNumpadCheck.IsChecked = s.HideNumpadInConfigurator;
            VisualKeyboard.SetHideNumpad(s.HideNumpadInConfigurator);

            RefreshGroupChipHighlights();
            VisualKeyboard.SetSelection(_editingGroupMode ? null : _selectedKey?.ScanCode, _selectedGroup);
            VisualKeyboard.RefreshStyles();

            RefreshInspector();
            RefreshBuiltInSwitchCards();
            RefreshAcousticsControls();
            RefreshImportedPacksList();
            RefreshExcludedAppsList();
            RefreshCombosList();

            HotkeyEnabledCheck.IsChecked = s.MuteHotkey.Enabled;
            if (!_isCapturingHotkey)
            {
                HotkeyCaptureBox.Text = s.MuteHotkey.Enabled
                    ? s.MuteHotkey.ToDisplayString()
                    : "Disabled";
            }
            HotkeyStatusText.Text = _controller.LastHotkeyError ?? string.Empty;

            bool monitorSupported = _controller.IsActiveWindowMonitorSupported;
            AutoPauseUnsupportedText.IsVisible = !monitorSupported;
            MuteFullscreenCheck.IsEnabled = monitorSupported;
            MuteFullscreenCheck.IsChecked = s.MuteInFullscreen;

            StartWithWindowsCheck.IsChecked = s.StartWithWindows;
            ThemeCombo.SelectedItem = _themeChoices.FirstOrDefault(t => t.Preference == s.Theme) ?? _themeChoices[0];
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void RefreshProfilesAndPacks() => RefreshAllState();

    private void RefreshGroupChipHighlights()
    {
        foreach ((KeyGroup group, ToggleButton btn) in _groupChips)
        {
            btn.IsChecked = _editingGroupMode && group == _selectedGroup;
        }
    }

    private void OnVisualKeyClicked(KeyDefinition key)
    {
        _selectedKey = key;
        _selectedGroup = key.Group;
        _editingGroupMode = false;
        _controller.AuditionKey(key.ScanCode);

        _isRefreshing = true;
        try
        {
            RefreshGroupChipHighlights();
            VisualKeyboard.SetSelection(key.ScanCode, key.Group);
            RefreshInspector();
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void OnGroupChipClicked(KeyGroup group)
    {
        _selectedGroup = group;
        _selectedKey = null;
        _editingGroupMode = true;

        KeyDefinition? sampleKey = Ansi104Layout.GetKeysByGroup(group).FirstOrDefault();
        if (sampleKey is not null)
        {
            _controller.AuditionKey(sampleKey.ScanCode);
        }

        _isRefreshing = true;
        try
        {
            RefreshGroupChipHighlights();
            VisualKeyboard.SetSelection(null, group);
            RefreshInspector();
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void RefreshInspector()
    {
        if (_editingGroupMode)
        {
            int count = Ansi104Layout.GetKeysByGroup(_selectedGroup).Count;
            InspectorTitle.Text = $"Group: {_selectedGroup.ToDisplayName()} ({count} keys)";
            SoundOverride? ov = _controller.GetGroupOverride(_selectedGroup);
            InspectorSubtitle.Text = ov is { IsEmpty: false }
                ? "Group Override active • Applies to keys in this group unless overridden per-key"
                : "Inheriting from Active Profile";
            BindInspectorOverride(ov);
        }
        else if (_selectedKey is not null)
        {
            InspectorTitle.Text = $"Key: {_selectedKey.KeyId} ({_selectedKey.Group.ToDisplayName()})";
            SoundOverride? keyOv = _controller.GetKeyOverride(_selectedKey.ScanCode);
            SoundOverride? grpOv = _controller.GetGroupOverride(_selectedKey.Group);

            InspectorSubtitle.Text = keyOv is { IsEmpty: false }
                ? "Per-Key Override active"
                : grpOv is { IsEmpty: false }
                    ? $"Inheriting from Group Override ({_selectedKey.Group.ToDisplayName()})"
                    : "Inheriting from Active Profile";

            BindInspectorOverride(keyOv);
        }
    }

    private void BindInspectorOverride(SoundOverride? ov)
    {
        ClearOverrideButton.IsEnabled = ov is { IsEmpty: false };
        InspectorMuteCheck.IsChecked = ov?.IsMuted ?? false;

        string targetSwitchId = ov?.BuiltInSwitchOverrideId ?? string.Empty;
        if (InspectorSwitchCombo.ItemsSource is IEnumerable<SwitchOptionItem> switchItems)
        {
            InspectorSwitchCombo.SelectedItem = switchItems.FirstOrDefault(i =>
                string.Equals(i.Id, targetSwitchId, StringComparison.OrdinalIgnoreCase));
        }

        int volPct = (int)Math.Round((ov?.VolumeMultiplier ?? 1.0f) * 100);
        InspectorVolumeSlider.Value = volPct;
        InspectorVolumeText.Text = $"{volPct}%";

        float pitch = ov?.PitchOffsetSemitones ?? 0f;
        InspectorPitchSlider.Value = pitch;
        InspectorPitchText.Text = $"{pitch:+0.0;-0.0;0.0} st";

        InspectorPressFileBox.Text = string.IsNullOrWhiteSpace(ov?.CustomPressAudioPath)
            ? "(Inherited)"
            : System.IO.Path.GetFileName(ov.CustomPressAudioPath);

        InspectorReleaseFileBox.Text = string.IsNullOrWhiteSpace(ov?.CustomReleaseAudioPath)
            ? "(Inherited)"
            : System.IO.Path.GetFileName(ov.CustomReleaseAudioPath);
    }

    private SoundOverride GetEditableOverrideCopy()
    {
        SoundOverride? current = _editingGroupMode
            ? _controller.GetGroupOverride(_selectedGroup)
            : (_selectedKey is not null ? _controller.GetKeyOverride(_selectedKey.ScanCode) : null);

        return current?.Clone() ?? new SoundOverride();
    }

    private void SaveInspectorOverride(SoundOverride updated)
    {
        if (_editingGroupMode)
        {
            _controller.SetGroupOverride(_selectedGroup, updated);
        }
        else if (_selectedKey is not null)
        {
            _controller.SetKeyOverride(_selectedKey.ScanCode, updated);
        }
    }

    private void OnInspectorControlChanged(object? sender, RoutedEventArgs e)
    {
        if (_isRefreshing) return;

        SoundOverride ov = GetEditableOverrideCopy();
        ov.IsMuted = InspectorMuteCheck.IsChecked == true;
        ov.BuiltInSwitchOverrideId = (InspectorSwitchCombo.SelectedItem as SwitchOptionItem)?.Id;
        SaveInspectorOverride(ov);
    }

    private void OnInspectorSliderChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isRefreshing) return;

        int volPct = (int)Math.Round(InspectorVolumeSlider.Value);
        float pitch = (float)Math.Round(InspectorPitchSlider.Value, 1);

        InspectorVolumeText.Text = $"{volPct}%";
        InspectorPitchText.Text = $"{pitch:+0.0;-0.0;0.0} st";

        SoundOverride ov = GetEditableOverrideCopy();
        ov.VolumeMultiplier = volPct / 100f;
        ov.PitchOffsetSemitones = pitch;
        SaveInspectorOverride(ov);
    }

    private async void OnBrowsePressAudioClick(object? sender, RoutedEventArgs e) =>
        await BrowseCustomAudioAsync(isRelease: false);

    private async void OnBrowseReleaseAudioClick(object? sender, RoutedEventArgs e) =>
        await BrowseCustomAudioAsync(isRelease: true);

    private async Task BrowseCustomAudioAsync(bool isRelease)
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = isRelease ? "Select Custom Release Audio" : "Select Custom Key Press Audio",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Audio Files") { Patterns = ["*.wav", "*.ogg", "*.mp3"] },
                FilePickerFileTypes.All
            ]
        });

        string? localPath = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(localPath)) return;

        try
        {
            string storedPath = _controller.CopyCustomAudioToStore(localPath);
            SoundOverride ov = GetEditableOverrideCopy();
            if (isRelease) ov.CustomReleaseAudioPath = storedPath;
            else ov.CustomPressAudioPath = storedPath;
            SaveInspectorOverride(ov);
        }
        catch (Exception ex)
        {
            await DialogWindow.ShowInfoAsync(this, "MekTok Audio Import", $"Could not load selected audio file: {ex.Message}");
        }
    }

    private void OnClearPressAudioClick(object? sender, RoutedEventArgs e)
    {
        SoundOverride ov = GetEditableOverrideCopy();
        ov.CustomPressAudioPath = null;
        SaveInspectorOverride(ov);
    }

    private void OnClearReleaseAudioClick(object? sender, RoutedEventArgs e)
    {
        SoundOverride ov = GetEditableOverrideCopy();
        ov.CustomReleaseAudioPath = null;
        SaveInspectorOverride(ov);
    }

    private void OnAuditionSelectedClick(object? sender, RoutedEventArgs e)
    {
        int scan = _editingGroupMode
            ? (Ansi104Layout.GetKeysByGroup(_selectedGroup).FirstOrDefault()?.ScanCode ?? 0x001E)
            : (_selectedKey?.ScanCode ?? 0x0039);
        _controller.AuditionKey(scan);
    }

    private void OnClearSelectedOverrideClick(object? sender, RoutedEventArgs e)
    {
        if (_editingGroupMode)
        {
            _controller.SetGroupOverride(_selectedGroup, null);
        }
        else if (_selectedKey is not null)
        {
            _controller.SetKeyOverride(_selectedKey.ScanCode, null);
        }
    }

    private async void OnResetProfileOverridesClick(object? sender, RoutedEventArgs e)
    {
        bool confirm = await DialogWindow.ShowConfirmAsync(
            this,
            "Reset Profile Overrides",
            "Clear all key and group overrides for the active profile?");

        if (confirm)
        {
            _controller.ResetActiveProfileCustomizations();
        }
    }

    private void OnMasterToggleChanged(object? sender, RoutedEventArgs e)
    {
        if (_isRefreshing) return;
        _controller.SetEnabled(SidebarMasterToggle.IsChecked == true);
    }

    private void OnConfigProfileComboChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshing) return;
        if (ConfigProfileCombo.SelectedItem is ProfileChoiceItem item && !string.IsNullOrWhiteSpace(item.Id))
        {
            _controller.SelectProfile(item.Id);
        }
    }

    private void OnTestCurrentProfileClick(object? sender, RoutedEventArgs e)
    {
        _controller.AuditionProfile(_controller.Settings.ActiveProfileId);
    }

    private void OnHideNumpadChanged(object? sender, RoutedEventArgs e)
    {
        if (_isRefreshing) return;
        _controller.SetHideNumpad(HideNumpadCheck.IsChecked == true);
    }

    private void RefreshBuiltInSwitchCards()
    {
        BuiltInSwitchGrid.Children.Clear();

        foreach (SwitchProfileDefinition def in BuiltInProfiles.All)
        {
            bool isSelected = string.Equals(
                _controller.Settings.ActiveProfileId,
                def.Id,
                StringComparison.OrdinalIgnoreCase);

            var card = new Border
            {
                Padding = new Thickness(14),
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(isSelected ? 1.8 : 1.0),
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            card[!Border.BackgroundProperty] = new DynamicResourceExtension(isSelected ? "AccentSoftBrush" : "SurfaceCardBrush");
            card[!Border.BorderBrushProperty] = new DynamicResourceExtension(isSelected ? "AccentBrush" : "BorderSubtleBrush");

            var stack = new StackPanel { Spacing = 4 };
            var topGrid = new Grid
            {
                ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"),
                Margin = new Thickness(0, 0, 0, 4)
            };

            var titleStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (Color.TryParse(def.AccentHex, out Color accentColor))
            {
                titleStack.Children.Add(new Ellipse
                {
                    Width = 10,
                    Height = 10,
                    Fill = new SolidColorBrush(accentColor),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            var nameBlock = new TextBlock
            {
                Text = def.Name,
                FontSize = 14,
                FontWeight = FontWeight.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            nameBlock[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextPrimaryBrush");
            titleStack.Children.Add(nameBlock);
            Grid.SetColumn(titleStack, 0);
            topGrid.Children.Add(titleStack);

            var testBtn = new Button
            {
                Content = "Test",
                Padding = new Thickness(10, 4)
            };
            testBtn.Classes.Add("secondary");
            string capturedId = def.Id;
            testBtn.Click += (_, ev) =>
            {
                ev.Handled = true;
                _controller.AuditionProfile(capturedId);
            };
            Grid.SetColumn(testBtn, 1);
            topGrid.Children.Add(testBtn);

            var taglineBlock = new TextBlock
            {
                Text = def.Tagline,
                FontSize = 11.5,
                FontWeight = FontWeight.Medium
            };
            taglineBlock[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("AccentBrush");

            var descBlock = new TextBlock
            {
                Text = def.Description,
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap
            };
            descBlock[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextSecondaryBrush");

            stack.Children.Add(topGrid);
            stack.Children.Add(taglineBlock);
            stack.Children.Add(descBlock);
            card.Child = stack;

            card.PointerPressed += (_, _) =>
            {
                _controller.SelectProfile(capturedId);
                _controller.AuditionProfile(capturedId);
            };

            BuiltInSwitchGrid.Children.Add(card);
        }
    }

    private void RefreshAcousticsControls()
    {
        AppSettings s = _controller.Settings;
        AcousticSettings ac = s.Acoustics;

        int masterPct = (int)Math.Round(s.MasterVolume * 100);
        AcMasterVolSlider.Value = masterPct;
        AcMasterVolText.Text = $"{masterPct}%";

        AcPitchSlider.Value = ac.PitchShiftSemitones;
        AcPitchText.Text = $"{ac.PitchShiftSemitones:+0.0;-0.0;0.0} st";

        int thockPct = (int)Math.Round(ac.ThockAmount * 100);
        AcThockSlider.Value = thockPct;
        AcThockText.Text = $"{thockPct}%";

        int brightPct = (int)Math.Round(ac.BrightnessAmount * 100);
        AcBrightSlider.Value = brightPct;
        AcBrightText.Text = $"{brightPct}%";

        AcKeyUpCheck.IsChecked = ac.EnableKeyUpSound;
        int keyUpPct = (int)Math.Round(ac.KeyUpVolume * 100);
        AcKeyUpVolSlider.Value = keyUpPct;
        AcKeyUpVolText.Text = $"{keyUpPct}%";

        int humPct = (int)Math.Round(ac.HumanizeAmount * 100);
        AcHumanizeSlider.Value = humPct;
        AcHumanizeText.Text = $"{humPct}%";

        AcStereoPanCheck.IsChecked = ac.EnableStereoPan;
        int panPct = (int)Math.Round(ac.StereoPanWidth * 100);
        AcPanWidthSlider.Value = panPct;
        AcPanWidthText.Text = $"{panPct}%";

        AcSuppressRepeatCheck.IsChecked = ac.SuppressHoldRepeat;
        AcEditingRepeatCheck.IsChecked = ac.AllowEditingKeyRepeat;
    }

    private void OnAcousticSliderChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isRefreshing) return;

        if (ReferenceEquals(sender, AcMasterVolSlider))
        {
            int masterPct = (int)Math.Round(AcMasterVolSlider.Value);
            AcMasterVolText.Text = $"{masterPct}%";
            _controller.SetMasterVolume(masterPct / 100f);
            return;
        }

        float pitch = (float)Math.Round(AcPitchSlider.Value, 1);
        int thockPct = (int)Math.Round(AcThockSlider.Value);
        int brightPct = (int)Math.Round(AcBrightSlider.Value);
        int keyUpPct = (int)Math.Round(AcKeyUpVolSlider.Value);
        int humPct = (int)Math.Round(AcHumanizeSlider.Value);
        int panPct = (int)Math.Round(AcPanWidthSlider.Value);

        AcPitchText.Text = $"{pitch:+0.0;-0.0;0.0} st";
        AcThockText.Text = $"{thockPct}%";
        AcBrightText.Text = $"{brightPct}%";
        AcKeyUpVolText.Text = $"{keyUpPct}%";
        AcHumanizeText.Text = $"{humPct}%";
        AcPanWidthText.Text = $"{panPct}%";

        bool needsSynthRebuild = ReferenceEquals(sender, AcThockSlider) || ReferenceEquals(sender, AcBrightSlider);
        _controller.UpdateAcoustics(ac =>
        {
            ac.PitchShiftSemitones = pitch;
            ac.ThockAmount = thockPct / 100f;
            ac.BrightnessAmount = brightPct / 100f;
            ac.KeyUpVolume = keyUpPct / 100f;
            ac.HumanizeAmount = humPct / 100f;
            ac.StereoPanWidth = panPct / 100f;
        }, rebuildSynthBanks: needsSynthRebuild);
    }

    private void OnAcousticToggleChanged(object? sender, RoutedEventArgs e)
    {
        if (_isRefreshing) return;
        _controller.UpdateAcoustics(ac =>
        {
            ac.EnableKeyUpSound = AcKeyUpCheck.IsChecked == true;
            ac.EnableStereoPan = AcStereoPanCheck.IsChecked == true;
            ac.SuppressHoldRepeat = AcSuppressRepeatCheck.IsChecked == true;
            ac.AllowEditingKeyRepeat = AcEditingRepeatCheck.IsChecked == true;
        }, rebuildSynthBanks: false);
    }

    private void RefreshImportedPacksList()
    {
        ImportedPacksPanel.Children.Clear();
        IReadOnlyList<ImportedSoundPack> packs = _controller.GetImportedPacks();

        if (packs.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = "No Mechvibes packs imported yet. Click 'Import Folder...' or 'Import .zip...' above to add community packs.",
                FontSize = 12,
                Margin = new Thickness(0, 6, 0, 2)
            };
            empty[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMutedBrush");
            ImportedPacksPanel.Children.Add(empty);
            return;
        }

        foreach (ImportedSoundPack pack in packs)
        {
            bool active = string.Equals(_controller.Settings.ActiveProfileId, pack.Id, StringComparison.OrdinalIgnoreCase);

            var row = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 8)
            };
            row[!Border.BackgroundProperty] = new DynamicResourceExtension(active ? "AccentSoftBrush" : "SurfaceSubtleBrush");
            row[!Border.BorderBrushProperty] = new DynamicResourceExtension(active ? "AccentBrush" : "BorderSubtleBrush");

            var grid = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };

            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var title = new TextBlock
            {
                Text = pack.Name,
                FontSize = 13,
                FontWeight = FontWeight.SemiBold
            };
            title[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextPrimaryBrush");
            var sub = new TextBlock
            {
                Text = $"{pack.PressClips.Count} mapped keys" + (pack.ReleaseClips.Count > 0 ? $" • {pack.ReleaseClips.Count} release sounds" : string.Empty),
                FontSize = 11.5
            };
            sub[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextSecondaryBrush");
            info.Children.Add(title);
            info.Children.Add(sub);
            Grid.SetColumn(info, 0);
            grid.Children.Add(info);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            Grid.SetColumn(actions, 1);

            ImportedSoundPack captured = pack;
            var useBtn = new Button
            {
                Content = active ? "Active" : "Use Pack",
                IsEnabled = !active
            };
            useBtn.Classes.Add("secondary");
            useBtn.Click += (_, _) => _controller.SelectProfile(captured.Id);

            var delBtn = new Button { Content = "Delete" };
            delBtn.Classes.Add("secondary");
            delBtn.Click += (_, _) => _controller.DeleteImportedPack(captured);

            actions.Children.Add(useBtn);
            actions.Children.Add(delBtn);
            grid.Children.Add(actions);

            row.Child = grid;
            ImportedPacksPanel.Children.Add(row);
        }
    }

    private async void OnImportPackFolderClick(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select a Mechvibes sound pack folder containing config.json",
            AllowMultiple = false
        });

        string? localPath = folders.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(localPath)) return;

        try
        {
            ImportedSoundPack pack = await _controller.ImportMechvibesFolderAsync(localPath);
            await DialogWindow.ShowInfoAsync(
                this,
                "Mechvibes Pack Imported",
                $"Imported '{pack.Name}' ({pack.PressClips.Count} keys) and set as active profile.");
        }
        catch (Exception ex)
        {
            await DialogWindow.ShowInfoAsync(this, "Pack Import Error", $"Failed to import pack folder: {ex.Message}");
        }
    }

    private async void OnImportPackZipClick(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select a Mechvibes Sound Pack (.zip)",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("ZIP Archives") { Patterns = ["*.zip"] }
            ]
        });

        string? localPath = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(localPath)) return;

        try
        {
            ImportedSoundPack pack = await _controller.ImportMechvibesZipAsync(localPath);
            await DialogWindow.ShowInfoAsync(
                this,
                "Mechvibes Pack Imported",
                $"Imported '{pack.Name}' ({pack.PressClips.Count} keys) and set as active profile.");
        }
        catch (Exception ex)
        {
            await DialogWindow.ShowInfoAsync(this, "Pack Import Error", $"Failed to import pack archive: {ex.Message}");
        }
    }

    private void RefreshDevices()
    {
        _isRefreshing = true;
        try
        {
            var items = new List<OutputDeviceItem>
            {
                new(string.Empty, MekTokController.FormatFriendlyAudioDeviceName(_controller.ActiveAudioDeviceName, isDefaultPrefix: true))
            };

            foreach (AudioOutputDeviceInfo dev in _controller.GetOutputDevices())
            {
                string friendly = MekTokController.FormatFriendlyAudioDeviceName(dev.FriendlyName);
                string label = dev.IsDefault ? $"{friendly} (Default)" : friendly;
                items.Add(new OutputDeviceItem(dev.Id, label));
            }

            SettingsDeviceCombo.ItemsSource = items;
            string current = _controller.Settings.OutputDeviceId ?? string.Empty;
            SettingsDeviceCombo.SelectedItem = items.FirstOrDefault(i =>
                string.Equals(i.Id, current, StringComparison.OrdinalIgnoreCase)) ?? items[0];
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void OnSettingsDeviceChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshing) return;
        if (SettingsDeviceCombo.SelectedItem is OutputDeviceItem item)
        {
            _controller.SelectOutputDevice(string.IsNullOrWhiteSpace(item.Id) ? null : item.Id);
        }
    }

    private void OnRefreshDevicesClick(object? sender, RoutedEventArgs e) => RefreshDevices();

    private void OnHotkeyEnabledChanged(object? sender, RoutedEventArgs e)
    {
        if (_isRefreshing) return;
        HotkeyConfig updated = new()
        {
            Enabled = HotkeyEnabledCheck.IsChecked == true,
            Modifiers = _controller.Settings.MuteHotkey.Modifiers,
            Key = _controller.Settings.MuteHotkey.Key
        };
        _controller.SetMuteHotkey(updated);
    }

    private void OnHotkeyCaptureGotFocus(object? sender, FocusChangedEventArgs e)
    {
        _isCapturingHotkey = true;
        HotkeyCaptureBox.Text = "Press shortcut combination...";
    }

    private void OnHotkeyCaptureLostFocus(object? sender, RoutedEventArgs e)
    {
        _isCapturingHotkey = false;
        HotkeyCaptureBox.Text = _controller.Settings.MuteHotkey.Enabled
            ? _controller.Settings.MuteHotkey.ToDisplayString()
            : "Disabled";
    }

    private void OnHotkeyCaptureKeyDown(object? sender, KeyEventArgs e)
    {
        e.Handled = true;
        if (AvaloniaKeyMapper.IsModifierKey(e.Key))
        {
            return;
        }

        HotkeyModifiers hkMods = AvaloniaKeyMapper.ToHotkeyModifiers(e.KeyModifiers);
        if (hkMods == HotkeyModifiers.None)
        {
            HotkeyStatusText.Text = "Include at least one modifier key (Ctrl, Alt, Shift, or Super/Cmd).";
            return;
        }

        HotkeyKey hkKey = AvaloniaKeyMapper.ToHotkeyKey(e.Key);
        if (hkKey == HotkeyKey.None)
        {
            HotkeyStatusText.Text = $"Key '{e.Key}' cannot be mapped as a global shortcut.";
            return;
        }

        var config = new HotkeyConfig
        {
            Enabled = true,
            Modifiers = hkMods,
            Key = hkKey
        };

        _isCapturingHotkey = false;
        (bool ok, string? err) = _controller.SetMuteHotkey(config);
        HotkeyCaptureBox.Text = _controller.Settings.MuteHotkey.ToDisplayString();
        HotkeyStatusText.Text = ok ? "Shortcut registered." : (err ?? "Could not register shortcut.");
        TopLevel.GetTopLevel(this)?.FocusManager?.Focus(null);
    }

    private void OnResetHotkeyClick(object? sender, RoutedEventArgs e)
    {
        _controller.SetMuteHotkey(new HotkeyConfig());
    }

    private void OnMuteFullscreenChanged(object? sender, RoutedEventArgs e)
    {
        if (_isRefreshing) return;
        _controller.SetMuteInFullscreen(MuteFullscreenCheck.IsChecked == true);
    }

    private void RefreshRunningProcesses()
    {
        IReadOnlyList<string> procs = _controller.GetRunningUserProcesses();
        RunningAppsCombo.ItemsSource = procs;
        if (procs.Count > 0 && RunningAppsCombo.SelectedIndex < 0)
        {
            RunningAppsCombo.SelectedIndex = 0;
        }
    }

    private void RefreshExcludedAppsList()
    {
        ExcludedAppsPanel.Children.Clear();
        List<string> excluded = _controller.Settings.ExcludedProcessNames;

        if (excluded.Count == 0)
        {
            var none = new TextBlock
            {
                Text = "No excluded apps configured.",
                FontSize = 11.5
            };
            none[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMutedBrush");
            ExcludedAppsPanel.Children.Add(none);
            return;
        }

        foreach (string proc in excluded)
        {
            var row = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };

            var nameBlock = new TextBlock
            {
                Text = OperatingSystem.IsWindows() ? proc + ".exe" : proc,
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center
            };
            nameBlock[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextPrimaryBrush");
            Grid.SetColumn(nameBlock, 0);
            row.Children.Add(nameBlock);

            string captured = proc;
            var removeBtn = new Button
            {
                Content = "Remove",
                Padding = new Thickness(10, 3)
            };
            removeBtn.Classes.Add("secondary");
            removeBtn.Click += (_, _) => _controller.RemoveExcludedProcess(captured);
            Grid.SetColumn(removeBtn, 1);
            row.Children.Add(removeBtn);

            ExcludedAppsPanel.Children.Add(row);
        }
    }

    private void OnRefreshRunningAppsClick(object? sender, RoutedEventArgs e) => RefreshRunningProcesses();

    private void OnAddSelectedProcessClick(object? sender, RoutedEventArgs e)
    {
        if (RunningAppsCombo.SelectedItem is string proc)
        {
            _controller.AddExcludedProcess(proc);
        }
    }

    private void OnAddManualProcessClick(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(ManualProcessBox.Text))
        {
            _controller.AddExcludedProcess(ManualProcessBox.Text);
            ManualProcessBox.Text = string.Empty;
        }
    }

    private void RefreshKeyboards()
    {
        KeyboardsListPanel.Children.Clear();

        string? notice = _controller.KeyboardPermissionNotice;
        PermissionNoticeBanner.IsVisible = !string.IsNullOrWhiteSpace(notice);
        PermissionNoticeText.Text = notice ?? string.Empty;

        if (!_controller.SupportsPerDeviceKeyboardFiltering)
        {
            KeyboardModeDescription.Text =
                "Capturing global keystrokes across all keyboards via SharpHook (libuiohook).";
            var info = new TextBlock
            {
                Text = "Per-device keyboard filtering requires native XInput2 (Linux) or IOHIDManager (macOS) device handles; all connected keyboards are active.",
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap
            };
            info[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMutedBrush");
            KeyboardsListPanel.Children.Add(info);
            return;
        }

        KeyboardModeDescription.Text =
            "All keyboards trigger sounds by default. Uncheck a hardware keyboard below to ignore it.";

        IReadOnlyList<KeyboardDeviceInfo> keyboards = _controller.GetConnectedKeyboards();
        if (keyboards.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = "No physical HID keyboards enumerated.",
                FontSize = 12
            };
            empty[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMutedBrush");
            KeyboardsListPanel.Children.Add(empty);
            return;
        }

        foreach (KeyboardDeviceInfo kb in keyboards)
        {
            bool enabled = !_controller.Settings.DisabledKeyboardDeviceIds.Contains(kb.DeviceId);
            var cb = new CheckBox
            {
                Content = kb.DisplayName,
                IsChecked = enabled,
                FontSize = 12.5
            };
            string capturedId = kb.DeviceId;
            cb.IsCheckedChanged += (_, _) => _controller.SetKeyboardEnabled(capturedId, cb.IsChecked == true);
            KeyboardsListPanel.Children.Add(cb);
        }
    }

    private void OnRefreshKeyboardsClick(object? sender, RoutedEventArgs e) => RefreshKeyboards();

    private void OnThemeComboChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshing) return;
        if (ThemeCombo.SelectedItem is ThemeChoiceItem item)
        {
            _controller.SetTheme(item.Preference);
            _applyTheme(item.Preference);
            RefreshKeyboards();
        }
    }

    private void OnStartupToggleChanged(object? sender, RoutedEventArgs e)
    {
        if (_isRefreshing) return;
        _controller.SetStartOnLogin(StartWithWindowsCheck.IsChecked == true);
    }

    private void OnOpenDataFolderClick(object? sender, RoutedEventArgs e) => _controller.OpenDataDirectory();

    private void RefreshCombosList()
    {
        if (CombosListPanel is null) return;

        CombosListPanel.Children.Clear();
        List<CustomKeyCombo> combos = _controller.Settings.CustomCombos;

        if (combos.Count == 0)
        {
            var empty = new Border { Padding = new Thickness(24) };
            empty.Classes.Add("card");
            var emptyStack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 8
            };
            emptyStack.Children.Add(new TextBlock
            {
                Text = "No custom key combinations created yet.",
                FontSize = 14,
                FontWeight = FontWeight.Medium,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            var hint = new TextBlock
            {
                Text = "Click '+ Add New Combo' above to map chords like Shift+Y+! or Ctrl+Alt+W to custom sound effects.",
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            hint[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextSecondaryBrush");
            emptyStack.Children.Add(hint);

            var addBtn = new Button
            {
                Content = "+ Create First Combo",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            addBtn.Classes.Add("primary");
            addBtn.Click += OnAddNewComboClick;
            emptyStack.Children.Add(addBtn);

            empty.Child = emptyStack;
            CombosListPanel.Children.Add(empty);
            return;
        }

        foreach (CustomKeyCombo combo in combos)
        {
            var card = new Border { Padding = new Thickness(16) };
            card.Classes.Add("card");

            var rootStack = new StackPanel { Spacing = 10 };

            // Header row
            var headerGrid = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto,Auto") };

            var nameBox = new TextBox
            {
                Text = combo.Name,
                FontSize = 14,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            string capturedId = combo.Id;
            nameBox.LostFocus += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(nameBox.Text)) nameBox.Text = "Custom Combo";
                combo.Name = nameBox.Text.Trim();
                _controller.UpdateCustomCombo(combo);
            };
            nameBox.KeyDown += (_, ke) =>
            {
                if (ke.Key == Key.Enter)
                {
                    TopLevel.GetTopLevel(this)?.FocusManager?.Focus(null);
                }
            };
            Grid.SetColumn(nameBox, 0);
            headerGrid.Children.Add(nameBox);

            var enableToggle = new CheckBox
            {
                Content = "Enabled",
                IsChecked = combo.IsEnabled,
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            enableToggle.IsCheckedChanged += (_, _) =>
            {
                combo.IsEnabled = enableToggle.IsChecked == true;
                _controller.UpdateCustomCombo(combo);
            };
            Grid.SetColumn(enableToggle, 1);
            headerGrid.Children.Add(enableToggle);

            var deleteBtn = new Button
            {
                Content = "Delete",
                Padding = new Thickness(10, 4),
                VerticalAlignment = VerticalAlignment.Center
            };
            deleteBtn.Classes.Add("secondary");
            deleteBtn.Click += (_, _) => _controller.RemoveCustomCombo(capturedId);
            Grid.SetColumn(deleteBtn, 2);
            headerGrid.Children.Add(deleteBtn);

            rootStack.Children.Add(headerGrid);

            // Keys row
            var keysBorder = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8)
            };
            keysBorder[!Border.BackgroundProperty] = new DynamicResourceExtension("SurfaceSubtleBrush");
            keysBorder[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderSubtleBrush");

            var keysGrid = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto,Auto") };

            var keysDisplay = new TextBlock
            {
                Text = combo.FormatKeysDisplay(),
                FontSize = 13,
                FontWeight = FontWeight.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };
            keysDisplay[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("AccentBrush");
            Grid.SetColumn(keysDisplay, 0);
            keysGrid.Children.Add(keysDisplay);

            var recordBtn = new Button
            {
                Content = _recordingComboId == combo.Id
                    ? "Listening... (Press Keys & Click When Done)"
                    : "Record Keys",
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(12, 4),
                VerticalAlignment = VerticalAlignment.Center
            };
            recordBtn.Classes.Add(_recordingComboId == combo.Id ? "primary" : "secondary");
            recordBtn.Click += (_, _) =>
            {
                if (_recordingComboId == combo.Id)
                {
                    FinishRecordingCombo(combo, keysDisplay, recordBtn);
                }
                else
                {
                    StartRecordingCombo(combo, keysDisplay, recordBtn);
                }
            };
            Grid.SetColumn(recordBtn, 1);
            keysGrid.Children.Add(recordBtn);

            var clearKeysBtn = new Button
            {
                Content = "Clear Keys",
                Padding = new Thickness(10, 4),
                VerticalAlignment = VerticalAlignment.Center
            };
            clearKeysBtn.Classes.Add("secondary");
            clearKeysBtn.Click += (_, _) =>
            {
                combo.ScanCodes.Clear();
                _controller.UpdateCustomCombo(combo);
                keysDisplay.Text = combo.FormatKeysDisplay();
            };
            Grid.SetColumn(clearKeysBtn, 2);
            keysGrid.Children.Add(clearKeysBtn);

            keysBorder.Child = keysGrid;
            rootStack.Children.Add(keysBorder);

            // Sound & volume row
            var soundGrid = new Grid
            {
                ColumnDefinitions = ColumnDefinitions.Parse("1.4*,Auto,*,Auto,Auto")
            };

            var soundFileStack = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
            var soundLabel = new TextBlock { Text = "Audio Sound Effect", FontSize = 11 };
            soundLabel[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextSecondaryBrush");
            var soundFileText = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(combo.AudioFilePath)
                    ? "(No sound file selected)"
                    : System.IO.Path.GetFileName(combo.AudioFilePath),
                FontSize = 12.5,
                FontWeight = FontWeight.Medium,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            soundFileStack.Children.Add(soundLabel);
            soundFileStack.Children.Add(soundFileText);
            Grid.SetColumn(soundFileStack, 0);
            soundGrid.Children.Add(soundFileStack);

            var browseBtn = new Button
            {
                Content = "Browse Audio...",
                Margin = new Thickness(0, 0, 16, 0),
                Padding = new Thickness(10, 4),
                VerticalAlignment = VerticalAlignment.Center
            };
            browseBtn.Classes.Add("secondary");
            browseBtn.Click += async (_, _) =>
            {
                IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select Audio Effect for " + combo.Name,
                    AllowMultiple = false,
                    FileTypeFilter =
                    [
                        new FilePickerFileType("Audio Files") { Patterns = ["*.wav", "*.mp3", "*.ogg"] },
                        FilePickerFileTypes.All
                    ]
                });
                string? localPath = files.FirstOrDefault()?.TryGetLocalPath();
                if (string.IsNullOrWhiteSpace(localPath)) return;

                try
                {
                    string importedPath = _controller.ImportComboAudioFile(localPath);
                    combo.AudioFilePath = importedPath;
                    _controller.UpdateCustomCombo(combo);
                    soundFileText.Text = System.IO.Path.GetFileName(importedPath);
                }
                catch (Exception ex)
                {
                    await DialogWindow.ShowInfoAsync(this, "Error", "Could not import audio: " + ex.Message);
                }
            };
            Grid.SetColumn(browseBtn, 1);
            soundGrid.Children.Add(browseBtn);

            var volStack = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
            var volHeaderGrid = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };
            var volLabel = new TextBlock { Text = "Volume", FontSize = 11 };
            volLabel[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextSecondaryBrush");
            var volVal = new TextBlock
            {
                Text = $"{(int)Math.Round(combo.Volume * 100)}%",
                FontSize = 11,
                FontWeight = FontWeight.Medium
            };
            Grid.SetColumn(volLabel, 0);
            Grid.SetColumn(volVal, 1);
            volHeaderGrid.Children.Add(volLabel);
            volHeaderGrid.Children.Add(volVal);
            volStack.Children.Add(volHeaderGrid);

            var volSlider = new Slider
            {
                Minimum = 0,
                Maximum = 150,
                Value = Math.Round(combo.Volume * 100)
            };
            volSlider.ValueChanged += (_, ve) =>
            {
                int pct = (int)Math.Round(ve.NewValue);
                volVal.Text = $"{pct}%";
                combo.Volume = pct / 100f;
                _controller.UpdateCustomCombo(combo);
            };
            volStack.Children.Add(volSlider);
            Grid.SetColumn(volStack, 2);
            soundGrid.Children.Add(volStack);

            var suppressCheck = new CheckBox
            {
                Content = "Mute Switch Sound",
                IsChecked = combo.SuppressSwitchSound,
                Margin = new Thickness(0, 0, 16, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTip.SetTip(suppressCheck, "Suppresses standard mechanical switch sound when this combination triggers.");
            suppressCheck.IsCheckedChanged += (_, _) =>
            {
                combo.SuppressSwitchSound = suppressCheck.IsChecked == true;
                _controller.UpdateCustomCombo(combo);
            };
            Grid.SetColumn(suppressCheck, 3);
            soundGrid.Children.Add(suppressCheck);

            var testBtn = new Button
            {
                Content = "Test Sound",
                Padding = new Thickness(12, 4),
                VerticalAlignment = VerticalAlignment.Center
            };
            testBtn.Classes.Add("secondary");
            testBtn.Click += (_, _) => _controller.TestPlayCustomCombo(combo);
            Grid.SetColumn(testBtn, 4);
            soundGrid.Children.Add(testBtn);

            rootStack.Children.Add(soundGrid);
            card.Child = rootStack;
            CombosListPanel.Children.Add(card);
        }
    }

    private void StartRecordingCombo(CustomKeyCombo combo, TextBlock keysDisplay, Button recordBtn)
    {
        _recordingComboId = combo.Id;
        _recordedComboScanCodes.Clear();
        _activeRecordingKeysBlock = keysDisplay;

        recordBtn.Content = "Listening... (Press Keys & Click When Done)";
        recordBtn.Classes.Set("primary", true);
        recordBtn.Classes.Set("secondary", false);
        keysDisplay.Text = "Listening for keyboard input...";
    }

    private void FinishRecordingCombo(CustomKeyCombo combo, TextBlock keysDisplay, Button recordBtn)
    {
        _recordingComboId = null;
        _activeRecordingKeysBlock = null;

        recordBtn.Content = "Record Keys";
        recordBtn.Classes.Set("primary", false);
        recordBtn.Classes.Set("secondary", true);

        if (_recordedComboScanCodes.Count > 0)
        {
            combo.ScanCodes = _recordedComboScanCodes.OrderBy(c => c).ToList();
            _controller.UpdateCustomCombo(combo);
        }

        keysDisplay.Text = combo.FormatKeysDisplay();
    }

    private void OnAddNewComboClick(object? sender, RoutedEventArgs e)
    {
        var combo = new CustomKeyCombo
        {
            Name = $"Combo #{_controller.Settings.CustomCombos.Count + 1}",
            IsEnabled = true,
            Volume = 1.0f,
            SuppressSwitchSound = true
        };

        _controller.AddCustomCombo(combo);
        NavCombos.IsChecked = true;
    }
}

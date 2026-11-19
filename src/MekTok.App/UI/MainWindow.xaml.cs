using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using MekTok.App.Native;
using MekTok.App.Services;
using MekTok.Core.Audio;
using MekTok.Core.Config;
using MekTok.Core.Keyboard;
using MekTok.Core.Packs;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace MekTok.App.UI;

public partial class MainWindow : Window
{
    private sealed record SwitchOptionItem(string Id, string DisplayName, string AccentHex)
    {
        public override string ToString() => DisplayName;
    }

    private sealed record OutputDeviceItem(string Id, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    private readonly MekTokController _controller;
    private readonly Dictionary<KeyGroup, ToggleButton> _groupChips = new();

    private KeyDefinition? _selectedKey = Ansi104Layout.TryGetByScanCode(0x0039); // Default to Spacebar
    private KeyGroup _selectedGroup = KeyGroup.Spacebar;
    private bool _editingGroupMode;
    private bool _isRefreshing;
    private bool _isCapturingHotkey;

    public MainWindow(MekTokController controller)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _isRefreshing = true;
        InitializeComponent();

        VisualKeyboard.BindController(_controller);
        VisualKeyboard.KeyClicked += OnVisualKeyClicked;

        BuildGroupChips();
        PopulateInspectorSwitchOptions();

        _controller.StateChanged += (_, _) => Dispatcher.BeginInvoke(RefreshAllState);
        _controller.ProfilesChanged += (_, _) => Dispatcher.BeginInvoke(RefreshProfilesAndPacks);
        _controller.AudioDevicesChanged += (_, _) => Dispatcher.BeginInvoke(RefreshDevices);
        _controller.KeyboardsChanged += (_, _) => Dispatcher.BeginInvoke(RefreshKeyboards);
        _controller.KeyActivityObserved += OnKeyActivityObserved;

        SourceInitialized += (_, _) => _controller.ThemeService.ApplyWindowChrome(this);
        IsVisibleChanged += (_, _) =>
        {
            _controller.NotifyConfiguratorVisibility(IsVisible);
            if (IsVisible)
            {
                RefreshAllState();
                RefreshRunningProcesses();
                RefreshKeyboards();
                RefreshDevices();
            }
        };

        RefreshAllState();
        RefreshDevices();
        RefreshKeyboards();
        RefreshRunningProcesses();
    }

    public bool AllowRealClose { get; set; }

    protected override void OnClosing(CancelEventArgs e)
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

    private void BuildGroupChips()
    {
        GroupChipsPanel.Children.Clear();
        _groupChips.Clear();

        Style chipStyle = (Style)FindResource("FluentChipToggle");
        foreach (KeyGroup group in Enum.GetValues<KeyGroup>())
        {
            var btn = new ToggleButton
            {
                Content = group.ToDisplayName(),
                Style = chipStyle,
                Margin = new Thickness(0, 0, 6, 4)
            };

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

    private void OnNavChanged(object sender, RoutedEventArgs e)
    {
        if (ViewConfigurator is null || ViewAcoustics is null || ViewSettings is null) return;

        ViewConfigurator.Visibility = NavConfigurator.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ViewAcoustics.Visibility = NavAcoustics.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ViewSettings.Visibility = NavSettings.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnHideToTrayClick(object sender, RoutedEventArgs e) => Hide();

    private void OnKeyActivityObserved(int scanCode, bool isKeyUp)
    {
        VisualKeyboard.SetKeyPressed(scanCode, !isKeyUp);
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

            ConfigProfileCombo.ItemsSource = profiles;
            ConfigProfileCombo.SelectedValue = s.ActiveProfileId;

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

            HotkeyEnabledCheck.IsChecked = s.MuteHotkey.Enabled;
            if (!_isCapturingHotkey)
            {
                HotkeyCaptureBox.Text = s.MuteHotkey.Enabled
                    ? s.MuteHotkey.ToDisplayString()
                    : "Disabled";
            }
            HotkeyStatusText.Text = _controller.LastHotkeyError ?? string.Empty;

            MuteFullscreenCheck.IsChecked = s.MuteInFullscreen;
            StartWithWindowsCheck.IsChecked = s.StartWithWindows;
            ThemeCombo.SelectedValue = s.Theme.ToString();
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void RefreshProfilesAndPacks()
    {
        RefreshAllState();
    }

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
        InspectorSwitchCombo.SelectedValue = ov?.BuiltInSwitchOverrideId ?? string.Empty;

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

    private void OnInspectorControlChanged(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing) return;

        SoundOverride ov = GetEditableOverrideCopy();
        ov.IsMuted = InspectorMuteCheck.IsChecked == true;
        ov.BuiltInSwitchOverrideId = InspectorSwitchCombo.SelectedValue as string;
        SaveInspectorOverride(ov);
    }

    private void OnInspectorSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
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

    private void OnBrowsePressAudioClick(object sender, RoutedEventArgs e) => BrowseCustomAudio(isRelease: false);
    private void OnBrowseReleaseAudioClick(object sender, RoutedEventArgs e) => BrowseCustomAudio(isRelease: true);

    private void BrowseCustomAudio(bool isRelease)
    {
        var dialog = new OpenFileDialog
        {
            Title = isRelease ? "Select Custom Release Audio" : "Select Custom Key Press Audio",
            Filter = "Audio Files (*.wav;*.ogg;*.mp3)|*.wav;*.ogg;*.mp3|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) != true) return;

        try
        {
            string storedPath = _controller.CopyCustomAudioToStore(dialog.FileName);
            SoundOverride ov = GetEditableOverrideCopy();
            if (isRelease) ov.CustomReleaseAudioPath = storedPath;
            else ov.CustomPressAudioPath = storedPath;
            SaveInspectorOverride(ov);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Could not load selected audio file: {ex.Message}",
                "MekTok Audio Import",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OnClearPressAudioClick(object sender, RoutedEventArgs e)
    {
        SoundOverride ov = GetEditableOverrideCopy();
        ov.CustomPressAudioPath = null;
        SaveInspectorOverride(ov);
    }

    private void OnClearReleaseAudioClick(object sender, RoutedEventArgs e)
    {
        SoundOverride ov = GetEditableOverrideCopy();
        ov.CustomReleaseAudioPath = null;
        SaveInspectorOverride(ov);
    }

    private void OnAuditionSelectedClick(object sender, RoutedEventArgs e)
    {
        int scan = _editingGroupMode
            ? (Ansi104Layout.GetKeysByGroup(_selectedGroup).FirstOrDefault()?.ScanCode ?? 0x001E)
            : (_selectedKey?.ScanCode ?? 0x0039);
        _controller.AuditionKey(scan);
    }

    private void OnClearSelectedOverrideClick(object sender, RoutedEventArgs e)
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

    private void OnResetProfileOverridesClick(object sender, RoutedEventArgs e)
    {
        MessageBoxResult confirm = MessageBox.Show(
            this,
            "Clear all key and group overrides for the active profile?",
            "Reset Profile Overrides",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm == MessageBoxResult.Yes)
        {
            _controller.ResetActiveProfileCustomizations();
        }
    }

    private void OnMasterToggleChanged(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing) return;
        _controller.SetEnabled(SidebarMasterToggle.IsChecked == true);
    }

    private void OnConfigProfileComboChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshing) return;
        if (ConfigProfileCombo.SelectedValue is string id && !string.IsNullOrWhiteSpace(id))
        {
            _controller.SelectProfile(id);
        }
    }

    private void OnTestCurrentProfileClick(object sender, RoutedEventArgs e)
    {
        _controller.AuditionProfile(_controller.Settings.ActiveProfileId);
    }

    private void OnHideNumpadChanged(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing) return;
        _controller.SetHideNumpad(HideNumpadCheck.IsChecked == true);
    }

    private void RefreshBuiltInSwitchCards()
    {
        BuiltInSwitchGrid.Children.Clear();
        Style secondaryBtnStyle = (Style)FindResource("FluentSecondaryButton");

        foreach (SwitchProfileDefinition def in BuiltInProfiles.All)
        {
            bool isSelected = string.Equals(
                _controller.Settings.ActiveProfileId,
                def.Id,
                StringComparison.OrdinalIgnoreCase);

            var card = new Border
            {
                Margin = new Thickness(0, 0, 10, 10),
                Padding = new Thickness(14),
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(isSelected ? 1.8 : 1.0),
                Cursor = Cursors.Hand
            };
            card.SetResourceReference(
                Border.BackgroundProperty,
                isSelected ? "AccentSoftBrush" : "SurfaceCardBrush");
            card.SetResourceReference(
                Border.BorderBrushProperty,
                isSelected ? "AccentBrush" : "BorderSubtleBrush");

            var stack = new StackPanel();
            var topGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };

            var titleStack = new StackPanel { Orientation = Orientation.Horizontal };
            var accentColor = (Color)ColorConverter.ConvertFromString(def.AccentHex);
            titleStack.Children.Add(new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = new SolidColorBrush(accentColor),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            var nameBlock = new TextBlock
            {
                Text = def.Name,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold
            };
            nameBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            titleStack.Children.Add(nameBlock);
            topGrid.Children.Add(titleStack);

            var testBtn = new Button
            {
                Content = "Test",
                Style = secondaryBtnStyle,
                HorizontalAlignment = HorizontalAlignment.Right,
                Padding = new Thickness(10, 4, 10, 4)
            };
            string capturedId = def.Id;
            testBtn.Click += (_, ev) =>
            {
                ev.Handled = true;
                _controller.AuditionProfile(capturedId);
            };
            topGrid.Children.Add(testBtn);

            var taglineBlock = new TextBlock
            {
                Text = def.Tagline,
                FontSize = 11.5,
                FontWeight = FontWeights.Medium,
                Margin = new Thickness(0, 0, 0, 4)
            };
            taglineBlock.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");

            var descBlock = new TextBlock
            {
                Text = def.Description,
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap
            };
            descBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");

            stack.Children.Add(topGrid);
            stack.Children.Add(taglineBlock);
            stack.Children.Add(descBlock);
            card.Child = stack;

            card.MouseLeftButtonDown += (_, _) =>
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

    private void OnAcousticSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isRefreshing) return;

        if (sender == AcMasterVolSlider)
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

        bool needsSynthRebuild = sender == AcThockSlider || sender == AcBrightSlider;
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

    private void OnAcousticToggleChanged(object sender, RoutedEventArgs e)
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
            empty.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
            ImportedPacksPanel.Children.Add(empty);
            return;
        }

        Style secondaryStyle = (Style)FindResource("FluentSecondaryButton");
        foreach (ImportedSoundPack pack in packs)
        {
            bool active = string.Equals(_controller.Settings.ActiveProfileId, pack.Id, StringComparison.OrdinalIgnoreCase);

            var row = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 4, 0, 0)
            };
            row.SetResourceReference(Border.BackgroundProperty, active ? "AccentSoftBrush" : "SurfaceSubtleBrush");
            row.SetResourceReference(Border.BorderBrushProperty, active ? "AccentBrush" : "BorderSubtleBrush");

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var title = new TextBlock
            {
                Text = pack.Name,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold
            };
            title.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            var sub = new TextBlock
            {
                Text = $"{pack.PressClips.Count} mapped keys" + (pack.ReleaseClips.Count > 0 ? $" • {pack.ReleaseClips.Count} release sounds" : string.Empty),
                FontSize = 11.5
            };
            sub.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            info.Children.Add(title);
            info.Children.Add(sub);
            grid.Children.Add(info);

            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            Grid.SetColumn(actions, 1);

            ImportedSoundPack captured = pack;
            var useBtn = new Button
            {
                Content = active ? "Active" : "Use Pack",
                Style = secondaryStyle,
                IsEnabled = !active,
                Margin = new Thickness(0, 0, 6, 0)
            };
            useBtn.Click += (_, _) => _controller.SelectProfile(captured.Id);

            var delBtn = new Button
            {
                Content = "Delete",
                Style = secondaryStyle
            };
            delBtn.Click += (_, _) => _controller.DeleteImportedPack(captured);

            actions.Children.Add(useBtn);
            actions.Children.Add(delBtn);
            grid.Children.Add(actions);

            row.Child = grid;
            ImportedPacksPanel.Children.Add(row);
        }
    }

    private async void OnImportPackFolderClick(object sender, RoutedEventArgs e)
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "Select a Mechvibes sound pack folder containing config.json",
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() != WinForms.DialogResult.OK) return;

        try
        {
            ImportedSoundPack pack = await _controller.ImportMechvibesFolderAsync(dialog.SelectedPath);
            MessageBox.Show(
                this,
                $"Imported '{pack.Name}' ({pack.PressClips.Count} keys) and set as active profile.",
                "Mechvibes Pack Imported",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Failed to import pack folder: {ex.Message}",
                "Pack Import Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async void OnImportPackZipClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select a Mechvibes Sound Pack (.zip)",
            Filter = "ZIP Archives (*.zip)|*.zip"
        };

        if (dialog.ShowDialog(this) != true) return;

        try
        {
            ImportedSoundPack pack = await _controller.ImportMechvibesZipAsync(dialog.FileName);
            MessageBox.Show(
                this,
                $"Imported '{pack.Name}' ({pack.PressClips.Count} keys) and set as active profile.",
                "Mechvibes Pack Imported",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Failed to import pack archive: {ex.Message}",
                "Pack Import Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
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
            SettingsDeviceCombo.SelectedValue = items.Any(i => string.Equals(i.Id, current, StringComparison.OrdinalIgnoreCase))
                ? current
                : string.Empty;
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void OnSettingsDeviceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshing) return;
        if (SettingsDeviceCombo.SelectedValue is string id)
        {
            _controller.SelectOutputDevice(string.IsNullOrWhiteSpace(id) ? null : id);
        }
    }

    private void OnRefreshDevicesClick(object sender, RoutedEventArgs e) => RefreshDevices();

    private void OnHotkeyEnabledChanged(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing) return;
        HotkeyConfig updated = new()
        {
            Enabled = HotkeyEnabledCheck.IsChecked == true,
            Modifiers = _controller.Settings.MuteHotkey.Modifiers,
            VirtualKey = _controller.Settings.MuteHotkey.VirtualKey
        };
        _controller.SetMuteHotkey(updated);
    }

    private void OnHotkeyCaptureGotFocus(object sender, RoutedEventArgs e)
    {
        _isCapturingHotkey = true;
        HotkeyCaptureBox.Text = "Press shortcut combination...";
    }

    private void OnHotkeyCaptureLostFocus(object sender, RoutedEventArgs e)
    {
        _isCapturingHotkey = false;
        HotkeyCaptureBox.Text = _controller.Settings.MuteHotkey.Enabled
            ? _controller.Settings.MuteHotkey.ToDisplayString()
            : "Disabled";
    }

    private void OnHotkeyCapturePreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return;
        }

        ModifierKeys mods = Keyboard.Modifiers;
        if (mods == ModifierKeys.None)
        {
            HotkeyStatusText.Text = "Include at least one modifier key (Ctrl, Alt, or Shift).";
            return;
        }

        HotkeyModifiers hkMods = HotkeyModifiers.None;
        if (mods.HasFlag(ModifierKeys.Control)) hkMods |= HotkeyModifiers.Control;
        if (mods.HasFlag(ModifierKeys.Alt)) hkMods |= HotkeyModifiers.Alt;
        if (mods.HasFlag(ModifierKeys.Shift)) hkMods |= HotkeyModifiers.Shift;
        if (mods.HasFlag(ModifierKeys.Windows)) hkMods |= HotkeyModifiers.Win;

        int vk = KeyInterop.VirtualKeyFromKey(key);
        var config = new HotkeyConfig
        {
            Enabled = true,
            Modifiers = hkMods,
            VirtualKey = vk
        };

        _isCapturingHotkey = false;
        (bool ok, string? err) = _controller.SetMuteHotkey(config);
        HotkeyCaptureBox.Text = _controller.Settings.MuteHotkey.ToDisplayString();
        HotkeyStatusText.Text = ok ? "Shortcut registered." : (err ?? "Could not register shortcut.");
        Keyboard.ClearFocus();
    }

    private void OnResetHotkeyClick(object sender, RoutedEventArgs e)
    {
        _controller.SetMuteHotkey(new HotkeyConfig());
    }

    private void OnMuteFullscreenChanged(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing) return;
        _controller.SetMuteInFullscreen(MuteFullscreenCheck.IsChecked == true);
    }

    private void RefreshRunningProcesses()
    {
        RunningAppsCombo.ItemsSource = ActiveWindowMonitor.GetRunningUserProcesses();
        if (RunningAppsCombo.Items.Count > 0 && RunningAppsCombo.SelectedIndex < 0)
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
            none.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
            ExcludedAppsPanel.Children.Add(none);
            return;
        }

        Style secondaryStyle = (Style)FindResource("FluentSecondaryButton");
        foreach (string proc in excluded)
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var nameBlock = new TextBlock
            {
                Text = proc + ".exe",
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center
            };
            nameBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            row.Children.Add(nameBlock);

            string captured = proc;
            var removeBtn = new Button
            {
                Content = "Remove",
                Style = secondaryStyle,
                Padding = new Thickness(10, 3, 10, 3)
            };
            removeBtn.Click += (_, _) => _controller.RemoveExcludedProcess(captured);
            Grid.SetColumn(removeBtn, 1);
            row.Children.Add(removeBtn);

            ExcludedAppsPanel.Children.Add(row);
        }
    }

    private void OnRefreshRunningAppsClick(object sender, RoutedEventArgs e) => RefreshRunningProcesses();

    private void OnAddSelectedProcessClick(object sender, RoutedEventArgs e)
    {
        if (RunningAppsCombo.SelectedItem is string proc)
        {
            _controller.AddExcludedProcess(proc);
        }
    }

    private void OnAddManualProcessClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(ManualProcessBox.Text))
        {
            _controller.AddExcludedProcess(ManualProcessBox.Text);
            ManualProcessBox.Clear();
        }
    }

    private void RefreshKeyboards()
    {
        KeyboardsListPanel.Children.Clear();
        IReadOnlyList<KeyboardDeviceInfo> keyboards = _controller.GetConnectedKeyboards();

        if (keyboards.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = "No physical HID keyboards enumerated.",
                FontSize = 12
            };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
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
                Margin = new Thickness(0, 4, 0, 4),
                FontSize = 12.5
            };
            cb.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
            string capturedId = kb.DeviceId;
            cb.Checked += (_, _) => _controller.SetKeyboardEnabled(capturedId, true);
            cb.Unchecked += (_, _) => _controller.SetKeyboardEnabled(capturedId, false);
            KeyboardsListPanel.Children.Add(cb);
        }
    }

    private void OnRefreshKeyboardsClick(object sender, RoutedEventArgs e) => RefreshKeyboards();

    private void OnThemeComboChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshing) return;
        if (ThemeCombo.SelectedValue is string raw &&
            Enum.TryParse<ThemePreference>(raw, out ThemePreference pref))
        {
            _controller.SetTheme(pref);
            RefreshKeyboards();
        }
    }

    private void OnStartupToggleChanged(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing) return;
        _controller.SetStartWithWindows(StartWithWindowsCheck.IsChecked == true);
    }

    private void OnOpenDataFolderClick(object sender, RoutedEventArgs e)
    {
        _controller.OpenDataDirectoryInExplorer();
    }
}

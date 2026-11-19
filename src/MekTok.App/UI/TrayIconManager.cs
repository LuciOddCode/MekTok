using MekTok.Core.Services;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace MekTok.App.UI;

/// <summary>
/// Manages the Windows system tray NotifyIcon, dynamic active/muted icon state, and right-click menu.
/// </summary>
public sealed class TrayIconManager : IDisposable
{
    private readonly MekTokController _controller;
    private readonly WinForms.NotifyIcon _notifyIcon;
    private readonly WinForms.ContextMenuStrip _contextMenu;
    private readonly WinForms.ToolStripMenuItem _toggleItem;
    private readonly WinForms.ToolStripMenuItem _profilesMenu;
    private readonly WinForms.ToolStripMenuItem _startupItem;

    private Drawing.Icon _activeIcon;
    private Drawing.Icon _mutedIcon;
    private bool _disposed;

    public TrayIconManager(MekTokController controller)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));

        _activeIcon = IconFactory.CreateTrayIcon(active: true);
        _mutedIcon = IconFactory.CreateTrayIcon(active: false);

        _toggleItem = new WinForms.ToolStripMenuItem();
        _toggleItem.Click += (_, _) => _controller.ToggleEnabled();

        _profilesMenu = new WinForms.ToolStripMenuItem("Switch Profile");

        var configureItem = new WinForms.ToolStripMenuItem("Configure Keys && Settings...");
        configureItem.Font = new Drawing.Font(configureItem.Font, Drawing.FontStyle.Bold);
        configureItem.Click += (_, _) => OpenConfiguratorRequested?.Invoke(this, EventArgs.Empty);

        _startupItem = new WinForms.ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = false
        };
        _startupItem.Click += (_, _) => _controller.SetStartWithWindows(!_controller.Settings.StartWithWindows);

        var exitItem = new WinForms.ToolStripMenuItem("Exit MekTok");
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        _contextMenu = new WinForms.ContextMenuStrip();
        _contextMenu.Items.Add(_toggleItem);
        _contextMenu.Items.Add(_profilesMenu);
        _contextMenu.Items.Add(new WinForms.ToolStripSeparator());
        _contextMenu.Items.Add(configureItem);
        _contextMenu.Items.Add(_startupItem);
        _contextMenu.Items.Add(new WinForms.ToolStripSeparator());
        _contextMenu.Items.Add(exitItem);
        _contextMenu.Opening += (_, _) => RefreshMenu();

        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = _activeIcon,
            ContextMenuStrip = _contextMenu,
            Visible = true
        };

        _notifyIcon.MouseUp += OnTrayMouseUp;
        _notifyIcon.DoubleClick += (_, _) => OpenConfiguratorRequested?.Invoke(this, EventArgs.Empty);

        _controller.StateChanged += (_, _) =>
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(RefreshVisualState);
        };
        _controller.ProfilesChanged += (_, _) =>
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(RefreshMenu);
        };

        RefreshVisualState();
        RefreshMenu();
    }

    public event EventHandler? ToggleFlyoutRequested;
    public event EventHandler? OpenConfiguratorRequested;
    public event EventHandler? ExitRequested;

    public void RefreshVisualState()
    {
        bool active = _controller.IsEffectivelyActive;
        _notifyIcon.Icon = active ? _activeIcon : _mutedIcon;

        string profileName = _controller.GetAllProfiles()
            .FirstOrDefault(p => string.Equals(p.Id, _controller.Settings.ActiveProfileId, StringComparison.OrdinalIgnoreCase))
            ?.DisplayName ?? "Creamy Thock (Holy Panda)";

        string status = !_controller.Settings.IsEnabled
            ? "Muted"
            : _controller.IsAutoPaused
                ? $"Paused ({_controller.AutoPauseReason})"
                : "Active";

        string rawTip = $"MekTok • {status} • {profileName}";
        _notifyIcon.Text = rawTip.Length > 62 ? rawTip[..62] : rawTip;
    }

    private void RefreshMenu()
    {
        string hotkeyHint = _controller.Settings.MuteHotkey.Enabled
            ? $" ({_controller.Settings.MuteHotkey.ToDisplayString()})"
            : string.Empty;

        _toggleItem.Text = _controller.Settings.IsEnabled
            ? $"Mute Sounds{hotkeyHint}"
            : $"Enable Sounds{hotkeyHint}";

        _startupItem.Checked = _controller.Settings.StartWithWindows;

        _profilesMenu.DropDownItems.Clear();
        foreach (ProfileChoiceItem profile in _controller.GetAllProfiles())
        {
            var item = new WinForms.ToolStripMenuItem(profile.DisplayName)
            {
                Checked = string.Equals(profile.Id, _controller.Settings.ActiveProfileId, StringComparison.OrdinalIgnoreCase)
            };
            string capturedId = profile.Id;
            item.Click += (_, _) => _controller.SelectProfile(capturedId);
            _profilesMenu.DropDownItems.Add(item);
        }
    }

    private void OnTrayMouseUp(object? sender, WinForms.MouseEventArgs e)
    {
        if (e.Button == WinForms.MouseButtons.Left)
        {
            ToggleFlyoutRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _contextMenu.Dispose();
        _activeIcon.Dispose();
        _mutedIcon.Dispose();
    }
}

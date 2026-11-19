using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using MekTok.Core.Keyboard;
using MekTok.Core.Services;

namespace MekTok.Desktop.UI;

/// <summary>
/// Interactive ANSI 104-key / 87-key TKL visual keyboard supporting selection,
/// override/mute status badges, and live key-press highlighting.
/// </summary>
public partial class VisualKeyboardControl : UserControl
{
    private const double UnitPx = 46.0;
    private const double GapPx = 4.0;
    private const double FunctionRowExtraGapU = 0.25;

    private sealed class KeyVisualSlot
    {
        public required KeyDefinition Key { get; init; }
        public required Border KeyCapBorder { get; init; }
        public required Ellipse OverrideDot { get; init; }
        public bool IsPressed { get; set; }
    }

    private readonly Dictionary<int, KeyVisualSlot> _slots = new();
    private MekTokController? _controller;
    private bool _hideNumpad;
    private int? _selectedScanCode;
    private KeyGroup? _selectedGroup;

    public VisualKeyboardControl()
    {
        InitializeComponent();
        RebuildBoard();
    }

    public event Action<KeyDefinition>? KeyClicked;

    public void BindController(MekTokController controller)
    {
        _controller = controller;
        _hideNumpad = controller.Settings.HideNumpadInConfigurator;
        RebuildBoard();
        RefreshStyles();
    }

    public void SetHideNumpad(bool hideNumpad)
    {
        if (_hideNumpad == hideNumpad) return;
        _hideNumpad = hideNumpad;
        RebuildBoard();
        RefreshStyles();
    }

    public void SetSelection(int? scanCode, KeyGroup? group)
    {
        _selectedScanCode = scanCode;
        _selectedGroup = group;
        RefreshStyles();
    }

    public void SetKeyPressed(int scanCode, bool isPressed)
    {
        if (_slots.TryGetValue(scanCode, out KeyVisualSlot? slot))
        {
            slot.IsPressed = isPressed;
            ApplySlotStyle(slot);
        }
    }

    public void RefreshStyles()
    {
        foreach (KeyVisualSlot slot in _slots.Values)
        {
            ApplySlotStyle(slot);
        }
    }

    private void RebuildBoard()
    {
        KeyCanvas.Children.Clear();
        _slots.Clear();

        double totalWidthU = _hideNumpad ? Ansi104Layout.TklWidthU : Ansi104Layout.FullWidthU;
        KeyCanvas.Width = totalWidthU * UnitPx;
        KeyCanvas.Height = (6.0 + FunctionRowExtraGapU) * UnitPx;

        foreach (KeyDefinition key in Ansi104Layout.GetKeys(includeNumpad: !_hideNumpad))
        {
            double x = key.Col * UnitPx;
            double rowU = key.Row == 0 ? 0.0 : key.Row + FunctionRowExtraGapU;
            double y = rowU * UnitPx;
            double w = (key.WidthU * UnitPx) - GapPx;
            double h = (key.HeightU * UnitPx) - GapPx;

            var grid = new Grid();

            var labelBlock = new TextBlock
            {
                Text = key.Label,
                FontSize = key.Label.Length > 3 ? 10.5 : (key.Label.Length > 1 ? 11.5 : 13.0),
                FontWeight = FontWeight.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            labelBlock.Classes.Add("keycap-label");

            var dot = new Ellipse
            {
                Width = 6,
                Height = 6,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 4, 4, 0),
                IsVisible = false
            };

            grid.Children.Add(labelBlock);
            grid.Children.Add(dot);

            var border = new Border
            {
                Width = w,
                Height = h,
                Child = grid
            };
            border.Classes.Add("keycap");
            ToolTip.SetTip(border, $"{key.KeyId} ({key.Group.ToDisplayName()})");

            KeyDefinition captured = key;
            border.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                KeyClicked?.Invoke(captured);
            };

            Canvas.SetLeft(border, x);
            Canvas.SetTop(border, y);
            KeyCanvas.Children.Add(border);

            _slots[key.ScanCode] = new KeyVisualSlot
            {
                Key = key,
                KeyCapBorder = border,
                OverrideDot = dot
            };
        }
    }

    private void ApplySlotStyle(KeyVisualSlot slot)
    {
        KeyDefinition key = slot.Key;
        bool isSelectedKey = _selectedScanCode.HasValue && _selectedScanCode.Value == key.ScanCode;
        bool isSelectedGroup = !_selectedScanCode.HasValue && _selectedGroup.HasValue && _selectedGroup.Value == key.Group;

        bool hasKeyOv = _controller?.HasKeyOverride(key.ScanCode) == true;
        bool hasGroupOv = _controller?.GetGroupOverride(key.Group) is { IsEmpty: false };
        bool isMuted = _controller?.IsKeyEffectivelyMuted(key.ScanCode, key.Group) == true;

        slot.KeyCapBorder.Classes.Set("pressed", slot.IsPressed);
        slot.KeyCapBorder.Classes.Set("selected", !slot.IsPressed && (isSelectedKey || isSelectedGroup));
        slot.KeyCapBorder.Classes.Set("muted", isMuted);

        slot.OverrideDot.Classes.Set("dot-muted", isMuted);
        slot.OverrideDot.Classes.Set("dot-key", !isMuted && hasKeyOv);
        slot.OverrideDot.Classes.Set("dot-group", !isMuted && !hasKeyOv && hasGroupOv);
        slot.OverrideDot.IsVisible = isMuted || hasKeyOv || hasGroupOv;
    }
}


using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace MekTok.Desktop.UI;

internal static class DialogWindow
{
    public static async Task ShowInfoAsync(Window owner, string title, string message)
    {
        var dialog = BuildWindow(title, message, isConfirm: false, out TaskCompletionSource<bool> tcs);
        await dialog.ShowDialog(owner);
        await tcs.Task;
    }

    public static async Task<bool> ShowConfirmAsync(Window owner, string title, string message)
    {
        var dialog = BuildWindow(title, message, isConfirm: true, out TaskCompletionSource<bool> tcs);
        await dialog.ShowDialog(owner);
        return await tcs.Task;
    }

    private static Window BuildWindow(string title, string message, bool isConfirm, out TaskCompletionSource<bool> tcs)
    {
        var completion = new TaskCompletionSource<bool>();
        tcs = completion;

        var dialog = new Window
        {
            Title = title,
            Width = 430,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        if (Application.Current?.TryGetResource("WindowBackgroundBrush", Application.Current.ActualThemeVariant, out object? bg) == true &&
            bg is IBrush bgBrush)
        {
            dialog.Background = bgBrush;
        }

        var root = new StackPanel { Margin = new Thickness(20), Spacing = 16 };
        root.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 13.5,
            TextWrapping = TextWrapping.Wrap
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };

        if (isConfirm)
        {
            var cancelBtn = new Button { Content = "Cancel" };
            cancelBtn.Classes.Add("secondary");
            cancelBtn.Click += (_, _) =>
            {
                completion.TrySetResult(false);
                dialog.Close();
            };

            var yesBtn = new Button { Content = "Confirm" };
            yesBtn.Classes.Add("primary");
            yesBtn.Click += (_, _) =>
            {
                completion.TrySetResult(true);
                dialog.Close();
            };

            buttons.Children.Add(cancelBtn);
            buttons.Children.Add(yesBtn);
        }
        else
        {
            var okBtn = new Button { Content = "OK" };
            okBtn.Classes.Add("primary");
            okBtn.Click += (_, _) =>
            {
                completion.TrySetResult(true);
                dialog.Close();
            };
            buttons.Children.Add(okBtn);
        }

        dialog.Closed += (_, _) => completion.TrySetResult(false);
        root.Children.Add(buttons);
        dialog.Content = root;
        return dialog;
    }
}


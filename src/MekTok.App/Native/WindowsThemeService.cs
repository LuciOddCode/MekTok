using System.Windows;
using System.Windows.Interop;
using MekTok.Core.Config;
using Microsoft.Win32;

namespace MekTok.App.Native;

/// <summary>
/// Applies Windows 11 Fluent Dark/Light theme dictionaries and DWM rounded corners / immersive title bars.
/// </summary>
public sealed class WindowsThemeService : IDisposable
{
    private ThemePreference _preference = ThemePreference.System;
    private bool _isDark = true;
    private bool _disposed;

    public WindowsThemeService()
    {
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public bool IsDarkTheme => _isDark;

    public event EventHandler<bool>? ThemeChanged;

    public void ApplyTheme(ThemePreference preference)
    {
        _preference = preference;
        bool useDark = preference switch
        {
            ThemePreference.Dark => true,
            ThemePreference.Light => false,
            _ => DetectWindowsDarkMode()
        };

        _isDark = useDark;
        string uriStr = useDark
            ? "pack://application:,,,/Themes/FluentDark.xaml"
            : "pack://application:,,,/Themes/FluentLight.xaml";

        Application? app = Application.Current;
        if (app is not null)
        {
            var dictionaries = app.Resources.MergedDictionaries;
            var newTheme = new ResourceDictionary { Source = new Uri(uriStr, UriKind.Absolute) };

            if (dictionaries.Count > 0)
            {
                dictionaries[0] = newTheme;
            }
            else
            {
                dictionaries.Insert(0, newTheme);
            }

            foreach (Window win in app.Windows)
            {
                ApplyWindowChrome(win, useDark);
            }
        }

        ThemeChanged?.Invoke(this, useDark);
    }

    public void ApplyWindowChrome(Window window) => ApplyWindowChrome(window, _isDark);

    public static void ApplyWindowChrome(Window window, bool isDark)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        int darkAttr = isDark ? 1 : 0;
        NativeMethods.DwmSetWindowAttribute(
            hwnd,
            NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE,
            ref darkAttr,
            sizeof(int));

        int roundAttr = NativeMethods.DWMWCP_ROUND;
        NativeMethods.DwmSetWindowAttribute(
            hwnd,
            NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE,
            ref roundAttr,
            sizeof(int));
    }

    private static bool DetectWindowsDarkMode()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int light)
            {
                return light == 0;
            }
        }
        catch
        {
            // Default to dark theme when registry key is unavailable.
        }
        return true;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_preference == ThemePreference.System &&
            e.Category == UserPreferenceCategory.General)
        {
            Application.Current?.Dispatcher.BeginInvoke(() => ApplyTheme(ThemePreference.System));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }
}


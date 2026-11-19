using MekTok.Core.Platform;
using Microsoft.Win32;

namespace MekTok.App.Native;

public sealed class WindowsStartupManager : IStartupManager
{
    public bool IsEnabled() => StartupManager.IsEnabled();
    public void SetEnabled(bool enabled) => StartupManager.SetEnabled(enabled);
}

/// <summary>
/// Manages automatic Windows login startup under HKCU\...\Run using Environment.ProcessPath
/// (safe for single-file self-contained executables).
/// </summary>
public static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MekTok";

    public static bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string val && !string.IsNullOrWhiteSpace(val);
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

        if (key is null) return;

        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        string? exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath)) return;

        key.SetValue(ValueName, $"\"{exePath}\" --minimized", RegistryValueKind.String);
    }
}


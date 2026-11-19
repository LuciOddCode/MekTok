using MekTok.Core.Platform;

namespace MekTok.Platform.Desktop.Startup;

/// <summary>
/// Manages XDG desktop autostart entry (<c>~/.config/autostart/mektok.desktop</c>)
/// supported natively by Linux Mint Cinnamon, GNOME, KDE, and XFCE.
/// </summary>
public sealed class LinuxStartupManager : IStartupManager
{
    private const string DesktopFileName = "mektok.desktop";
    private readonly string _autostartDirectory;
    private readonly Func<string?> _executablePathProvider;

    public LinuxStartupManager(
        string? autostartDirectory = null,
        Func<string?>? executablePathProvider = null)
    {
        _autostartDirectory = string.IsNullOrWhiteSpace(autostartDirectory)
            ? GetDefaultAutostartDirectory()
            : Path.GetFullPath(autostartDirectory);
        _executablePathProvider = executablePathProvider ?? (() => Environment.ProcessPath);
    }

    public string DesktopFilePath => Path.Combine(_autostartDirectory, DesktopFileName);

    public bool IsEnabled() => File.Exists(DesktopFilePath);

    public void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            if (File.Exists(DesktopFilePath))
            {
                File.Delete(DesktopFilePath);
            }
            return;
        }

        string? exePath = _executablePathProvider();
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return;
        }

        Directory.CreateDirectory(_autostartDirectory);
        string escapedPath = exePath.Replace("\"", "\\\"");
        string content =
            $"""
            [Desktop Entry]
            Type=Application
            Version=1.0
            Name=MekTok
            Comment=Mechanical keyboard sound simulator
            Exec="{escapedPath}" --minimized
            Icon=mektok
            Terminal=false
            Categories=AudioVideo;Utility;
            X-GNOME-Autostart-enabled=true
            """;

        File.WriteAllText(DesktopFilePath, content + Environment.NewLine);
    }

    private static string GetDefaultAutostartDirectory()
    {
        string? xdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrWhiteSpace(xdgConfig))
        {
            return Path.Combine(xdgConfig, "autostart");
        }

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".config", "autostart");
    }
}


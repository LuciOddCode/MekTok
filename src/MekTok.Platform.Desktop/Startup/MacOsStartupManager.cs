using System.Security;
using MekTok.Core.Platform;

namespace MekTok.Platform.Desktop.Startup;

/// <summary>
/// Manages macOS login launch agent (<c>~/Library/LaunchAgents/com.mektok.app.plist</c>).
/// </summary>
public sealed class MacOsStartupManager : IStartupManager
{
    private const string PlistFileName = "com.mektok.app.plist";
    private readonly string _launchAgentsDirectory;
    private readonly Func<string?> _executablePathProvider;

    public MacOsStartupManager(
        string? launchAgentsDirectory = null,
        Func<string?>? executablePathProvider = null)
    {
        _launchAgentsDirectory = string.IsNullOrWhiteSpace(launchAgentsDirectory)
            ? GetDefaultLaunchAgentsDirectory()
            : Path.GetFullPath(launchAgentsDirectory);
        _executablePathProvider = executablePathProvider ?? (() => Environment.ProcessPath);
    }

    public string PlistFilePath => Path.Combine(_launchAgentsDirectory, PlistFileName);

    public bool IsEnabled() => File.Exists(PlistFilePath);

    public void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            if (File.Exists(PlistFilePath))
            {
                File.Delete(PlistFilePath);
            }
            return;
        }

        string? exePath = _executablePathProvider();
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return;
        }

        Directory.CreateDirectory(_launchAgentsDirectory);
        string escaped = SecurityElement.Escape(exePath) ?? exePath;
        string plist =
            $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
                <key>Label</key>
                <string>com.mektok.app</string>
                <key>ProgramArguments</key>
                <array>
                    <string>{escaped}</string>
                    <string>--minimized</string>
                </array>
                <key>RunAtLoad</key>
                <true/>
                <key>KeepAlive</key>
                <false/>
            </dict>
            </plist>
            """;

        File.WriteAllText(PlistFilePath, plist + Environment.NewLine);
    }

    private static string GetDefaultLaunchAgentsDirectory()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, "Library", "LaunchAgents");
    }
}


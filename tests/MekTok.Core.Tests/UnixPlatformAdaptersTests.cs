using MekTok.Platform.Desktop.Lifecycle;
using MekTok.Platform.Desktop.Startup;
using Xunit;

namespace MekTok.Core.Tests;

public class UnixPlatformAdaptersTests : IDisposable
{
    private readonly string _tempRoot;

    public UnixPlatformAdaptersTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "MekTok_UnixTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public void LinuxStartupManager_WritesAndDeletesDesktopFile()
    {
        string autostartDir = Path.Combine(_tempRoot, "autostart");
        var manager = new LinuxStartupManager(autostartDir, () => "/usr/bin/mektok");

        Assert.False(manager.IsEnabled());
        manager.SetEnabled(true);

        Assert.True(manager.IsEnabled());
        string content = File.ReadAllText(manager.DesktopFilePath);
        Assert.Contains("Exec=\"/usr/bin/mektok\" --minimized", content);
        Assert.Contains("X-GNOME-Autostart-enabled=true", content);

        manager.SetEnabled(false);
        Assert.False(manager.IsEnabled());
    }

    [Fact]
    public void MacOsStartupManager_WritesAndDeletesLaunchAgentPlist()
    {
        string launchAgentsDir = Path.Combine(_tempRoot, "LaunchAgents");
        var manager = new MacOsStartupManager(launchAgentsDir, () => "/Applications/MekTok.app/Contents/MacOS/MekTok");

        Assert.False(manager.IsEnabled());
        manager.SetEnabled(true);

        Assert.True(manager.IsEnabled());
        string plist = File.ReadAllText(manager.PlistFilePath);
        Assert.Contains("<string>com.mektok.app</string>", plist);
        Assert.Contains("<string>/Applications/MekTok.app/Contents/MacOS/MekTok</string>", plist);
        Assert.Contains("<string>--minimized</string>", plist);

        manager.SetEnabled(false);
        Assert.False(manager.IsEnabled());
    }

    [Fact]
    public async Task UnixSingleInstanceGuard_BlocksSecondInstanceAndSignalsPrimary()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string runtimeDir = Path.Combine(_tempRoot, "run");
        using var primary = new UnixSingleInstanceGuard(runtimeDir);
        Assert.True(primary.IsPrimary);

        var signaled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        primary.SecondInstanceSignaled += (_, _) => signaled.TrySetResult(true);

        using var secondary = new UnixSingleInstanceGuard(runtimeDir);
        Assert.False(secondary.IsPrimary);
        secondary.NotifyPrimaryInstance();

        Task completed = await Task.WhenAny(signaled.Task, Task.Delay(3000));
        Assert.Same(signaled.Task, completed);
        Assert.True(await signaled.Task);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }
}


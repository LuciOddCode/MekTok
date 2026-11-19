using Avalonia;
using MekTok.Core.Audio;
using MekTok.Core.Config;
using MekTok.Core.Keyboard;
using MekTok.Core.Platform;
using MekTok.Core.Services;
using MekTok.Platform.Desktop;
using MekTok.Platform.Desktop.Audio;
using MekTok.Platform.Desktop.Input;

namespace MekTok.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Any(a => string.Equals(a, "--self-test", StringComparison.OrdinalIgnoreCase)))
        {
            return RunSelfTest();
        }

        if (args.Any(a => string.Equals(a, "--latency-test", StringComparison.OrdinalIgnoreCase)))
        {
            return RunLatencyTest();
        }

        using ISingleInstanceGuard instanceGuard = DesktopPlatformFactory.CreateSingleInstanceGuard();
        if (!instanceGuard.IsPrimary)
        {
            instanceGuard.NotifyPrimaryInstance();
            return 0;
        }

        App.SingleInstanceGuard = instanceGuard;
        App.StartMinimized = args.Any(a => string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase));

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static int RunSelfTest()
    {
        if (!MekTokController.RunCoreSelfTest(out string coreSummary))
        {
            Console.Error.WriteLine(coreSummary);
            return 1;
        }

        var mappedScanCodes = new HashSet<int>(SharpHookKeyMap.AllMappings.Values);
        int unmappedCount = Ansi104Layout.AllKeys.Count(k => !mappedScanCodes.Contains(k.ScanCode));
        if (unmappedCount > 0)
        {
            Console.Error.WriteLine($"SharpHookKeyMap is missing {unmappedCount} key(s) from Ansi104Layout.");
            return 1;
        }

        using var audioEngine = new MiniAudioOutputEngine();
        audioEngine.Start(null);
        IReadOnlyList<AudioOutputDeviceInfo> devices = audioEngine.ListOutputDevices();

        Console.WriteLine(
            $"MekTok Desktop self-test passed: {coreSummary}, " +
            $"104/104 SharpHook keys mapped, {devices.Count} audio output(s), " +
            $"active='{audioEngine.ActiveDeviceName}' (hasOutput={audioEngine.HasActiveOutput}).");
        return 0;
    }

    private static int RunLatencyTest()
    {
        using var audioEngine = new MiniAudioOutputEngine();
        audioEngine.Start(null);

        if (!audioEngine.HasActiveOutput)
        {
            AudioLatencyDiagnostics diag = audioEngine.GetLatencyDiagnostics();
            Console.WriteLine(
                $"Latency test: no active audio output device available (backend={diag.BackendName}, status='{diag.DeviceName}').");
            return 0;
        }

        SynthesizedProfileBank bank = SwitchSynthesizer.SynthesizeBank(
            BuiltInProfiles.GetByIdOrDefault(BuiltInProfiles.CreamyThockId),
            new AcousticSettings());
        AudioClip clip = bank.GetClip(KeyGroup.Spacebar, isKeyUp: false, 0);

        audioEngine.Mixer.MasterVolume = 0.25f;
        audioEngine.Mixer.TriggerClip(clip);
        Thread.Sleep(500);
        audioEngine.Mixer.TriggerClip(clip);
        Thread.Sleep(1000);

        AudioLatencyDiagnostics stats = audioEngine.GetLatencyDiagnostics();
        double periodMs = stats.LastCallbackFrames * 1000.0 / AudioClip.StandardSampleRate;

        Console.WriteLine(
            $"MekTok Audio Latency Diagnostics:{Environment.NewLine}" +
            $"  Backend:                 {stats.BackendName}{Environment.NewLine}" +
            $"  Active Device:           {stats.DeviceName}{Environment.NewLine}" +
            $"  Callbacks Observed:      {stats.CallbackCount}{Environment.NewLine}" +
            $"  Period Size:             {stats.LastCallbackFrames} frames ({periodMs:F2} ms @ 48 kHz){Environment.NewLine}" +
            $"  Avg Callback Interval:   {stats.AverageCallbackIntervalMs:F2} ms{Environment.NewLine}" +
            $"  Estimated Buffer Latency:{stats.EstimatedLatencyMs?.ToString("F2") ?? "n/a"} ms");

        return stats.CallbackCount > 0 ? 0 : 1;
    }
}


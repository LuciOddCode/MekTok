# MekTok

MekTok is a lightweight, low-latency mechanical keyboard sound simulator for **Windows**, **Linux (Linux Mint / X11)**, and **macOS**. It plays realistic synthesized or imported mechanical switch sounds through your speakers or headphones whenever you type on any keyboard.

---

## Features

- **9 Built-in Switch Profiles**:
  - Cherry MX Red (Smooth linear)
  - Cherry MX Brown (Light tactile bump)
  - Cherry MX Black (Heavy linear)
  - Cherry MX Blue (Crisp audible click)
  - Creamy Thock (Deep, muted sound signature)
  - Gateron Yellow (Buttery smooth linear)
  - Kailh Box Jade (Sharp thick clickbar)
  - Topre Electro-Capacitive (Subtle deep dome collapse)
  - IBM Model M (Classic buckling spring)
- **Ultra-Low Latency Audio**:
  - **Windows**: WASAPI shared low-latency playback (`WasapiPlayer`) with real-time in-memory mixing.
  - **Linux & macOS**: PipeWire / PulseAudio / ALSA and CoreAudio via `miniaudio` (`SoundFlow`) pulling directly from MekTok's lock-free `Span<float>` mixer.
- **Custom Mechvibes Sound Packs**: Import community packs (`config.json` + `.wav` / `.ogg` / `.mp3`) via folder or `.zip` with pure-managed cross-platform decoding (`NAudio.Core`, `NVorbis`, `NLayer`) and case-insensitive Windows pack path resolution.
- **Custom Key Combination Sounds**: Map simultaneous key chords (e.g. `Shift + Y + !` or `Ctrl + Alt + W`) to custom sound effects (`.wav`, `.mp3`, `.ogg`) with interactive live keyboard recording and automatic switch sound suppression.
- **Visual Keyboard Configurator**: Interactive ANSI 104-key / 87-key TKL visual keyboard map to test and customize switch sounds per key or key group.
- **Independent Controls**: Master volume, key-up release sounds toggle, humanization, and realistic stereo panning per physical key position.
- **Per-Application Sound Rules**: Auto-mute on fullscreen apps or excluded foreground apps (Win32 `WinEventHook` on Windows, X11 EWMH on Linux Mint/X11, `NSWorkspace` on macOS).
- **System Tray Integration & Global Mute Hotkey**: Runs quietly in the system tray / menu bar with a configurable global mute hotkey (`Ctrl + Alt + M` default).
- **Privacy & Security**: Operates 100% offline, requires no admin/root privileges on Windows or Linux X11, never logs keystrokes, and sends zero telemetry.

---

## Platform Downloads & Notes

Check **Releases** for pre-built packages:
- **Windows (10/11 x64)**: `MekTok-win-x64.zip` / `MekTok.exe`
  - If Windows SmartScreen appears, click **More info** $\rightarrow$ **Run anyway**.
- **Linux Mint / Ubuntu / Debian (x64)**: `MekTok-<version>-linux-x64.deb` or portable `MekTok-<version>-linux-x64.AppImage`
  - Install `.deb` on Linux Mint: `sudo apt install ./MekTok-*-linux-x64.deb`
  - **X11 vs. Wayland**: Linux Mint Cinnamon runs on X11 by default, where global key capture (`libuiohook`/X11) and per-app auto-pause work out of the box with standard user permissions. On native Wayland sessions, compositor isolation limits global key snooping to XWayland windows unless an `evdev` (`input` group) backend is used.
  - Flatpak/Snap are intentionally avoided because sandbox isolation blocks global keyboard monitoring.
- **macOS (Apple Silicon `arm64` & Intel `x64`, macOS 13+)**: `MekTok-<version>-osx-arm64.zip` / `osx-x64.zip` (`MekTok.app`)
  - **Permissions**: Grant **Input Monitoring** (and **Accessibility** if prompted) in *System Settings $\rightarrow$ Privacy & Security*.
  - **Secure Input**: Password prompts and apps using macOS Secure Input remain silent by OS security design.
  - **Unsigned Builds**: If built/archived without an Apple Developer ID certificate, right-click `MekTok.app` $\rightarrow$ **Open** (or clear quarantine with `xattr -dr com.apple.quarantine MekTok.app`) on first launch.

---

## Building from Source

### Prerequisites
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (current LTS)
  - On **Linux Mint 22.x / Ubuntu 24.04+**:
    ```bash
    sudo apt update && sudo apt install dotnet-sdk-10.0 libx11-6 libxi6 libxtst6 libpulse0
    ```
- **Windows builds** require a Windows host (or `windows-latest` CI) for WPF XAML/WinForms compilation.

### Steps (Linux Mint & macOS)

1. **Clone the repository**:
   ```bash
   git clone https://github.com/LuciOddCode/MekTok.git
   cd MekTok
   ```

2. **Restore & run the cross-platform test suite**:
   ```bash
   dotnet test MekTok.CrossPlatform.slnf -c Release
   ```

3. **Run the Avalonia Desktop app**:
   ```bash
   dotnet run --project src/MekTok.Desktop/MekTok.Desktop.csproj
   ```
   Optional diagnostics:
   ```bash
   dotnet run --project src/MekTok.Desktop/MekTok.Desktop.csproj -- --self-test
   dotnet run --project src/MekTok.Desktop/MekTok.Desktop.csproj -- --latency-test
   ```

4. **Publish & package**:
   - **Linux Mint (`.deb` / `.AppImage`)**:
     ```bash
     dotnet publish src/MekTok.Desktop/MekTok.Desktop.csproj -c Release -r linux-x64 --self-contained -o publish/linux-x64
     ./packaging/linux/build-deb.sh publish/linux-x64 2.0.0 publish/MekTok-2.0.0-linux-x64.deb
     ```
   - **macOS (`.app`)**:
     ```bash
     dotnet publish src/MekTok.Desktop/MekTok.Desktop.csproj -c Release -r osx-arm64 --self-contained -o publish/osx-arm64
     ./packaging/macos/build-app-bundle.sh publish/osx-arm64 2.0.0 "$PWD/publish/MekTok-2.0.0-osx-arm64.zip"
     ```
   - **Windows (WPF single-file `.exe`)**:
     ```powershell
     dotnet test MekTok.sln -c Release
     dotnet publish src/MekTok.App/MekTok.App.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
     ```

---

## Architecture Overview

- **`MekTok.Core`** (`net10.0`, OS-agnostic):
  - `SwitchSynthesizer`, `KeyboardAudioMixer`, `SoundResolver`, `AudioLoader` (pure managed WAV/OGG/MP3)
  - `MechvibesPackImporter`, `Ansi104Layout`, `KeyRepeatFilter`, `HotkeyKey`, `SettingsStore`
  - `MekTokController` & `MekTok.Core.Platform` interfaces (`IAudioOutputEngine`, `IGlobalKeyboardService`, `IGlobalHotkeyService`, `IActiveWindowMonitor`, `IStartupManager`, `ISingleInstanceGuard`)
- **`MekTok.Platform.Desktop`** (`net10.0`):
  - `SharpHookKeyboardService` & `SharpHookKeyMap` (`libuiohook` across Linux X11, macOS, and Windows)
  - `MiniAudioOutputEngine` (`SoundFlow` / `miniaudio` across PipeWire/PulseAudio/ALSA, CoreAudio, and WASAPI)
  - `KeyboardListenerHotkeyService`, `LinuxX11ActiveWindowMonitor`, `MacOsActiveWindowMonitor`, `LinuxStartupManager`, `MacOsStartupManager`, `UnixSingleInstanceGuard`
- **`MekTok.Desktop`** (`net10.0`, Avalonia 12 UI):
  - Cross-platform Fluent Dark/Light UI, native tray menu, visual 104-key configurator, and diagnostics (`--self-test`, `--latency-test`)
- **`MekTok.App`** (`net10.0-windows`, WPF):
  - Windows-native Fluent UI with `WasapiAudioEngine`, `RawInputKeyboardService`, `ActiveWindowMonitor`, and `TrayFlyoutWindow`
- **`MekTok.Core.Tests`** (`net10.0`):
  - Cross-platform xUnit test suite running on Windows, Linux, and macOS CI runners.

---

## Contributing

Contributions are welcome! Feel free to open an issue or submit a pull request with new features, native Wayland `evdev` / `XInput2` / `IOHIDManager` keyboard backends, switch profiles, or bug fixes.

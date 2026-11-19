# MekTok

MekTok is a lightweight, low-latency mechanical keyboard sound simulator for Windows. It plays realistic mechanical switch sounds through your speakers or headphones whenever you type on any keyboard.

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
- **Ultra-Low Latency Audio**: Driven by NAudio and Windows WASAPI with real-time in-memory mixing.
- **Custom Mechvibes Sound Packs**: Import custom sound packs (`config.json` + `.wav` / `.mp3`) with one click.
- **Visual Keyboard Configurator**: Interactive ANSI 104-key visual keyboard map to test and customize switch sounds per key group.
- **Independent Controls**: Master volume, key-up release sounds toggle, and realistic stereo panning (sound pans slightly left or right depending on key position).
- **Per-Application Sound Rules**: Set custom volume, switch profiles, or mute MekTok entirely when specific games or software are active.
- **Global Tray Flyout**: Minimizes quietly to the Windows system tray with a quick-access flyout and a global mute hotkey (`Ctrl + Alt + M`).
- **Privacy & Security**: Operates completely offline, requires standard user permissions (no admin required), does not log keystrokes, and sends zero telemetry.

---

## Download Pre-built Executable

1. Navigate to the **Releases** section on the right side of this repository.
2. Download the latest `MekTok-win-x64.zip` or `MekTok.exe`.
3. Extract and run `MekTok.exe`.

> **Note on Windows SmartScreen**:  
> Because this is an independent open-source project without an enterprise code-signing certificate, Windows SmartScreen may show a prompt saying *"Windows protected your PC"*. Click **More info** $\rightarrow$ **Run anyway** to launch the app.

---

## Building from Source

### Prerequisites
- Windows 10 or Windows 11 (64-bit)
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or higher

### Steps

1. **Clone the repository**:
   ```powershell
   git clone https://github.com/LuciOddCode/MekTok.git
   cd MekTok
   ```

2. **Restore dependencies & run tests**:
   ```powershell
   dotnet test
   ```

3. **Run in development mode**:
   ```powershell
   dotnet run --project src/MekTok.App/MekTok.App.csproj
   ```

4. **Publish as a standalone single executable**:
   ```powershell
   dotnet publish src/MekTok.App/MekTok.App.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
   ```
   The self-contained executable will be generated at `./publish/MekTok.exe`.

---

## Architecture Overview

- **`MekTok.Core`**:
  - `SwitchSynthesizer`: Real-time mathematical synthesis for mechanical switch acoustic profiles.
  - `KeyboardAudioMixer`: Low-latency floating-point mixing and dynamic voice management.
  - `MechvibesPackImporter`: Parser and loader for custom community audio packs.
  - `SettingsStore`: JSON persistence for user profiles, volume, and hotkeys in `%APPDATA%\MekTok`.
- **`MekTok.App`**:
  - `RawInputKeyboardService`: Win32 Raw Input hook for ultra-responsive key detection.
  - `ActiveWindowMonitor`: Win32 foreground window tracker for per-application audio rules.
  - `MainWindow` & `TrayFlyoutWindow`: Windows 11 Fluent dark/light user interface.
- **`MekTok.Core.Tests`**:
  - xUnit test suite verifying audio mixing, repeat filtering, layout mapping, and pack parsing.

---

## Contributing

Contributions are welcome! Feel free to open an issue or submit a pull request with new features, switch profiles, or bug fixes.


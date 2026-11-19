using MekTok.Core.Config;
using MekTok.Core.Keyboard;
using MekTok.Core.Platform;
using MekTok.Platform.Desktop.Input;
using Xunit;

namespace MekTok.Core.Tests;

public class KeyboardListenerHotkeyServiceTests
{
    private sealed class FakeKeyboardService : IGlobalKeyboardService
    {
        public bool SupportsPerDeviceFiltering => false;
        public string? PermissionNotice => null;

        public event Action<RawKeyInputEvent>? KeyEvent;
        public event EventHandler? KeyboardsChanged;

        public void Start() { }
        public IReadOnlyList<KeyboardDeviceInfo> GetConnectedKeyboards() => Array.Empty<KeyboardDeviceInfo>();

        public void Emit(int scanCode, bool isKeyUp) =>
            KeyEvent?.Invoke(new RawKeyInputEvent(scanCode, isKeyUp, "FAKE"));

        public void Dispose() => _ = KeyboardsChanged;
    }

    [Fact]
    public void ApplyHotkey_TriggersOnceOnExactModifierMatchAndIgnoresRepeat()
    {
        using var keyboard = new FakeKeyboardService();
        using var service = new KeyboardListenerHotkeyService(keyboard);

        (bool ok, string? err) = service.ApplyHotkey(new HotkeyConfig
        {
            Enabled = true,
            Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt,
            Key = HotkeyKey.M
        });

        Assert.True(ok);
        Assert.Null(err);

        int fireCount = 0;
        service.HotkeyPressed += (_, _) => fireCount++;

        const int lCtrl = 0x001D;
        const int lAlt = 0x0038;
        const int keyM = 0x0032;

        // Extra Shift modifier must NOT trigger Ctrl+Alt+M
        keyboard.Emit(0x002A, isKeyUp: false);
        keyboard.Emit(lCtrl, isKeyUp: false);
        keyboard.Emit(lAlt, isKeyUp: false);
        keyboard.Emit(keyM, isKeyUp: false);
        Assert.Equal(0, fireCount);
        keyboard.Emit(keyM, isKeyUp: true);
        keyboard.Emit(0x002A, isKeyUp: true);

        // Exact Ctrl+Alt+M fires once even with repeated key-downs while held
        keyboard.Emit(keyM, isKeyUp: false);
        keyboard.Emit(keyM, isKeyUp: false);
        keyboard.Emit(keyM, isKeyUp: false);
        Assert.Equal(1, fireCount);

        // Releasing M and pressing again triggers a second time
        keyboard.Emit(keyM, isKeyUp: true);
        keyboard.Emit(keyM, isKeyUp: false);
        Assert.Equal(2, fireCount);
    }
}


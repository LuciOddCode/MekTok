using MekTok.Core.Keyboard;
using Xunit;

namespace MekTok.Core.Tests;

public class KeyRepeatFilterTests
{
    [Fact]
    public void ShouldTrigger_SuppressesHeldAlphaRepeatsWhenEnabled()
    {
        var filter = new KeyRepeatFilter();
        const int keyA = 0x001E;

        Assert.True(filter.ShouldTrigger(keyA, isKeyUp: false, suppressHoldRepeat: true, allowEditingKeyRepeat: true));
        Assert.False(filter.ShouldTrigger(keyA, isKeyUp: false, suppressHoldRepeat: true, allowEditingKeyRepeat: true));
        Assert.False(filter.ShouldTrigger(keyA, isKeyUp: false, suppressHoldRepeat: true, allowEditingKeyRepeat: true));
        Assert.True(filter.ShouldTrigger(keyA, isKeyUp: true, suppressHoldRepeat: true, allowEditingKeyRepeat: true));

        // Stray key-up without prior key-down should be ignored
        Assert.False(filter.ShouldTrigger(keyA, isKeyUp: true, suppressHoldRepeat: true, allowEditingKeyRepeat: true));
    }

    [Fact]
    public void ShouldTrigger_RespectsEditingKeyRepeatOption()
    {
        var filter = new KeyRepeatFilter();
        const int backspace = 0x000E;

        Assert.True(filter.ShouldTrigger(backspace, isKeyUp: false, suppressHoldRepeat: true, allowEditingKeyRepeat: true));
        Assert.True(filter.ShouldTrigger(backspace, isKeyUp: false, suppressHoldRepeat: true, allowEditingKeyRepeat: true));
        Assert.False(filter.ShouldTrigger(backspace, isKeyUp: false, suppressHoldRepeat: true, allowEditingKeyRepeat: false));
    }

    [Fact]
    public void Reset_ClearsHeldStateWithoutPhantomRelease()
    {
        var filter = new KeyRepeatFilter();
        const int space = 0x0039;

        Assert.True(filter.ShouldTrigger(space, isKeyUp: false, suppressHoldRepeat: true, allowEditingKeyRepeat: true));
        filter.Reset();

        // Release after Reset should not emit stray release sound
        Assert.False(filter.ShouldTrigger(space, isKeyUp: true, suppressHoldRepeat: true, allowEditingKeyRepeat: true));
        // Fresh press after Reset triggers immediately
        Assert.True(filter.ShouldTrigger(space, isKeyUp: false, suppressHoldRepeat: true, allowEditingKeyRepeat: true));
    }
}


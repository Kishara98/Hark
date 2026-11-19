namespace Hark.Core.Tests;

public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Alt+R", HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x52)]
    [InlineData(" control + shift + f9 ", HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x78)]
    [InlineData("Win+Alt+5", HotkeyModifiers.Win | HotkeyModifiers.Alt, 0x35)]
    [InlineData("Ctrl+F12", HotkeyModifiers.Control, 0x7B)]
    public void TryParse_accepts(string text, HotkeyModifiers modifiers, int virtualKey)
    {
        Assert.True(Hotkey.TryParse(text, out Hotkey hotkey));
        Assert.Equal(modifiers, hotkey.Modifiers);
        Assert.Equal(virtualKey, hotkey.VirtualKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("R")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+R+T")]
    [InlineData("Ctrl+F13")]
    [InlineData("Ctrl+Space")]
    [InlineData("Ctrl++R")]
    public void TryParse_rejects(string text)
    {
        Assert.False(Hotkey.TryParse(text, out _));
    }

    [Fact]
    public void ToString_is_canonical()
    {
        Assert.True(Hotkey.TryParse("shift+alt+ctrl+win+r", out Hotkey hotkey));
        Assert.Equal("Ctrl+Alt+Shift+Win+R", hotkey.ToString());
    }

    [Fact]
    public void ToString_formats_function_and_digit_keys()
    {
        Assert.True(Hotkey.TryParse("alt+f9", out Hotkey f9));
        Assert.True(Hotkey.TryParse("ctrl+7", out Hotkey seven));
        Assert.Equal("Alt+F9", f9.ToString());
        Assert.Equal("Ctrl+7", seven.ToString());
    }
}

namespace IdleViz.Core.Tests;

public class HotkeyTests
{
    private const ushort KeyV = 0x56;

    [Fact]
    public void TheDefaultIsCtrlAltV()
    {
        Assert.Equal("Ctrl + Alt + V", Hotkey.Default.Label);
        Assert.Equal("Ctrl+Alt+V", Hotkey.Default.MenuLabel);
        Assert.True(Hotkey.Default.IsValid);
    }

    [Fact]
    public void ModifiersAreListedInAFixedOrder()
    {
        var all = HotkeyModifiers.Win | HotkeyModifiers.Shift | HotkeyModifiers.Alt | HotkeyModifiers.Ctrl;
        Assert.Equal("Ctrl + Alt + Shift + Win + F5", new Hotkey(all, 0x74).Label);
    }

    [Fact]
    public void NamesKeys()
    {
        Assert.Equal("A", Hotkey.KeyName(0x41));
        Assert.Equal("7", Hotkey.KeyName(0x37));
        Assert.Equal("F1", Hotkey.KeyName(0x70));
        Assert.Equal("F24", Hotkey.KeyName(0x87));
        Assert.Equal("Num 3", Hotkey.KeyName(0x63));
        Assert.Equal("Space", Hotkey.KeyName(0x20));
        Assert.Equal("←", Hotkey.KeyName(0x25));
        Assert.Equal("Key 255", Hotkey.KeyName(0xFF));
    }

    [Fact]
    public void AHotkeyNeedsAModifierAndAKey()
    {
        Assert.False(new Hotkey(HotkeyModifiers.None, KeyV).IsValid);
        Assert.False(new Hotkey(HotkeyModifiers.Ctrl, 0).IsValid);
        // Ctrl + Shift alone: the key itself is a modifier.
        Assert.False(new Hotkey(HotkeyModifiers.Ctrl, 0xA0).IsValid);
        Assert.True(new Hotkey(HotkeyModifiers.Win, KeyV).IsValid);
    }

    [Fact]
    public void ModifierKeysMapToTheirModifier()
    {
        Assert.Equal(HotkeyModifiers.Ctrl, Hotkey.ModifierFor(0xA2));
        Assert.Equal(HotkeyModifiers.Ctrl, Hotkey.ModifierFor(0xA3));
        Assert.Equal(HotkeyModifiers.Alt, Hotkey.ModifierFor(0xA4));
        Assert.Equal(HotkeyModifiers.Shift, Hotkey.ModifierFor(0xA1));
        Assert.Equal(HotkeyModifiers.Win, Hotkey.ModifierFor(0x5B));
        Assert.Equal(HotkeyModifiers.None, Hotkey.ModifierFor(KeyV));
        Assert.True(Hotkey.IsModifierKey(0x5C));
        Assert.False(Hotkey.IsModifierKey(KeyV));
    }
}

public class HotkeySettingTests
{
    [Fact]
    public void NothingStoredIsTheDefault()
    {
        Assert.Equal(Hotkey.Default, HotkeySetting.Read(new SettingsStore()));
    }

    [Fact]
    public void RoundTrips()
    {
        var settings = new SettingsStore();
        var hotkey = new Hotkey(HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, 0x70);
        HotkeySetting.Write(settings, hotkey);
        Assert.Equal(hotkey, HotkeySetting.Read(settings));
    }

    [Fact]
    public void AClearedHotkeyStaysCleared()
    {
        var settings = new SettingsStore();
        HotkeySetting.Write(settings, null);
        Assert.Null(HotkeySetting.Read(settings));
    }

    [Fact]
    public void UnusableValuesFallBackToTheDefault()
    {
        var noModifier = new SettingsStore();
        noModifier.SetInt(HotkeySetting.KeyKey, 0x56);
        noModifier.SetInt(HotkeySetting.ModifiersKey, 0);
        Assert.Equal(Hotkey.Default, HotkeySetting.Read(noModifier));

        var outOfRange = new SettingsStore();
        outOfRange.SetInt(HotkeySetting.KeyKey, 70000);
        outOfRange.SetInt(HotkeySetting.ModifiersKey, 2);
        Assert.Equal(Hotkey.Default, HotkeySetting.Read(outOfRange));

        var wrongType = new SettingsStore();
        wrongType.SetString(HotkeySetting.KeyKey, "V");
        wrongType.SetInt(HotkeySetting.ModifiersKey, 2);
        Assert.Equal(Hotkey.Default, HotkeySetting.Read(wrongType));
    }
}

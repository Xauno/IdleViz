namespace IdleViz.Core.Tests;

public class DismissTrackerTests
{
    // Windows virtual-key codes. The hooks report the sided modifier keys.
    private const ushort Control = 0xA2;
    private const ushort Alt = 0xA4;
    private const ushort KeyA = 0x41;
    private const ushort KeyL = 0x4C;
    private const ushort VolumeUp = 0xAF;

    private static DismissTracker Armed(params ushort[] holding)
    {
        var tracker = new DismissTracker(gracePeriod: 0.4);
        tracker.Arm(holding, elapsed: 0.4);
        return tracker;
    }

    private static DismissTracker ArmedWithPassKey(params ushort[] holding)
    {
        var tracker = new DismissTracker(gracePeriod: 0.4, passKeys: [KeyL]);
        tracker.Arm(holding, elapsed: 0.4);
        return tracker;
    }

    private static InputEvent Down(ushort code) => InputEvent.Key(code, isDown: true, isRepeat: false);

    private static InputEvent Repeat(ushort code) => InputEvent.Key(code, isDown: true, isRepeat: true);

    private static InputEvent Up(ushort code) => InputEvent.Key(code, isDown: false, isRepeat: false);

    // Grace period

    [Fact]
    public void IgnoresEverythingBeforeArmed()
    {
        var tracker = new DismissTracker(gracePeriod: 0.4);
        InputEvent[] events =
        [
            InputEvent.MouseMoved(5, 0), InputEvent.MouseDown, InputEvent.Scroll, InputEvent.Gesture, Down(KeyA),
        ];
        foreach (var input in events)
        {
            Assert.False(tracker.ShouldDismiss(input, elapsed: 0.2), input.ToString());
        }

        Assert.False(tracker.ShouldDismiss(secondsSinceLastInput: 0, heldKeys: [], elapsed: 0.3));
    }

    // Events

    [Fact]
    public void AnyInputClosesOnceArmed()
    {
        InputEvent[] events = [InputEvent.MouseDown, InputEvent.Scroll, InputEvent.Gesture, Down(KeyA), Down(Control)];
        foreach (var input in events)
        {
            Assert.True(Armed().ShouldDismiss(input, elapsed: 1), input.ToString());
        }
    }

    [Fact]
    public void TinyMouseMoveCloses()
    {
        Assert.True(Armed().ShouldDismiss(InputEvent.MouseMoved(0, -0.5), elapsed: 1));
    }

    [Fact]
    public void ZeroDeltaMouseMoveIsIgnored()
    {
        Assert.False(Armed().ShouldDismiss(InputEvent.MouseMoved(0, 0), elapsed: 1));
    }

    [Fact]
    public void ReleasingAnUnheldKeyDoesNotClose()
    {
        Assert.False(Armed().ShouldDismiss(Up(KeyA), elapsed: 1));
    }

    // Keys held past the grace period

    [Fact]
    public void ReleasingHeldModifiersIsIgnored()
    {
        var tracker = Armed(Control, Alt);
        Assert.False(tracker.ShouldDismiss(Up(Control), elapsed: 0.9));
        Assert.False(tracker.ShouldDismiss(Up(Alt), elapsed: 1.2));
        Assert.Empty(tracker.StuckKeys);
    }

    [Fact]
    public void PressingAHeldKeyAgainCloses()
    {
        var tracker = Armed(Control);
        Assert.False(tracker.ShouldDismiss(Up(Control), elapsed: 0.9));
        Assert.True(tracker.ShouldDismiss(Down(Control), elapsed: 2));
    }

    [Fact]
    public void KeyRepeatOfAHeldKeyIsIgnored()
    {
        var tracker = Armed(KeyA);
        Assert.False(tracker.ShouldDismiss(Repeat(KeyA), elapsed: 0.8));
        Assert.False(tracker.ShouldDismiss(Repeat(KeyA), elapsed: 0.9));
    }

    [Fact]
    public void FreshPressOfAStuckKeyCloses()
    {
        // The release was missed, so a non-repeat press is new input.
        Assert.True(Armed(KeyA).ShouldDismiss(Down(KeyA), elapsed: 2));
    }

    [Fact]
    public void OtherInputStillClosesWhileAKeyIsHeld()
    {
        Assert.True(Armed(Control).ShouldDismiss(Down(KeyA), elapsed: 1));
        Assert.True(Armed(Control).ShouldDismiss(InputEvent.MouseDown, elapsed: 1));
    }

    // Like and skip keys

    [Fact]
    public void APassKeyNeverCloses()
    {
        var tracker = ArmedWithPassKey();
        Assert.False(tracker.ShouldDismiss(Down(KeyL), elapsed: 1));
        Assert.False(tracker.ShouldDismiss(Repeat(KeyL), elapsed: 1.5));
        Assert.False(tracker.ShouldDismiss(Up(KeyL), elapsed: 1.6));
        // Pressed again, it still doesn't close.
        Assert.False(tracker.ShouldDismiss(Down(KeyL), elapsed: 2));
    }

    [Fact]
    public void OtherKeysStillCloseNextToAPassKey()
    {
        var tracker = ArmedWithPassKey();
        Assert.False(tracker.ShouldDismiss(Down(KeyL), elapsed: 1));
        Assert.True(tracker.ShouldDismiss(Down(KeyA), elapsed: 1.1));
    }

    [Fact]
    public void BackupIgnoresAPassKeyTheHookHandled()
    {
        var tracker = ArmedWithPassKey();
        Assert.False(tracker.ShouldDismiss(Down(KeyL), elapsed: 1.0));
        Assert.False(tracker.ShouldDismiss(secondsSinceLastInput: 0.03, heldKeys: [KeyL], elapsed: 1.02));
        Assert.False(tracker.ShouldDismiss(Up(KeyL), elapsed: 1.1));
        Assert.False(tracker.ShouldDismiss(secondsSinceLastInput: 0.02, heldKeys: [], elapsed: 1.11));
        // Input after that is new.
        Assert.True(tracker.ShouldDismiss(secondsSinceLastInput: 0.01, heldKeys: [], elapsed: 2.0));
    }

    [Fact]
    public void BackupClosesOnAPassKeyTheHookNeverSaw()
    {
        // The hook was dropped, so the key did nothing here.
        Assert.True(ArmedWithPassKey().ShouldDismiss(secondsSinceLastInput: 0.05, heldKeys: [KeyL], elapsed: 3));
    }

    [Fact]
    public void APassKeyHeldSinceOpeningIsNotStuck()
    {
        var tracker = ArmedWithPassKey(KeyL);
        Assert.False(tracker.ShouldDismiss(Up(KeyL), elapsed: 0.9));
        Assert.Empty(tracker.StuckKeys);
        Assert.False(tracker.ShouldDismiss(Down(KeyL), elapsed: 2));
    }

    // Media keys

    [Fact]
    public void MediaKeysAreRecognized()
    {
        // Mute, volume down and up, next, previous, play/pause.
        foreach (ushort key in new ushort[] { 0xAD, 0xAE, 0xAF, 0xB0, 0xB1, 0xB3 })
        {
            Assert.True(MediaKeys.Contains(key), $"0x{key:X}");
        }
    }

    [Fact]
    public void OtherKeysAreNotMediaKeys()
    {
        // Stop, mail, media select, the two app keys, browser back and home, the Windows key, F1, Caps Lock, A.
        foreach (ushort key in new ushort[] { 0xB2, 0xB4, 0xB5, 0xB6, 0xB7, 0xA6, 0xAC, 0x5B, 0x70, 0x14, KeyA })
        {
            Assert.False(MediaKeys.Contains(key), $"0x{key:X}");
        }
    }

    [Fact]
    public void AMediaKeyNeverCloses()
    {
        var tracker = Armed();
        Assert.False(tracker.ShouldDismiss(InputEvent.MediaKey, elapsed: 1));
        Assert.False(tracker.ShouldDismiss(InputEvent.MediaKey, elapsed: 1.1));
        Assert.True(tracker.ShouldDismiss(Down(KeyA), elapsed: 1.2));
    }

    [Fact]
    public void BackupIgnoresAMediaKeyTheHookHandled()
    {
        var tracker = Armed();
        Assert.False(tracker.ShouldDismiss(InputEvent.MediaKey, elapsed: 1.0));
        Assert.False(tracker.ShouldDismiss(secondsSinceLastInput: 0.03, heldKeys: [VolumeUp], elapsed: 1.02));
        Assert.True(tracker.ShouldDismiss(secondsSinceLastInput: 0.01, heldKeys: [], elapsed: 2.0));
    }

    // Seen on the PC: a play key the hook had handled closed the window 2.4 s later. The tick count the
    // backup check reads moves in 16 ms steps, so the same key press can look a few ms newer than it was.
    [Fact]
    public void BackupIgnoresAMediaKeyThatLooksAFewMillisecondsLater()
    {
        var tracker = Armed();
        Assert.False(tracker.ShouldDismiss(InputEvent.MediaKey, elapsed: 1.0));
        Assert.False(tracker.ShouldDismiss(secondsSinceLastInput: 0.484, heldKeys: [], elapsed: 1.5));
        Assert.False(tracker.ShouldDismiss(secondsSinceLastInput: 2.384, heldKeys: [], elapsed: 3.4));
        // A real key press 0.1 s after the media key is still new input.
        Assert.True(tracker.ShouldDismiss(secondsSinceLastInput: 0.3, heldKeys: [], elapsed: 1.4));
    }

    // Backup check

    [Fact]
    public void BackupIgnoresTheTriggerInput()
    {
        // The hotkey was released 0.1 s after opening, 2 s ago.
        Assert.False(Armed().ShouldDismiss(secondsSinceLastInput: 2.0, heldKeys: [], elapsed: 2.1));
    }

    [Fact]
    public void BackupClosesOnInputAfterGracePeriod()
    {
        Assert.True(Armed().ShouldDismiss(secondsSinceLastInput: 0.05, heldKeys: [], elapsed: 3));
    }

    [Fact]
    public void BackupIgnoresReleaseOfHeldModifiers()
    {
        var tracker = Armed(Control, Alt);
        // Still held: the key repeat or held state keeps resetting the idle time.
        Assert.False(tracker.ShouldDismiss(secondsSinceLastInput: 0.01, heldKeys: [Control, Alt], elapsed: 0.5));
        // Control let go between checks.
        Assert.False(tracker.ShouldDismiss(secondsSinceLastInput: 0.05, heldKeys: [Alt], elapsed: 0.6));
        // Alt let go between checks.
        Assert.False(tracker.ShouldDismiss(secondsSinceLastInput: 0.03, heldKeys: [], elapsed: 1.7));
        // Nothing since.
        Assert.False(tracker.ShouldDismiss(secondsSinceLastInput: 1.03, heldKeys: [], elapsed: 2.7));
        // New input.
        Assert.True(tracker.ShouldDismiss(secondsSinceLastInput: 0.02, heldKeys: [], elapsed: 3.0));
    }

    [Fact]
    public void BackupIgnoresReleaseTheHookAlreadyHandled()
    {
        var tracker = Armed(Control);
        Assert.False(tracker.ShouldDismiss(Up(Control), elapsed: 1.0));
        Assert.False(tracker.ShouldDismiss(secondsSinceLastInput: 0.05, heldKeys: [], elapsed: 1.05));
        Assert.True(tracker.ShouldDismiss(secondsSinceLastInput: 0.01, heldKeys: [], elapsed: 2.0));
    }

    [Fact]
    public void BackupPressingAHeldKeyAgainCloses()
    {
        var tracker = Armed(Control);
        Assert.False(tracker.ShouldDismiss(secondsSinceLastInput: 0.05, heldKeys: [], elapsed: 1.0));
        Assert.True(tracker.ShouldDismiss(secondsSinceLastInput: 0.05, heldKeys: [Control], elapsed: 2.0));
    }
}

public class KeyboardInputTests
{
    private const ushort KeyA = 0x41;

    [Fact]
    public void ASecondDownWithoutAnUpIsARepeat()
    {
        var keyboard = new KeyboardInput();
        Assert.Equal(InputEvent.Key(KeyA, isDown: true, isRepeat: false), keyboard.Translate(KeyA, isDown: true));
        Assert.Equal(InputEvent.Key(KeyA, isDown: true, isRepeat: true), keyboard.Translate(KeyA, isDown: true));
        Assert.Equal(InputEvent.Key(KeyA, isDown: false, isRepeat: false), keyboard.Translate(KeyA, isDown: false));
        Assert.Equal(InputEvent.Key(KeyA, isDown: true, isRepeat: false), keyboard.Translate(KeyA, isDown: true));
    }

    [Fact]
    public void AKeyHeldWhenTheHooksStartRepeats()
    {
        var keyboard = new KeyboardInput();
        keyboard.Reset([KeyA]);
        Assert.True(keyboard.Translate(KeyA, isDown: true).IsRepeat);
    }

    [Fact]
    public void MediaKeysBecomeMediaKeyEvents()
    {
        var keyboard = new KeyboardInput();
        Assert.Equal(InputEvent.MediaKey, keyboard.Translate(0xAF, isDown: true));
        Assert.Equal(InputEvent.MediaKey, keyboard.Translate(0xAF, isDown: false));
        Assert.Equal(InputKind.Key, keyboard.Translate(0xB2, isDown: true).Kind);
    }

    [Fact]
    public void KeyStateSkipsMouseButtonsAndUnsidedModifiers()
    {
        Assert.False(KeyboardInput.IsKeyboardKey(0x01));
        Assert.False(KeyboardInput.IsKeyboardKey(0x06));
        Assert.False(KeyboardInput.IsKeyboardKey(0x10));
        Assert.False(KeyboardInput.IsKeyboardKey(0x11));
        Assert.False(KeyboardInput.IsKeyboardKey(0x12));
        Assert.False(KeyboardInput.IsKeyboardKey(0xFF));
        Assert.True(KeyboardInput.IsKeyboardKey(0x08));
        Assert.True(KeyboardInput.IsKeyboardKey(KeyA));
        Assert.True(KeyboardInput.IsKeyboardKey(0xA2));
        Assert.True(KeyboardInput.IsKeyboardKey(0x5B));
    }
}

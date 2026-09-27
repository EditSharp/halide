using Godot;

namespace Halide.Scripts.Input;

// lives inside every window that is not the main one - an option button's
// popup, a dialog, a project's own OS window. a window routes input to its
// own viewport and never hands a key it did not use back to the main window,
// so without this a shortcut pressed there would simply vanish. this sits in
// the window's own unhandled-key group and feeds the key into the same
// dispatch the main window uses. an OS window's mouse never reaches the main
// window at all, so its mouse is fed to InputManager here too. InputManager
// plants one in each window as it appears
public partial class ShortcutRelay : Node
{
    public const string NodeName = "ShortcutRelay";

    // an embedded window's mouse already passes through the main window
    public override void _Input(InputEvent @event)
    {
        if (GetParent() is Window { } window && !window.IsEmbedded()) InputManager.Singleton.Feed(@event);
    }

    public override void _Notification(int what)
    {
        // the window lost focus mid-gesture; the release will never come
        if (what == NotificationWMWindowFocusOut && GetParent() is Window { } window && !window.IsEmbedded())
            InputManager.Singleton.Mouse.ForceReleaseAll();
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey key && InputManager.Singleton.Keyboard.Dispatch(key, GetViewport())) GetViewport().SetInputAsHandled();
    }
}

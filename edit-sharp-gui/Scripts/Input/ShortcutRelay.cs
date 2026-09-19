using Godot;

namespace EditSharpGUI.Scripts.Input;

// lives inside every window that is not the main one - an option button's
// popup, a dialog. an embedded window routes input to its own viewport and
// never hands a key it did not use back to the main window, so without this
// a shortcut pressed while a popup is open would simply vanish. this sits in
// the window's own unhandled-key group and feeds the key into the same
// dispatch the main window uses. InputManager plants one in each window as
// it appears
public partial class ShortcutRelay : Node
{
    public const string NodeName = "ShortcutRelay";

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey key && InputManager.Singleton.Keyboard.Dispatch(key, GetViewport())) GetViewport().SetInputAsHandled();
    }
}

using Godot;

namespace EditSharpGUI.Scripts.UI.ContextMenu;

[GlobalClass]
public partial class ContextButton : ContextBaseButton
{
    [Export] public bool Checked = false;

    [Export] public CheckType Type = CheckType.None;
    public enum CheckType { None, Check, Radio }

    // Checked has already been toggled when this fires. not fired for buttons inside a list
    [Signal] public delegate void PressedEventHandler();
}

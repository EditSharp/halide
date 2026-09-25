using Godot;
using Godot.Collections;

namespace EditSharpGUI.Scripts.UI.ContextMenu;

// list of buttons where any can be selected/deselected
[GlobalClass]
public partial class ContextCheckList : ContextElement
{
    [Export] public Array<ContextButton> Buttons = [];
    [Export] public Array<int> CheckedButtons = [];

    // the button and its new state; CheckedButtons is already updated
    [Signal] public delegate void ToggledEventHandler(ContextButton button, bool on);
}

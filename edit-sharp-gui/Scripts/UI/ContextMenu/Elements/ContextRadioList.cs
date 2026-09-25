using Godot;
using Godot.Collections;

namespace EditSharpGUI.Scripts.UI.ContextMenu;

// list of buttons where only one can be selected
[GlobalClass]
public partial class ContextRadioList : ContextElement
{
    [Export] public Array<ContextButton> Buttons = [];
    [Export] public int SelectedButton = 0;

    // SelectedButton is already updated
    [Signal] public delegate void SelectedEventHandler(ContextButton button);
}

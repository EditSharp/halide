using Godot;
using Godot.Collections;

namespace EditSharpGUI.Scripts.UI.ContextMenu;

[GlobalClass]
public partial class ContextSubmenu : ContextBaseButton
{
    [Export] public Array<ContextElement> Elements = [];
}

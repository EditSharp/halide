using Godot;

namespace EditSharpGUI.Scripts.UI.ContextMenu;

[GlobalClass]
public abstract partial class ContextElement : Resource
{
    [Export] public bool Visible = true;
}

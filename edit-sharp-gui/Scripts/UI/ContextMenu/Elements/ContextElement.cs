using Godot;

namespace EditSharpGUI.Scripts.UI.ContextMenu;

[GlobalClass]
public abstract partial class ContextElement : Resource
{
    // what code finds the element by, "clip.cut" say; blank for elements code never touches
    [Export] public string Id = "";

    [Export] public bool Visible = true;
}

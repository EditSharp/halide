using EditSharpGUI.Scripts.UI.ContextMenu;
using Godot;

// opens the assigned menu on ready and on right-click; the code-built example when none is assigned
public partial class TestPopup : Control
{
	[Export] ContextMenu popup;

	ContextMenu Menu => popup ?? ContextMenus.Example;

	public override void _Ready()
	{
		ContextMenus.ShowContextMenu(Menu);
	}

	public override void _GuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: false } click)
		{
			ContextMenus.ShowContextMenu(Menu, click.GlobalPosition);
			AcceptEvent();
		}
	}
}

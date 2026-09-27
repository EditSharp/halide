using Godot;

namespace EditSharpGUI.Scripts.UI.Docking;

// the copy of a dragged tab that follows the pointer; laid out in DockTabGhost.tscn
[GlobalClass]
public partial class DockTabGhost : PanelContainer
{
	public const string ScenePath = "res://Scenes/Docking/DockTabGhost.tscn";

	[Export] Label title;

	// where it sits from the pointer, clear of the compass tile being aimed at
	[Export] Vector2 offset = new(15, 15);

	public static DockTabGhost Create(string text)
	{
		DockTabGhost ghost = GD.Load<PackedScene>(ScenePath).Instantiate<DockTabGhost>();
		ghost.title.Text = text;
		return ghost;
	}

	// into `window` if it isn't there already, beside the pointer at `at`
	public void Follow(Window window, Vector2 at)
	{
		if (GetParent() != window)
		{
			GetParent()?.RemoveChild(this);
			window.AddChild(this);
		}

		ResetSize();
		GlobalPosition = (at + offset).Round();
		Visible = true;
	}
}

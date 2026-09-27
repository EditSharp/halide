using Godot;

namespace EditSharpGUI.Scripts.UI.Docking;

// where a view dragged outside every window would float, drawn as a see-through window; set up in DockOutline.tscn
[GlobalClass]
public partial class DockOutlineWindow : Window
{
	public const string ScenePath = "res://Scenes/Docking/DockOutline.tscn";

	public static DockOutlineWindow Create(Node parent)
	{
		DockOutlineWindow window = GD.Load<PackedScene>(ScenePath).Instantiate<DockOutlineWindow>();
		parent.AddChild(window);
		return window;
	}

	// over `rect` in screen pixels
	public void ShowAt(Rect2I rect)
	{
		Position = rect.Position;
		Size = rect.Size;
		if (!Visible) Show();
	}
}

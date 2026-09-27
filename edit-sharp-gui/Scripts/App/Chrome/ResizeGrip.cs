using Godot;

namespace EditSharpGUI.Scripts.App.Chrome;

// an invisible strip or corner of a borderless window that hands a resize to the compositor
[GlobalClass]
public partial class ResizeGrip : Control
{
	[Export] public DisplayServer.WindowResizeEdge Edge;

	public override void _GuiInput(InputEvent e)
	{
		if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) return;
		DisplayServer.WindowStartResize(Edge, GetWindow().GetWindowId());
		AcceptEvent();
	}
}

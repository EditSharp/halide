using EditSharpGUI.Scripts.Input;
using Godot;
using System;

// a grab point hung off one side of a clip. it only reports the life of the
// drag - started, ended, or abandoned - and the clips view does the rest from
// the cursor position every frame, so the handle keeps up with edge scrolling
// exactly like a dragged clip does. a click with no drag does nothing
public partial class UIDragHandle : Control, IDragCancellable
{
	[Export] public HandleSide Side;

	public enum HandleSide
	{
		Top,
		Right,
		Bottom,
		Left
	}

	public event EventHandler DragStarted;
	public event EventHandler DragEnded;
	public event EventHandler DragCancelled;

	public override void _GuiInput(InputEvent _)
	{
		MouseButtonState left = InputManager.Singleton.Mouse.LeftButton;

		switch (left.Action)
		{
			case MouseAction.Press:
			case MouseAction.DoubleClick:
				// claim the press, so whatever drag follows is unambiguously ours
				left.Capture(this);
				break;

			case MouseAction.DragStart:
				if (!left.HasCapture(this)) break;
				DragStarted?.Invoke(this, EventArgs.Empty);
				break;

			case MouseAction.DragEnd:
				if (!left.HasCapture(this)) break;
				DragEnded?.Invoke(this, EventArgs.Empty);
				break;
		}
	}

	// our captured drag will never get a release. Mouse calls this directly
	// because no input event is coming to route it
	public void CancelDrag(MouseButtonState button) => DragCancelled?.Invoke(this, EventArgs.Empty);
}

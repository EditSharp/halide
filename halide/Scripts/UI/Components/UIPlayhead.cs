using Halide.Scripts.Input;
using Godot;
using System;

// the line marking the current time. it knows nothing about time itself - the
// timeline owns that and puts this wherever it belongs. all this reports is
// when it is grabbed and when it is let go of; the timeline runs the drag in
// between from the cursor position, the same way the clips view runs a clip
// drag, so the playhead keeps up with edge scrolling and never drifts
public partial class UIPlayhead : VBoxContainer, IDragCancellable
{
	public event EventHandler Pressed;
	public event EventHandler Released;

	public override void _GuiInput(InputEvent _)
	{
		MouseButtonState left = InputManager.Singleton.Mouse.LeftButton;

		switch (left.Action)
		{
			case MouseAction.Press:
			case MouseAction.DoubleClick:
				// claim the press, so whatever drag follows is unambiguously ours
				if (!left.Capture(this)) break;
				Pressed?.Invoke(this, EventArgs.Empty);
				break;

			case MouseAction.Click:
			case MouseAction.DragEnd:
				if (!left.HasCapture(this)) break;
				Released?.Invoke(this, EventArgs.Empty);
				break;
		}
	}

	// the press will never get a release of its own - the window lost focus, or
	// the os swallowed the button - so let go now
	public void CancelDrag(MouseButtonState button) => Released?.Invoke(this, EventArgs.Empty);

	public void Move(float position) => Position = new(position, Position.Y);
}

using EditSharpGUI.Scripts.Input;
using Godot;
using System;

public partial class UIPlayhead : VBoxContainer
{
	public event EventHandler<float> Drag;

	protected virtual void OnDrag(float drag)
	{
		Drag.Invoke(this, drag);
	}

	public override void _GuiInput(InputEvent _)
	{
		MouseButtonState left = InputManager.Singleton.Mouse.LeftButton;

        switch (left.Action)
        {
            case MouseAction.Press:
            case MouseAction.DoubleClick:
                // claim the press, so whatever drag follows is unambiguously ours
                if (!left.Capture(this)) break;
                GD.Print("Playhead clicked");
                break;
			case MouseAction.DragStart:
			case MouseAction.DragMove:
				if (!left.HasCapture(this)) break;
				OnDrag(InputManager.Singleton.Mouse.GetDragStepDelta(left).X);
				break;
        }
	}

	public void Move(float position)
	{
		Position = new(
			position,
			Position.Y
		);
	}
}

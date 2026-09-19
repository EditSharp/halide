using EditSharpGUI.Scripts.Input;
using Godot;
using System;

namespace EditSharpGUI.Scripts.UI.Inspecting;

// a dial showing an angle as an angle: a pointer on a circle, turned by
// dragging - right or up to add degrees, left or down to take them away -
// so a turn is a turn and any number of degrees can be reached, past a
// full circle and back. a degree a pixel; a tenth with ctrl, ten with
// shift. zero points right and positive turns clockwise, the way the
// compositor reads rotation
public partial class AngleKnob : Control, IDragCancellable
{
	public bool ReadOnly
	{
		get;
		set { field = value; MouseDefaultCursorShape = value ? CursorShape.Arrow : CursorShape.PointingHand; }
	}

	public event Action DragBegan;
	public event Action<double> Changed;
	public event Action<double> Committed;
	public event Action DragEnded;

	double? degrees;
	bool dragging;
	bool hovered;
	Vector2 last;
	double dragValue;

	public override void _Ready()
	{
		MouseDefaultCursorShape = ReadOnly ? CursorShape.Arrow : CursorShape.PointingHand;
	}

	public void Display(double? value)
	{
		degrees = value;
		if (!dragging) QueueRedraw();
	}

	public override void _Notification(int what)
	{
		if (what == NotificationMouseEnter) { hovered = true; QueueRedraw(); }
		else if (what == NotificationMouseExit) { hovered = false; QueueRedraw(); }
	}

	public override void _Draw()
	{
		Vector2 centre = Size / 2f;
		float radius = Mathf.Min(Size.X, Size.Y) / 2f - 1.5f;

		Color track = GetThemeColor("track", "AngleKnob");
		Color fill = GetThemeColor("fill", "AngleKnob");
		Color pointer = GetThemeColor("pointer", "AngleKnob");

		DrawCircle(centre, radius, fill);
		DrawArc(centre, radius, 0f, Mathf.Tau, 48, hovered && !ReadOnly ? pointer : track, 1.5f, true);

		if (degrees is not double d) return;

		float angle = Mathf.DegToRad((float)d);
		Vector2 tip = centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (radius - 2f);

		DrawLine(centre, tip, pointer, 2f, true);
		DrawCircle(centre, 2f, pointer);
	}

	public override void _GuiInput(InputEvent _)
	{
		if (ReadOnly) return;

		Mouse mouse = InputManager.Singleton.Mouse;
		MouseButtonState left = mouse.LeftButton;

		switch (left.Action)
		{
			case MouseAction.Press:
			case MouseAction.DoubleClick:
				left.Capture(this);
				break;

			case MouseAction.DragStart:
				if (!left.HasCapture(this)) break;
				dragging = true;
				dragValue = degrees ?? 0d;
				last = mouse.CurrentPosition;
				DragBegan?.Invoke();
				goto case MouseAction.DragMove;

			case MouseAction.DragMove:
				if (!left.HasCapture(this) || !dragging) break;
				Drag(mouse.CurrentPosition, Godot.Input.IsKeyPressed(Key.Ctrl), Godot.Input.IsKeyPressed(Key.Shift));
				break;

			case MouseAction.DragEnd:
				if (!left.HasCapture(this)) break;
				FinishDrag();
				break;
		}
	}

	void Drag(Vector2 position, bool fine, bool coarse)
	{
		Vector2 delta = position - last;
		last = position;

		// right and up add; the screen's y grows downward, so up is negative
		double pixels = delta.X - delta.Y;
		double perPixel = fine ? 0.1d : coarse ? 10d : 1d;

		dragValue = Math.Round((dragValue + pixels * perPixel) / perPixel) * perPixel;
		degrees = dragValue;

		Changed?.Invoke(dragValue);
		QueueRedraw();
	}

	void FinishDrag()
	{
		if (!dragging) return;

		dragging = false;
		Committed?.Invoke(dragValue);
		DragEnded?.Invoke();
		QueueRedraw();
	}

	public void CancelDrag(MouseButtonState button) => FinishDrag();
}

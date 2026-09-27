using Halide.Scripts.Input;
using Halide.Scripts.UI.Theming;
using Godot;
using System;

namespace Halide.Scripts.UI.Inspecting;

// a dial showing an angle as an angle, from nodes in AngleKnob.tscn: a
// ring, a pointer that rotates with the value, a radial progress showing
// how far round the current turn has gone, and a count of whole turns.
// turned by dragging - right or up to add degrees, left or down to take
// them away - so a turn is a turn and any number of degrees can be
// reached. a degree a pixel; a tenth with ctrl, ten with shift. zero
// points right and positive turns clockwise, the way the compositor reads
// rotation. each part takes an icon; one given none gets one rasterised
[Tool]
public partial class AngleKnob : Control
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

	[ExportGroup("Parts")]
	[Export] TextureRect ring;
	[Export] TextureRect pointer;
	[Export] TextureProgressBar progress;
	[Export] Label turns;

	[ExportGroup("Icons")]
	[Export] public Texture2D RingIcon { get; set; }
	[Export] public Texture2D PointerIcon { get; set; }
	[Export] public Texture2D ProgressIcon { get; set; }

	double? degrees;
	bool dragging;
	Vector2 last;
	double dragValue;

	public override void _Ready()
	{
		MouseDefaultCursorShape = ReadOnly ? CursorShape.Arrow : CursorShape.PointingHand;

		int size = Mathf.RoundToInt(Mathf.Min(CustomMinimumSize.X, CustomMinimumSize.Y));
		if (size < 8) size = 26;

		if (ring is not null) ring.Texture = RingIcon ?? IconRaster.Get(IconRaster.Shape.Ring, size);
		if (pointer is not null) pointer.Texture = PointerIcon ?? IconRaster.Get(IconRaster.Shape.Pointer, size);

		if (progress is not null)
		{
			progress.TextureProgress = ProgressIcon ?? IconRaster.Get(IconRaster.Shape.Ring, size);
			progress.FillMode = (int)TextureProgressBar.FillModeEnum.Clockwise;
			progress.MinValue = 0;
			progress.MaxValue = 360;
			progress.RadialInitialAngle = 90f;
		}

		Refresh();
	}

	public void Display(double? value)
	{
		degrees = value;
		if (!dragging) Refresh();
	}

	void Refresh()
	{
		bool known = degrees is double;
		double d = degrees ?? 0d;

		if (pointer is not null)
		{
			pointer.Visible = known;
			pointer.PivotOffset = pointer.Size / 2f;
			pointer.Rotation = Mathf.DegToRad((float)d);
		}

		if (progress is not null)
		{
			progress.Visible = known;
			double within = d % 360d;
			if (within < 0d) within += 360d;
			progress.Value = within;
		}

		if (turns is not null)
		{
			int whole = (int)Math.Truncate(d / 360d);
			turns.Visible = known && whole != 0;
			turns.Text = whole > 0 ? $"+{whole}" : whole.ToString();
		}
	}

	public override void _Notification(int what)
	{
		if (what == NotificationResized && pointer is not null) pointer.PivotOffset = pointer.Size / 2f;
	}

	// ---- turning ----

	public override void _GuiInput(InputEvent @event)
	{
		if (ReadOnly) return;

		// outside the app there is no input manager: read the events directly
		if (InputManager.Singleton is null) { RawInput(@event); return; }

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

	bool rawPressed;

	void RawInput(InputEvent @event)
	{
		switch (@event)
		{
			case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
				if (button.Pressed)
				{
					rawPressed = true;
					dragging = true;
					dragValue = degrees ?? 0d;
					last = GetGlobalMousePosition();
					DragBegan?.Invoke();
				}
				else if (rawPressed)
				{
					rawPressed = false;
					FinishDrag();
				}
				AcceptEvent();
				break;

			case InputEventMouseMotion motion when rawPressed && dragging:
				Drag(GetGlobalMousePosition(), motion.CtrlPressed, motion.ShiftPressed);
				AcceptEvent();
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
		Refresh();
	}

	void FinishDrag()
	{
		if (!dragging) return;

		dragging = false;
		Committed?.Invoke(dragValue);
		DragEnded?.Invoke();
		Refresh();
	}

	public void CancelDrag(MouseButtonState button) => FinishDrag();
}

using EditSharpGUI.Scripts.Input;
using Godot;
using System;

namespace EditSharpGUI.Scripts.UI.Inspecting;

// a number the way godot's inspector shows one: drag across it to scrub
// the value by its step - a tenth of that with ctrl, ten times with shift -
// click to type one in, and a fill along the bottom shows where it sits in
// its range when it has one. it is a button, so its normal, hover, pressed,
// focus and disabled looks are the theme's; the value and unit are its
// text, the fill a ColorRect child, the typing field a LineEdit child -
// SpinSlider.tscn. it works in model units and shows scaled ones, so a
// fraction can read as a percentage
[Tool]
public partial class SpinSlider : Button
{
	public double? Min;
	public double? Max;
	public double Step = 0.01d;
	public double Multiplier = 1d;
	public string Unit;
	public int Decimals = 3;

	public bool ReadOnly
	{
		get;
		set { field = value; Disabled = value; MouseDefaultCursorShape = value ? CursorShape.Arrow : CursorShape.Hsize; }
	}

	public event Action DragBegan;
	public event Action<double> Changed;
	public event Action<double> Committed;
	public event Action DragEnded;

	[Export] LineEdit entry;
	[Export] ColorRect fill;

	double? value;

	bool dragging;
	double dragValue;
	float lastX;

	public bool IsEditing => entry is not null && entry.Visible;

	public override void _Ready()
	{
		MouseDefaultCursorShape = ReadOnly ? CursorShape.Arrow : CursorShape.Hsize;
		Alignment = HorizontalAlignment.Left;
		ClipText = true;

		entry.Visible = false;
		Callable submitted = new(this, MethodName.OnEntrySubmitted);
		if (!entry.IsConnected(LineEdit.SignalName.TextSubmitted, submitted)) entry.Connect(LineEdit.SignalName.TextSubmitted, submitted);
		Callable left = new(this, MethodName.OnEntryFocusExited);
		if (!entry.IsConnected(Control.SignalName.FocusExited, left)) entry.Connect(Control.SignalName.FocusExited, left);

		Refresh();
	}

	public override void _Notification(int what)
	{
		if (what == NotificationThemeChanged && IsNodeReady()) Refresh();
	}

	public void Display(double? model)
	{
		value = model;
		if (!dragging) Refresh();
	}

	// ---- showing ----

	void Refresh()
	{
		if (fill is not null)
		{
			fill.Color = HasThemeColor("range_fill", "SpinSlider") ? GetThemeColor("range_fill", "SpinSlider") : new Color(1f, 1f, 1f, 0.2f);

			bool ranged = value is double && Min is double min && Max is double max && max > min;
			fill.Visible = ranged && !IsEditing;

			if (ranged)
			{
				float fraction = (float)Math.Clamp((value.Value - Min.Value) / (Max.Value - Min.Value), 0d, 1d);
				fill.AnchorRight = fraction;
			}
		}

		Text = value is double shown ? ValueEditor.FormatNumber(shown * Multiplier, Decimals) + (string.IsNullOrEmpty(Unit) ? "" : " " + Unit) : "—";
	}

	// ---- dragging and clicking ----

	public override void _GuiInput(InputEvent @event)
	{
		if (ReadOnly || IsEditing) return;

		// outside the app - in the editor's theme tabs - there is no input
		// manager, so the mouse is read straight off the events
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
				dragValue = value ?? Min ?? 0d;
				lastX = mouse.CurrentPosition.X;
				DragBegan?.Invoke();
				goto case MouseAction.DragMove;

			case MouseAction.DragMove:
				if (!left.HasCapture(this) || !dragging) break;
				Drag(mouse.CurrentPosition.X, Godot.Input.IsKeyPressed(Key.Ctrl), Godot.Input.IsKeyPressed(Key.Shift));
				break;

			case MouseAction.DragEnd:
				if (!left.HasCapture(this)) break;
				FinishDrag();
				break;

			case MouseAction.Click:
				if (!left.HasCapture(this)) break;
				StartTyping();
				break;
		}
	}

	bool rawPressed;
	Vector2 rawStart;

	void RawInput(InputEvent @event)
	{
		switch (@event)
		{
			case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
				if (button.Pressed)
				{
					rawPressed = true;
					rawStart = GetGlobalMousePosition();
				}
				else if (rawPressed)
				{
					rawPressed = false;
					if (dragging) FinishDrag();
					else StartTyping();
				}
				AcceptEvent();
				break;

			case InputEventMouseMotion motion when rawPressed:
			{
				Vector2 at = GetGlobalMousePosition();

				if (!dragging)
				{
					if (at.DistanceTo(rawStart) < 3f) break;
					dragging = true;
					dragValue = value ?? Min ?? 0d;
					lastX = at.X;
					DragBegan?.Invoke();
				}

				Drag(at.X, motion.CtrlPressed, motion.ShiftPressed);
				AcceptEvent();
				break;
			}
		}
	}

	void Drag(float x, bool fine, bool coarse)
	{
		float dx = x - lastX;
		lastX = x;

		double step = Step > 0d ? Step : 0.01d;
		double amount = dx * step * (fine ? 0.1d : coarse ? 10d : 1d);

		dragValue = Clamp(Snap(dragValue + amount, fine ? step * 0.1d : step));
		value = dragValue;

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

	double Clamp(double v)
	{
		if (Min is double min && v < min) v = min;
		if (Max is double max && v > max) v = max;
		return v;
	}

	static double Snap(double v, double step) => step > 0d ? Math.Round(v / step) * step : v;

	// ---- typing ----

	void StartTyping()
	{
		entry.Text = value is double v ? ValueEditor.FormatNumber(v * Multiplier, Decimals) : "";
		entry.Visible = true;
		entry.GrabFocus();
		entry.SelectAll();
		if (fill is not null) fill.Visible = false;
	}

	void OnEntrySubmitted(string text)
	{
		CommitText();
		entry.ReleaseFocus();
	}

	void OnEntryFocusExited()
	{
		CommitText();
		StopTyping();
	}

	void StopTyping()
	{
		entry.Visible = false;
		Refresh();
	}

	bool committing;

	void CommitText()
	{
		if (committing || !entry.Visible) return;
		committing = true;

		try
		{
			if (ValueEditor.TryParseNumber(entry.Text, out double typed))
			{
				double model = Clamp(typed / Multiplier);

				if (value != model)
				{
					value = model;
					Committed?.Invoke(model);
				}
			}
		}
		finally
		{
			committing = false;
		}
	}
}

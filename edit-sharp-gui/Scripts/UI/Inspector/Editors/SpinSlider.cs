using EditSharpGUI.Scripts.Input;
using Godot;
using System;

namespace EditSharpGUI.Scripts.UI.Inspecting;

// a number the way godot's inspector shows one: drag across it to scrub
// the value by its step - a tenth of that with ctrl, ten times with shift -
// click to type one in, and a fill along the bottom shows where it sits in
// its range when it has one. it works in model units and shows scaled
// ones, so a fraction can read as a percentage. SpinSlider.tscn holds the
// text entry it shows while typing
public partial class SpinSlider : Control, IDragCancellable
{
	public double? Min;
	public double? Max;
	public double Step = 0.01d;
	public double Scale = 1d;
	public string Unit;
	public int Decimals = 3;

	public bool ReadOnly
	{
		get;
		set { field = value; MouseDefaultCursorShape = value ? CursorShape.Arrow : CursorShape.Hsize; }
	}

	public event Action DragBegan;
	public event Action<double> Changed;
	public event Action<double> Committed;
	public event Action DragEnded;

	[Export] LineEdit entry;

	double? value;
	bool hovered;

	bool dragging;
	double dragValue;
	float lastX;

	public bool IsEditing => entry is not null && entry.Visible;

	public override void _Ready()
	{
		MouseDefaultCursorShape = ReadOnly ? CursorShape.Arrow : CursorShape.Hsize;

		entry.Visible = false;
		entry.TextSubmitted += _ => { CommitText(); entry.ReleaseFocus(); };
		entry.FocusExited += () => { CommitText(); StopTyping(); };
	}

	public void Display(double? model)
	{
		value = model;
		if (!dragging) QueueRedraw();
	}

	// ---- drawing ----

	public override void _Notification(int what)
	{
		if (what == NotificationMouseEnter) { hovered = true; QueueRedraw(); }
		else if (what == NotificationMouseExit) { hovered = false; QueueRedraw(); }
	}

	public override void _Draw()
	{
		Rect2 rect = new(Vector2.Zero, Size);
		StyleBox box = GetThemeStylebox(IsEditing ? "focus" : hovered && !ReadOnly ? "hover" : "normal", "SpinSlider");
		DrawStyleBox(box, rect);

		if (IsEditing) return;

		// the type's own font and size when the theme binds them - the mono
		// font for values, say - else the theme's default
		Font font = HasThemeFont("font", "SpinSlider") ? GetThemeFont("font", "SpinSlider") : GetThemeDefaultFont();
		int fontSize = HasThemeFontSize("font_size", "SpinSlider") ? GetThemeFontSize("font_size", "SpinSlider") : GetThemeDefaultFontSize();
		float left = box.GetMargin(Side.Left);
		float right = Size.X - box.GetMargin(Side.Right);
		float baseline = (Size.Y + font.GetAscent(fontSize) - font.GetDescent(fontSize)) / 2f;

		if (value is null)
		{
			DrawString(font, new Vector2(left, baseline), "—", HorizontalAlignment.Left, -1f, fontSize, GetThemeColor("mixed", "SpinSlider"));
			return;
		}

		double v = value.Value;

		if (Min is double min && Max is double max && max > min)
		{
			float fraction = (float)Math.Clamp((v - min) / (max - min), 0d, 1d);
			float inset = box.GetMargin(Side.Left) * 0.5f;
			DrawRect(new Rect2(inset, Size.Y - 3f - inset, (Size.X - inset * 2f) * fraction, 3f), GetThemeColor("range_fill", "SpinSlider"));
		}

		string text = ValueEditor.FormatNumber(v * Scale, Decimals);
		Color fontColor = GetThemeColor("font", "SpinSlider");

		DrawString(font, new Vector2(left, baseline), text, HorizontalAlignment.Left, right - left, fontSize, fontColor);

		if (!string.IsNullOrEmpty(Unit))
		{
			float used = font.GetStringSize(text, HorizontalAlignment.Left, -1f, fontSize).X;
			DrawString(font, new Vector2(left + used + 3f, baseline), Unit, HorizontalAlignment.Left, right - left - used, fontSize, GetThemeColor("unit", "SpinSlider"));
		}
	}

	// ---- dragging and clicking ----

	public override void _GuiInput(InputEvent _)
	{
		if (ReadOnly || IsEditing) return;

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

	void Drag(float x, bool fine, bool coarse)
	{
		float dx = x - lastX;
		lastX = x;

		double step = Step > 0d ? Step : 0.01d;
		double amount = dx * step * (fine ? 0.1d : coarse ? 10d : 1d);

		dragValue = Clamp(Snap(dragValue + amount, fine ? step * 0.1d : step));
		value = dragValue;

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

	// the drag lost its release: keep what it reached and close it out
	public void CancelDrag(MouseButtonState button) => FinishDrag();

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
		entry.Text = value is double v ? ValueEditor.FormatNumber(v * Scale, Decimals) : "";
		entry.Visible = true;
		entry.GrabFocus();
		entry.SelectAll();
		QueueRedraw();
	}

	void StopTyping()
	{
		entry.Visible = false;
		QueueRedraw();
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
				double model = Clamp(typed / Scale);

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

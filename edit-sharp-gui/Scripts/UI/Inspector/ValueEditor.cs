using EditSharp.Editing;
using Godot;
using System;
using System.Globalization;

namespace EditSharpGUI.Scripts.UI.Inspecting;

// the control a row edits its value with. it shows a value - or a blank,
// when the objects it stands for disagree - and reports the user's changes:
// a change in progress (a drag, a colour being picked) between EditBegan and
// EditEnded, and the value they settle on. the row turns those into writes
// and history entries; the editor knows nothing of what it is editing.
// each kind is a scene under Scenes/Inspector/Editors, chosen by the
// inspector for the property's editor, and its script wires the scene's
// controls in Build
[Tool]
public abstract partial class ValueEditor : HBoxContainer
{
	public event Action EditBegan;
	public event Action<object> ValueChanged;
	public event Action<object> ValueCommitted;
	public event Action EditEnded;

	public EditorSpec Spec { get; private set; }
	public Inspector Inspector { get; private set; }

	public bool ReadOnly => Spec.ReadOnly;

	// true while the user is typing into it, so a refresh from outside
	// does not overwrite what they have typed
	public virtual bool IsEditing => false;

	// show a value; mixed means the objects disagree and nothing is shown
	public abstract void Display(object value, bool mixed);

	protected void RaiseBegan() => EditBegan?.Invoke();
	protected void RaiseChanged(object value) => ValueChanged?.Invoke(value);
	protected void RaiseCommitted(object value) => ValueCommitted?.Invoke(value);
	protected void RaiseEnded() => EditEnded?.Invoke();

	// wire the scene's controls. Spec and Inspector are set by then, and
	// the scene's nodes are there, though the editor is not yet in the tree
	protected abstract void Build();

	public static ValueEditor Create(EditorSpec spec, Inspector inspector)
	{
		PackedScene scene = inspector.EditorSceneFor(spec.Editor);

		if (scene?.Instantiate() is not ValueEditor editor)
		{
			GD.PushWarning($"No editor scene for {spec.Editor}; showing the value as text.");
			editor = new LabelEditorFallback();
		}

		editor.Spec = spec;
		editor.Inspector = inspector;
		editor.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		editor.Build();

		return editor;
	}

	// what stands in when a scene is missing: a label made on the spot
	sealed partial class LabelEditorFallback : ValueEditor
	{
		Label label;

		protected override void Build()
		{
			label = new Label { ThemeTypeVariation = "InspectorValue", SizeFlagsHorizontal = SizeFlags.ExpandFill };
			AddChild(label);
		}

		public override void Display(object value, bool mixed) => label.Text = mixed ? "—" : value?.ToString() ?? "(none)";
	}

	// ---- shared helpers ----

	// ---- frame-relative values in pixels ----

	protected bool ShowingPixels => Spec.Frame != FrameMeasure.None && (Inspector?.ShowPixels ?? false);

	// pixels per stored unit along each axis
	protected (double X, double Y) PixelScale
	{
		get
		{
			Godot.Vector2I size = Inspector?.FrameSize ?? new(1920, 1080);

			return Spec.Frame switch
			{
				FrameMeasure.Width => (size.X, size.X),
				FrameMeasure.Frame => (size.X, size.Y),
				FrameMeasure.HalfFrame => (size.X / 2d, size.Y / 2d),
				_ => (1d, 1d),
			};
		}
	}

	// the button that flips every frame-relative value between pixels and
	// fractions, shown only on those
	protected void WireFrameToggle(Button toggle)
	{
		if (toggle is null) return;

		toggle.Visible = Spec.Frame != FrameMeasure.None;
		toggle.Pressed += () => { if (Inspector is not null) Inspector.ShowPixels = !Inspector.ShowPixels; };
	}

	// a spin slider as pixels (whole ones, stepping by one) or as it was built
	protected void ShowFrameUnits(SpinSlider spin, double scale, (double Multiplier, double Step, int Decimals, string Unit) fraction, Button toggle)
	{
		bool pixels = ShowingPixels;

		spin.Multiplier = pixels ? scale : fraction.Multiplier;
		spin.Step = pixels ? 1d / scale : fraction.Step;
		spin.Decimals = pixels ? 0 : fraction.Decimals;
		spin.Unit = pixels ? "px" : fraction.Unit;

		if (toggle is not null) toggle.Text = pixels ? "px" : "fr";
	}

	public static string FormatNumber(double value, int decimals) => value.ToString("F" + decimals, CultureInfo.InvariantCulture);

	public static bool TryParseNumber(string text, out double value)
	{
		text = text?.Trim().TrimEnd('%', '°').Trim() ?? "";
		return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
	}

	public static string FormatTime(TimeSpan t)
	{
		string sign = t < TimeSpan.Zero ? "-" : "";
		t = t.Duration();

		return t.Hours > 0
			? $"{sign}{t.Hours}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds:000}"
			: $"{sign}{t.Minutes}:{t.Seconds:00}.{t.Milliseconds:000}";
	}

	// "12.5", "1:02.5", "0:01:02.500" - seconds, minutes and seconds, or all three
	public static bool TryParseTime(string text, out TimeSpan value)
	{
		value = TimeSpan.Zero;
		if (string.IsNullOrWhiteSpace(text)) return false;

		text = text.Trim();
		bool negative = text.StartsWith('-');
		if (negative) text = text[1..];

		string[] parts = text.Split(':');
		if (parts.Length > 3) return false;

		double total = 0d;
		double scale = 1d;

		for (int i = parts.Length - 1; i >= 0; i--)
		{
			if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double part)) return false;
			total += part * scale;
			scale *= 60d;
		}

		value = TimeSpan.FromSeconds(negative ? -total : total);
		return true;
	}
}

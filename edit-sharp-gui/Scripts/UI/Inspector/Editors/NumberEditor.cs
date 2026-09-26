using Godot;
using System;
using Vector2 = System.Numerics.Vector2;

namespace EditSharpGUI.Scripts.UI.Inspecting;

// ---- a number, in a spin slider (Number.tscn) ----

[Tool]
public partial class NumberEditor : ValueEditor
{
	[Export] SpinSlider spin;
	[Export] Button frameToggle;

	(double Multiplier, double Step, int Decimals, string Unit) fraction;

	protected override void Build()
	{
		bool percent = Spec.Editor == EditSharp.Editing.PropertyEditor.Percent;

		spin.Min = Spec.Min;
		spin.Max = Spec.Max;
		spin.Step = Spec.Step ?? (Spec.ValueType == typeof(int) || Spec.ValueType == typeof(long) ? 1d : 0.01d);
		spin.Multiplier = percent ? 100d : 1d;
		spin.Unit = percent ? "%" : Spec.Unit;
		spin.Decimals = percent ? Math.Max(0, Spec.Decimals - 2) : Spec.Decimals;
		spin.ReadOnly = ReadOnly;
		spin.TooltipText = Spec.Tooltip ?? "";

		fraction = (spin.Multiplier, spin.Step, spin.Decimals, spin.Unit);
		WireFrameToggle(frameToggle);

		spin.DragBegan += RaiseBegan;
		spin.Changed += v => RaiseChanged(Spec.Coerce(v));
		spin.Committed += v => RaiseCommitted(Spec.Coerce(v));
		spin.DragEnded += RaiseEnded;
	}

	public override bool IsEditing => spin.IsEditing;

	public override void Display(object value, bool mixed)
	{
		if (Spec.Frame != EditSharp.Editing.FrameMeasure.None) ShowFrameUnits(spin, PixelScale.X, fraction, frameToggle);
		spin.Display(mixed || value is null ? null : value is Rational r ? r.Value : Convert.ToDouble(value));
	}
}

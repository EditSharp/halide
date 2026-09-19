using Godot;
using System;
using Vector2 = System.Numerics.Vector2;

namespace EditSharpGUI.Scripts.UI.Inspecting;

// ---- an angle: a knob to turn, and the degrees beside it (Angle.tscn) ----

public partial class AngleEditor : ValueEditor
{
	[Export] AngleKnob knob;
	[Export] SpinSlider spin;

	protected override void Build()
	{
		knob.ReadOnly = ReadOnly;
		knob.TooltipText = Spec.Tooltip ?? "";
		knob.DragBegan += RaiseBegan;
		knob.Changed += v => { spin.Display(v); RaiseChanged(Spec.Coerce(v)); };
		knob.Committed += v => { spin.Display(v); RaiseCommitted(Spec.Coerce(v)); };
		knob.DragEnded += RaiseEnded;

		spin.Min = Spec.Min;
		spin.Max = Spec.Max;
		spin.Step = Spec.Step ?? 1d;
		spin.Unit = "°";
		spin.Decimals = Spec.Step is null ? 1 : Spec.Decimals;
		spin.ReadOnly = ReadOnly;
		spin.DragBegan += RaiseBegan;
		spin.Changed += v => { knob.Display(v); RaiseChanged(Spec.Coerce(v)); };
		spin.Committed += v => { knob.Display(v); RaiseCommitted(Spec.Coerce(v)); };
		spin.DragEnded += RaiseEnded;
	}

	public override bool IsEditing => spin.IsEditing;

	public override void Display(object value, bool mixed)
	{
		double? degrees = mixed || value is null ? null : Convert.ToDouble(value);
		knob.Display(degrees);
		spin.Display(degrees);
	}
}

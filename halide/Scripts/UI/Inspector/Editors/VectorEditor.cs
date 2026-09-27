using Godot;
using System;
using Vector2 = System.Numerics.Vector2;

namespace Halide.Scripts.UI.Inspecting;

// ---- a vector: x and y side by side, linkable (Vector.tscn) ----

[Tool]
public partial class VectorEditor : ValueEditor
{
	[Export] SpinSlider x;
	[Export] SpinSlider y;

	// with the link on, moving one axis moves the other by the same
	// factor, so the pair keeps its ratio - uniform scaling
	[Export] Button link;
	[Export] Button frameToggle;

	(double Multiplier, double Step, int Decimals, string Unit) fraction;

	Vector2 current;

	protected override void Build()
	{
		Axis(x);
		Axis(y);

		link.TooltipText = "Keep the ratio between x and y";
		link.Disabled = ReadOnly;

		fraction = (x.Multiplier, x.Step, x.Decimals, x.Unit);
		WireFrameToggle(frameToggle);
	}

	void Axis(SpinSlider spin)
	{
		spin.Min = Spec.Min;
		spin.Max = Spec.Max;
		spin.Step = Spec.Step ?? 0.001d;
		spin.Decimals = Spec.Step is null ? 3 : Spec.Decimals;
		spin.ReadOnly = ReadOnly;
		spin.TooltipText = Spec.Tooltip ?? "";

		spin.DragBegan += RaiseBegan;
		spin.Changed += v => RaiseChanged(Compose(spin, v));
		spin.Committed += v => RaiseCommitted(Compose(spin, v));
		spin.DragEnded += RaiseEnded;
	}

	bool Linked => link is not null && link.ButtonPressed;

	Vector2 Compose(SpinSlider which, double value)
	{
		float v = (float)value;

		if (which == x)
		{
			float other = Linked ? Follow(current.X, current.Y, v) : current.Y;
			current = new Vector2(v, other);
			if (Linked) y.Display(other);
		}
		else
		{
			float other = Linked ? Follow(current.Y, current.X, v) : current.X;
			current = new Vector2(other, v);
			if (Linked) x.Display(other);
		}

		return current;
	}

	// the other axis after this one went from `was` to `now`: scaled by the
	// same factor, or matched when there was nothing to scale from
	static float Follow(float was, float other, float now)
		=> Math.Abs(was) < 1e-6f ? now : other * (now / was);

	public override bool IsEditing => x.IsEditing || y.IsEditing;

	public override void Display(object value, bool mixed)
	{
		if (Spec.Frame != EditSharp.Editing.FrameMeasure.None)
		{
			ShowFrameUnits(x, PixelScale.X, fraction, frameToggle);
			ShowFrameUnits(y, PixelScale.Y, fraction, frameToggle);
		}

		if (mixed || value is not Vector2 v)
		{
			x.Display(null);
			y.Display(null);
			return;
		}

		current = v;
		x.Display(v.X);
		y.Display(v.Y);
	}
}

using Godot;
using System;
using System.Linq;

namespace Halide.Scripts.UI.Inspecting;

// ---- a check box (Toggle.tscn) ----

[Tool]
public partial class ToggleEditor : ValueEditor
{
	[Export] CheckBox box;

	protected override void Build()
	{
		box.Disabled = ReadOnly;
		box.TooltipText = Spec.Tooltip ?? "";
		box.Toggled += on => RaiseCommitted(on);
	}

	public override void Display(object value, bool mixed)
	{
		box.SetPressedNoSignal(!mixed && value is true);
		box.SelfModulate = mixed ? new Color(1f, 1f, 1f, 0.4f) : Colors.White;
	}
}

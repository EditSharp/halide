using Godot;
using System;

namespace Halide.Scripts.UI.Inspecting;

// something to do to the objects shown rather than a value of theirs - Reverse
// Clip - as a button across the row. it greys out while it can't be done
public partial class ActionRow : Button
{
	Action run;
	Func<bool> enabled;

	public void Configure(string label, Action run, Func<bool> enabled)
	{
		Text = label;
		ThemeTypeVariation = "InspectorButton";
		SizeFlagsHorizontal = SizeFlags.ExpandFill;
		FocusMode = FocusModeEnum.None;

		this.run = run;
		this.enabled = enabled;
		Refresh();
	}

	public override void _Ready() => Pressed += () => run?.Invoke();

	public void Refresh() => Disabled = enabled is not null && !enabled();
}

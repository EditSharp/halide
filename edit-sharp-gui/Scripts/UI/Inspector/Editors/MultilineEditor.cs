using Godot;
using System;
using System.Linq;

namespace EditSharpGUI.Scripts.UI.Inspecting;

// ---- several lines, committed on leaving (Multiline.tscn) ----

[Tool]
public partial class MultilineEditor : ValueEditor
{
	[Export] TextEdit entry;
	string shown = "";

	public override bool IsEditing => entry.HasFocus();

	protected override void Build()
	{
		entry.Editable = !ReadOnly;
		entry.TooltipText = Spec.Tooltip ?? "";

		entry.FocusExited += () =>
		{
			if (ReadOnly || entry.Text == shown) return;
			shown = entry.Text;
			RaiseCommitted(entry.Text);
		};
	}

	public override void Display(object value, bool mixed)
	{
		shown = mixed ? "" : value?.ToString() ?? "";
		entry.PlaceholderText = mixed ? "—" : "";

		if (!IsEditing) entry.Text = shown;
	}
}

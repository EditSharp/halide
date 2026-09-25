using Godot;
using System;
using System.Linq;

namespace EditSharpGUI.Scripts.UI.Inspecting;

// ---- several lines, written as they're typed and committed on leaving (Multiline.tscn) ----

[Tool]
public partial class MultilineEditor : ValueEditor
{
	[Export] TextEdit entry;
	string shown = "";

	// between the first keystroke and leaving: one edit, one history entry
	bool typing;

	public override bool IsEditing => entry.HasFocus();

	protected override void Build()
	{
		entry.Editable = !ReadOnly;
		entry.TooltipText = Spec.Tooltip ?? "";

		entry.TextChanged += () =>
		{
			if (ReadOnly || !entry.HasFocus()) return;
			if (!typing) { typing = true; RaiseBegan(); }
			RaiseChanged(entry.Text);
		};

		entry.FocusExited += () =>
		{
			if (!typing) return;
			typing = false;
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

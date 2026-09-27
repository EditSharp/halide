using Godot;
using System;
using System.Linq;

namespace Halide.Scripts.UI.Inspecting;

// ---- one line of text, committed on enter or on leaving (Text.tscn) ----

[Tool]
public partial class TextEditor : ValueEditor
{
	[Export] protected LineEdit entry;
	string shown = "";

	public override bool IsEditing => entry.HasFocus();

	protected override void Build()
	{
		entry.Editable = !ReadOnly;
		entry.TooltipText = Spec.Tooltip ?? "";
		entry.TextSubmitted += _ => { Commit(); entry.ReleaseFocus(); };
		entry.FocusExited += Commit;
	}

	void Commit()
	{
		if (ReadOnly || entry.Text == shown) return;

		shown = entry.Text;
		OnText(entry.Text);
	}

	// what a submitted text becomes. plain text by default
	protected virtual void OnText(string text) => RaiseCommitted(text);

	public override void Display(object value, bool mixed)
	{
		shown = mixed ? "" : value?.ToString() ?? "";
		entry.PlaceholderText = mixed ? "—" : "";

		if (!IsEditing) entry.Text = shown;
	}
}

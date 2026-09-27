using System.Collections.Generic;
using System.Linq;

namespace EditSharpGUI.Scripts.UI.Dialogs.Platform;

// a dialog as plain values, made on godot's thread and handed to a native
// dialog on another thread or in another process; it never touches the resources
public sealed record DialogSnapshot(string Title, IReadOnlyList<ElementSnapshot> Elements, IReadOnlyList<ButtonSnapshot> Buttons, string Problem)
{
	public static DialogSnapshot Of(Dialog dialog) => new(
		dialog.Title,
		[.. dialog.Elements.Select((e, i) => ElementSnapshot.Of(e, i))],
		[.. dialog.Buttons.Select((b, i) => new ButtonSnapshot(i, b.Id, b.Text, b.Role))],
		dialog.Problem());
}

public sealed record ButtonSnapshot(int Index, string Id, string Text, DialogButtonRole Role);

public sealed record RowSnapshot(int Index, string Id, string Text, string Detail, string ButtonText, bool ButtonEnabled);

// one element: its kind and every value a native dialog shows
public sealed record ElementSnapshot(
	int Index, string Kind, string Id, string Label, bool Visible, bool Enabled,
	string Text, int TextStyle, string Value, string Placeholder,
	IReadOnlyList<string> Options, int Selected,
	double Number, double Min, double Max, double Step, string Suffix,
	bool Checked, IReadOnlyList<RowSnapshot> Rows, IReadOnlyList<ButtonSnapshot> ListButtons)
{
	public static ElementSnapshot Of(DialogElement e, int index) => e switch
	{
		DialogText t => Base(e, index, "text") with { Text = t.Text, TextStyle = (int)t.Style },
		DialogTextField f => Base(e, index, "field") with { Value = f.Value, Placeholder = f.Placeholder },
		DialogPathField p => Base(e, index, "path") with { Value = p.Value },
		DialogDropdown d => Base(e, index, "dropdown") with { Options = [.. d.Options], Selected = d.Selected },
		DialogNumber n => Base(e, index, "number") with { Number = n.Value, Min = n.Min, Max = n.Max, Step = n.Step, Suffix = n.Suffix },
		DialogCheckbox c => Base(e, index, "checkbox") with { Text = c.Text, Checked = c.Checked },
		DialogList l => Base(e, index, "list") with
		{
			Rows = [.. l.Rows.Select((r, i) => new RowSnapshot(i, r.Id, r.Text, r.Detail, r.ButtonText, r.ButtonEnabled))],
			ListButtons = [.. l.Buttons.Select((b, i) => new ButtonSnapshot(i, b.Id, b.Text, b.Role))],
		},
		_ => Base(e, index, "unknown"),
	};

	static ElementSnapshot Base(DialogElement e, int index, string kind) =>
		new(index, kind, e.Id, e.Label, e.Visible, e.Enabled, "", 0, "", "", [], 0, 0d, 0d, 0d, 1d, "", false, [], []);
}

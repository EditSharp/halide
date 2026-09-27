using Godot;
using System.Collections.Generic;

namespace EditSharpGUI.Scripts.UI.Dialogs;

// how a dialog was closed, and what its elements held then
public sealed class DialogResult(string button, IReadOnlyDictionary<string, Variant> values)
{
	// the Id of the button that closed it; the Cancel button's when it was dismissed
	public string Button { get; } = button;

	public bool Is(string id) => Button == id;

	public string Text(string id) => values.TryGetValue(id, out Variant v) ? v.AsString() : "";
	public bool Checked(string id) => values.TryGetValue(id, out Variant v) && v.AsBool();
	public double Number(string id) => values.TryGetValue(id, out Variant v) ? v.AsDouble() : 0d;
	public int Selected(string id) => values.TryGetValue(id, out Variant v) ? v.AsInt32() : -1;
}

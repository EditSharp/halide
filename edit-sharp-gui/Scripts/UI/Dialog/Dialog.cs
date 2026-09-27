using Godot;
using Godot.Collections;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EditSharpGUI.Scripts.UI.Dialogs;

// a dialog: title, elements and buttons, shown with Dialogs.Show; it follows its elements while open
[GlobalClass]
public partial class Dialog : Resource
{
	[Export] public string Title = "";
	[Export] public Array<DialogElement> Elements = [];
	[Export] public Array<DialogButton> Buttons = [];

	// the problem with the current answers, or null; while set, Default buttons are disabled
	public Func<Dialog, string> Validate;

	// an element changed, by code or by the user; a showing dialog listens
	public new event Action<DialogElement> Changed;

	// any value the user changed, after the element has taken it
	public event Action<DialogElement> Edited;

	// the element with this Id, or null
	public T Find<T>(string id) where T : DialogElement => Elements.OfType<T>().FirstOrDefault(e => e.Id == id);

	public string Problem() => Validate?.Invoke(this);

	public DialogButton Default => Buttons.FirstOrDefault(b => b.Role == DialogButtonRole.Default);
	public DialogButton Cancel => Buttons.FirstOrDefault(b => b.Role == DialogButtonRole.Cancel);

	// wires the elements up for one showing; Release lets go again
	internal void Attach()
	{
		foreach (DialogElement element in Elements) element.Changed += OnChanged;
	}

	internal void Release()
	{
		foreach (DialogElement element in Elements) element.Changed -= OnChanged;
	}

	void OnChanged(DialogElement element) => Changed?.Invoke(element);

	// the user changed an element: it takes the value quietly, then whoever cares hears
	internal void UserEdited(DialogElement element, Variant value)
	{
		element.Changed -= OnChanged;
		element.SetFromUser(value);
		element.Changed += OnChanged;
		Edited?.Invoke(element);
	}

	// every value, by element Id, for the result
	internal IReadOnlyDictionary<string, Variant> Values()
	{
		Godot.Collections.Dictionary<string, Variant> values = [];
		foreach (DialogElement element in Elements)
		{
			Variant? value = element switch
			{
				DialogTextField t => t.Value,
				DialogPathField p => p.Value,
				DialogDropdown d => d.Selected,
				DialogNumber n => n.Value,
				DialogCheckbox c => c.Checked,
				_ => null,
			};

			if (value is { } v && element.Id.Length > 0) values[element.Id] = v;
		}

		return values.ToDictionary(p => p.Key, p => p.Value);
	}
}

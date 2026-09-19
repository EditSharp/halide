using Godot;
using System;
using System.Linq;

namespace EditSharpGUI.Scripts.UI.Inspecting;

// ---- one of an enum's values (Dropdown.tscn) ----

public partial class DropdownEditor : ValueEditor
{
	[Export] OptionButton options;
	Array values;

	protected override void Build()
	{
		options.Disabled = ReadOnly;
		options.TooltipText = Spec.Tooltip ?? "";
		options.Clear();

		values = Spec.ValueType.IsEnum ? Enum.GetValues(Spec.ValueType) : Array.Empty<object>();

		foreach (object value in values) options.AddItem(Humanize(value.ToString()));

		options.ItemSelected += index => { if (index >= 0 && index < values.Length) RaiseCommitted(values.GetValue(index)); };
	}

	public override void Display(object value, bool mixed)
	{
		int index = mixed || value is null || values is null ? -1 : Array.IndexOf(values.Cast<object>().ToArray(), value);

		options.Selected = index;
		if (index < 0) options.Text = "—";
	}

	// "FadeToColor" -> "Fade to color"
	public static string Humanize(string name)
	{
		if (string.IsNullOrEmpty(name)) return name;

		System.Text.StringBuilder text = new(name.Length + 4);

		for (int i = 0; i < name.Length; i++)
		{
			char c = name[i];

			if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1]))
			{
				text.Append(' ');
				text.Append(char.ToLowerInvariant(c));
			}
			else text.Append(c);
		}

		return text.ToString();
	}
}

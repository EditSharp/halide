using Halide.Scripts.UI.Dialogs;
using Godot;
using System.Threading.Tasks;

namespace Halide.Scripts.App.Layouts;

/// <summary>The questions the layout commands ask: a name for a new or renamed layout, and whether to delete one.</summary>
public static class LayoutDialogs
{
	/// <summary>A free layout name, or null when cancelled.</summary>
	public static async Task<string> AskName(Node owner, string title, string action, string initial)
	{
		Dialog dialog = new() { Title = title };
		dialog.Elements.Add(new DialogTextField { Id = "name", Label = "Name", Value = initial, Placeholder = "Layout name" });
		dialog.Buttons.Add(new DialogButton { Id = "ok", Text = action, Role = DialogButtonRole.Default });
		dialog.Buttons.Add(new DialogButton { Id = "cancel", Text = "Cancel", Role = DialogButtonRole.Cancel });
		dialog.Validate = d =>
		{
			string name = d.Find<DialogTextField>("name").Value.Trim();
			if (name.Length == 0) return "Give the layout a name.";
			if (name != initial && LayoutStore.Exists(name)) return $"There's already a layout called \"{name}\".";
			return null;
		};

		DialogResult result = await Dialogs.Show(dialog, owner);
		return result.Is("ok") ? result.Text("name").Trim() : null;
	}

	public static async Task<bool> ConfirmDelete(Node owner, string name)
	{
		Dialog dialog = Dialogs.Question("Delete Layout", $"Delete the layout \"{name}\"?", "Windows using it switch to Editing.",
			("delete", "Delete", DialogButtonRole.Destructive), ("cancel", "Cancel", DialogButtonRole.Cancel));
		return (await Dialogs.Show(dialog, owner)).Is("delete");
	}

	/// <summary>A name like "My Layout 2" that isn't taken yet.</summary>
	public static string FreeName(string stem)
	{
		if (!LayoutStore.Exists(stem)) return stem;
		for (int i = 2; ; i++)
			if (!LayoutStore.Exists($"{stem} {i}")) return $"{stem} {i}";
	}
}

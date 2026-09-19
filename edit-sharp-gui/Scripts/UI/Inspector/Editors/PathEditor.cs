using Godot;
using System;
using System.Linq;

namespace EditSharpGUI.Scripts.UI.Inspecting;

// ---- a file path, typed or browsed for (Path.tscn) ----

public partial class PathEditor : TextEditor
{
	[Export] Button browse;
	[Export] FileDialog dialog;

	protected override void Build()
	{
		base.Build();

		browse.Disabled = ReadOnly;
		browse.Pressed += Browse;
		dialog.FileSelected += path => { entry.Text = path; RaiseCommitted(path); };
	}

	void Browse()
	{
		if (!string.IsNullOrEmpty(entry.Text)) dialog.CurrentPath = entry.Text;
		dialog.PopupCentered(new Vector2I(800, 600));
	}
}

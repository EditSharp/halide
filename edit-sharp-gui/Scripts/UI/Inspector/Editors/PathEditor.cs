using EditSharpGUI.Scripts.UI.DragDrop;
using Godot;
using System;
using System.Linq;

namespace EditSharpGUI.Scripts.UI.Inspecting;

// ---- a file path, typed or browsed for (Path.tscn) ----

[Tool]
public partial class PathEditor : TextEditor, IDropTarget
{
	// a file dropped on the field becomes its path
	public bool CanDrop(DragPayload payload, Vector2 at) => !ReadOnly && payload is FilesPayload { Paths.Count: 1 };

	public void Drop(DragPayload payload, Vector2 at)
	{
		if (payload is not FilesPayload f) return;
		entry.Text = f.Paths[0];
		RaiseCommitted(f.Paths[0]);
	}

	[Export] Button browse;
	[Export] FileDialog dialog;

	protected override void Build()
	{
		base.Build();

		browse.Disabled = ReadOnly;
		browse.Pressed += Browse;
		dialog.FileSelected += path => { entry.Text = path; RaiseCommitted(path); };
		dialog.DirSelected += path => { entry.Text = path; RaiseCommitted(path); };
		if (Spec?.Folder == true) dialog.FileMode = FileDialog.FileModeEnum.OpenDir;
	}

	void Browse()
	{
		if (!string.IsNullOrEmpty(entry.Text)) dialog.CurrentPath = entry.Text;
		dialog.PopupCentered(new Vector2I(800, 600));
	}
}

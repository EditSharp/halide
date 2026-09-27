using EditSharp.History;
using Godot;
using System;
using System.Text.Json.Nodes;

// one open project and everything that goes with it: the project itself and
// where it lives on disk. it sits in the project's window, and every view in
// that window - popups included - finds it through Of
public partial class ProjectSession : Node
{
	public const string NodeName = "ProjectSession";

	public Project Project { get; private set; }

	// the project file; null for a project that was never saved
	public string FilePath { get; set; }

	// the history entry the file matches; the project has unsaved changes
	// whenever the history is anywhere else
	int savedAt;

	public bool Dirty => Project.History.Position != savedAt;

	public void MarkSaved() => savedAt = autosavedAt = Project.History.Position;

	// what's showing isn't what the file holds - a restored autosave
	public void MarkUnsaved() => savedAt = -1;

	// ---- the editor's view of the project ----

	// what the file said about the editor, for it to restore; and whoever
	// can say what it is now, for saving - the editor, once it's built
	public JsonObject Gui { get; set; } = [];

	public Func<JsonObject> GuiProvider { get; set; }

	public JsonObject CaptureGui() => GuiProvider?.Invoke() ?? Gui;

	// ---- autosave ----

	int autosavedAt;
	ulong autosavedTicks = Godot.Time.GetTicksMsec();

	// changes the last save or autosave doesn't have, older than the interval
	public bool NeedsAutosave(int seconds) =>
		Dirty && Project.History.Position != autosavedAt && Godot.Time.GetTicksMsec() - autosavedTicks >= (ulong)seconds * 1000UL;

	public void MarkAutosaved()
	{
		autosavedAt = Project.History.Position;
		autosavedTicks = Godot.Time.GetTicksMsec();
	}

	// a session for a project, put in a node - a project's window, or a test
	public static ProjectSession Attach(Node owner, Project project, string filePath = null)
	{
		ProjectSession session = new() { Name = NodeName, Project = project, FilePath = filePath };
		session.savedAt = session.autosavedAt = project.History.Position;
		owner.AddChild(session);
		return session;
	}

	// the nearest session above a node: its window's, reached out of popups
	// and dialogs too since they sit inside the window that opened them; null
	// outside any project
	public static ProjectSession Of(Node node)
	{
		for (Node at = node; at is not null; at = at.GetParent())
			if (at.GetNodeOrNull<ProjectSession>(NodeName) is { } session) return session;

		return null;
	}

	// the history writes land in while this window is the one in use
	public void Activate() => History.Active = Project.History;
}

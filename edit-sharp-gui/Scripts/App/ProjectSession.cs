using EditSharp.History;
using Godot;
using System;
using System.Text.Json.Nodes;

// one open project and its file, kept in the project's window; views find it with Of
public partial class ProjectSession : Node
{
	public const string NodeName = "ProjectSession";

	public Project Project { get; private set; }

	// the project file; null for a project that was never saved
	public string FilePath { get; set; }

	// the history position the file matches
	int savedAt;

	public bool Dirty => Project.History.Position != savedAt;

	public void MarkSaved() => savedAt = autosavedAt = Project.History.Position;

	// marks the project unsaved, as after restoring an autosave
	public void MarkUnsaved() => savedAt = -1;

	// ---- the editor's view of the project ----

	// the editor state from the file, and the callback that captures it for saving
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

	// a session for a project, placed in a project window or a test node
	public static ProjectSession Attach(Node owner, Project project, string filePath = null)
	{
		ProjectSession session = new() { Name = NodeName, Project = project, FilePath = filePath };
		session.savedAt = session.autosavedAt = project.History.Position;
		owner.AddChild(session);
		return session;
	}

	// the session of the window a node is in, or null
	public static ProjectSession Of(Node node)
	{
		for (Node at = node; at is not null; at = at.GetParent())
			if (at.GetNodeOrNull<ProjectSession>(NodeName) is { } session) return session;

		return null;
	}

	// the history writes land in while this window is the one in use
	public void Activate() => History.Active = Project.History;
}

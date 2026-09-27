using EditSharp.History;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace EditSharpGUI.Api;

/// <summary>One open project and its window: every project call goes through one of these.</summary>
public sealed class ProjectHandle
{
	readonly ProjectWindow window;

	internal ProjectHandle(ProjectWindow window)
	{
		this.window = window;
		path = window.Session?.FilePath;
		Timeline = new TimelineService(this);
		Media = new MediaService(this);
		Playback = new PlaybackService(this);
		Layout = new LayoutService(this);
	}

	/// <summary>Whether the project is still open; a closed project's handle throws when used.</summary>
	public bool IsOpen => !closed && Godot.GodotObject.IsInstanceValid(window);

	bool closed;

	// what it was called is kept for after it's gone
	internal void Close()
	{
		if (IsOpen) _ = Name;
		closed = true;
	}

	/// <summary>The project's name: its file's, or "Untitled" before it's saved; the last one it had once it's closed.</summary>
	public string Name
	{
		get
		{
			if (IsOpen) name = FilePath is string file ? System.IO.Path.GetFileNameWithoutExtension(file) : "Untitled";
			return name;
		}
	}

	string name = "Untitled";

	/// <summary>The project file, or null before it's first saved; the last one it had once it's closed.</summary>
	public string FilePath
	{
		get
		{
			if (IsOpen) path = window.Session.FilePath;
			return path;
		}
	}

	string path;

	/// <summary>Whether it has changes the file doesn't.</summary>
	public bool Dirty => Session.Dirty;

	/// <summary>The project itself: its timelines, media and settings, for reading and editing directly.</summary>
	/// <remarks>Edits must happen inside <see cref="Batch"/> or another history transaction so they can be undone.</remarks>
	public Project Project => Session.Project;

	/// <summary>The project's undo history.</summary>
	public History History => Project.History;

	public TimelineService Timeline { get; }
	public MediaService Media { get; }
	public PlaybackService Playback { get; }
	public LayoutService Layout { get; }

	/// <summary>Everything done until the returned batch is disposed becomes one undo entry called <paramref name="name"/>.</summary>
	/// <remarks>Call <see cref="ProjectBatch.Cancel"/> to roll it all back instead. Batches nest: an inner one joins the outer entry.</remarks>
	public ProjectBatch Batch(string name) => new(History.Begin(name));

	/// <summary>Runs <paramref name="edits"/> as one undo entry, rolled back if it throws.</summary>
	public void Batch(string name, Action edits)
	{
		using ProjectBatch batch = Batch(name);
		try { edits(); }
		catch
		{
			batch.Cancel();
			throw;
		}
	}

	/// <summary>Writes the project to its file; false when it has none yet or the write failed.</summary>
	public bool Save() => ProjectManager.Singleton.Save(Session);

	/// <summary>Writes the project to <paramref name="path"/>, which becomes its file.</summary>
	public bool SaveAs(string path) => ProjectManager.Singleton.SaveTo(Session, path);

	/// <summary>Closes the window, asking about unsaved changes; true once it's closed.</summary>
	public Task<bool> CloseAsync() => Window.CloseAsync();

	/// <summary>Brings the project's window to the front.</summary>
	public void Focus()
	{
		Window.MoveToForeground();
		Window.GrabFocus();
	}

	internal ProjectWindow Window => IsOpen ? window : throw new InvalidOperationException($"'{name}' is closed.");

	internal ProjectSession Session => Window.Session;

	internal Editor Editor => Window.FindChildren("*", "", true, false).OfType<Editor>().First();

	internal EditSharpGUI.Scripts.App.Commands.CommandContext Context => new() { Window = Window, Source = Window };
}

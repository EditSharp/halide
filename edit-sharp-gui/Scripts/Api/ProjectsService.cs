using EditSharp.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EditSharpGUI.Api;

/// <summary>The open projects: opening, creating and finding them.</summary>
public sealed class ProjectsService
{
	readonly Dictionary<ProjectWindow, ProjectHandle> handles = [];
	bool watching;

	/// <summary>A project finished opening.</summary>
	public event Action<ProjectHandle> Opened;

	/// <summary>A project closed; its handle no longer works.</summary>
	public event Action<ProjectHandle> Closed;

	/// <summary>Every open project, in the order they opened.</summary>
	public IReadOnlyList<ProjectHandle> All
	{
		get
		{
			Sync();
			return [.. ProjectManager.Singleton.OpenProjects.Select(Handle)];
		}
	}

	/// <summary>The project in the focused window, or the most recently opened one; null when none are open.</summary>
	public ProjectHandle Focused
	{
		get
		{
			Sync();
			return EditSharpGUI.Scripts.App.Commands.CommandContext.Current.Window is ProjectWindow window ? Handle(window) : null;
		}
	}

	/// <summary>An open project by name or file path; null when none matches.</summary>
	public ProjectHandle Find(string nameOrPath) => All.FirstOrDefault(p =>
		string.Equals(p.Name, nameOrPath, StringComparison.OrdinalIgnoreCase) ||
		p.FilePath is string path && string.Equals(System.IO.Path.GetFullPath(path), SafeFullPath(nameOrPath), StringComparison.OrdinalIgnoreCase));

	/// <summary>Opens a project file in a window of its own, or focuses it when it's open already; null when it couldn't open.</summary>
	public async Task<ProjectHandle> OpenAsync(string path)
	{
		ProjectWindow window = await ProjectManager.Singleton.OpenProjectAsync(path, ((Godot.SceneTree)Godot.Engine.GetMainLoop()).Root);
		return window is null ? null : await Ready(window);
	}

	/// <summary>Creates a project folder named <paramref name="name"/> inside <paramref name="parentFolder"/> and opens it.</summary>
	public async Task<ProjectHandle> CreateAsync(string parentFolder, string name, RenderSettings? settings = null)
	{
		ProjectWindow window = ProjectManager.Singleton.CreateProject(parentFolder, name, settings ?? new RenderSettings());
		return window is null ? null : await Ready(window);
	}

	/// <summary>The handle for a project window.</summary>
	public ProjectHandle Handle(ProjectWindow window)
	{
		Sync();
		if (!handles.TryGetValue(window, out ProjectHandle handle)) handles[window] = handle = new ProjectHandle(window);
		return handle;
	}

	// a new window's editor is ready a frame after it's added
	async Task<ProjectHandle> Ready(ProjectWindow window)
	{
		Godot.SceneTree tree = (Godot.SceneTree)Godot.Engine.GetMainLoop();
		while (!EditorReady(window))
		{
			if (!Godot.GodotObject.IsInstanceValid(window)) return null;
			await window.ToSignal(tree, Godot.SceneTree.SignalName.ProcessFrame);
		}

		// the saved layout is restored the frame after
		await window.ToSignal(tree, Godot.SceneTree.SignalName.ProcessFrame);
		return Handle(window);
	}

	static bool EditorReady(ProjectWindow window) =>
		Godot.GodotObject.IsInstanceValid(window) && window.FindChildren("*", "", true, false).OfType<Editor>().FirstOrDefault() is Editor editor && editor.IsNodeReady();

	void Sync()
	{
		if (watching || ProjectManager.Singleton is null) return;
		watching = true;
		ProjectManager.Singleton.OpenProjectsChanged += Changed;
		Changed();
	}

	void Changed()
	{
		IReadOnlyList<ProjectWindow> open = ProjectManager.Singleton.OpenProjects;

		foreach (ProjectWindow gone in handles.Keys.Where(w => !open.Contains(w)).ToList())
		{
			ProjectHandle handle = handles[gone];
			handles.Remove(gone);
			handle.Close();
			Closed?.Invoke(handle);
		}

		foreach (ProjectWindow window in open.Where(w => !handles.ContainsKey(w)))
		{
			ProjectHandle handle = handles[window] = new ProjectHandle(window);
			Opened?.Invoke(handle);
		}
	}

	static string SafeFullPath(string path)
	{
		try { return System.IO.Path.GetFullPath(path); }
		catch (Exception) { return path; }
	}
}

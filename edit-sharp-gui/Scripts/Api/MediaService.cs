using EditSharp.Components.Media;
using EditSharp.History;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EditSharpGUI.Api;

/// <summary>A project's media library: importing, finding and removing media.</summary>
public sealed class MediaService
{
	readonly ProjectHandle project;

	internal MediaService(ProjectHandle project) => this.project = project;

	/// <summary>Every media in the project, in library order.</summary>
	public IReadOnlyList<IMedia> All => [.. project.Project.Media];

	/// <summary>A media by its file, ignoring case; null when it isn't in the library.</summary>
	public IMedia Find(string path) => All.FirstOrDefault(m => string.Equals(m.Path, path, StringComparison.OrdinalIgnoreCase));

	/// <summary>Adds files to the library as one undo entry, skipping ones already in it; returns what was added.</summary>
	/// <remarks>Proxies and analysis start in the background, as they do for files imported by hand.</remarks>
	public IReadOnlyList<IMedia> Import(IEnumerable<string> paths) => project.Editor.MediaView.Import(paths);

	/// <summary>Takes media out of the library as one undo entry; clips using them stay.</summary>
	public void Remove(IEnumerable<IMedia> media)
	{
		List<IMedia> gone = [.. media.Where(m => project.Project.Media.Holds(m))];
		if (gone.Count == 0) return;

		using (Transaction.Scope scope = project.History.Begin(gone.Count == 1 ? "Remove media" : $"Remove {gone.Count} media"))
		{
			foreach (IMedia m in gone) project.Project.Media.Remove(m);
			scope.Commit();
		}
	}

	/// <summary>Opens a media in the source viewer, docking it if it's closed.</summary>
	public void OpenInSource(IMedia media) => project.Editor.OpenSource(media);

	/// <summary>The library gained or lost media.</summary>
	public event Action Changed
	{
		add => project.Project.Media.Changed += value;
		remove => project.Project.Media.Changed -= value;
	}
}

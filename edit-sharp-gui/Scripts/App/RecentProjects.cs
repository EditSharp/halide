using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

// one project Home knows about: where its file is, and when it was last opened
public sealed record RecentProject(string Path, DateTime LastOpenedUtc)
{
	public string Folder => System.IO.Path.GetDirectoryName(Path);
	public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);
	public bool Exists => File.Exists(Path);
}

// every project opened or created, newest first, in user://projects.json.
// Changed fires on every change
public static class RecentProjects
{
	public const string DefaultPath = "user://projects.json";

	// where the list lives; a test points it somewhere of its own
	static string file = DefaultPath;

	static List<RecentProject> items = Load();

	public static void UseFile(string path)
	{
		file = path;
		items = Load();
		Changed?.Invoke();
	}

	public static IReadOnlyList<RecentProject> All => items;

	public static event Action Changed;

	// opened just now: to the front of the list
	public static void Touch(string path)
	{
		path = System.IO.Path.GetFullPath(path);
		items.RemoveAll(p => Same(p.Path, path));
		items.Insert(0, new RecentProject(path, DateTime.UtcNow));
		Save();
	}

	public static void Remove(string path)
	{
		if (items.RemoveAll(p => Same(p.Path, path)) > 0) Save();
	}

	// a project moved, renamed or located: the entry follows it
	public static void Replace(string oldPath, string newPath)
	{
		int at = items.FindIndex(p => Same(p.Path, oldPath));
		if (at < 0) { Touch(newPath); return; }

		items[at] = items[at] with { Path = System.IO.Path.GetFullPath(newPath) };
		Save();
	}

	static bool Same(string a, string b) => string.Equals(System.IO.Path.GetFullPath(a), System.IO.Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

	static List<RecentProject> Load()
	{
		string file = ProjectSettings.GlobalizePath(RecentProjects.file);

		try
		{
			if (File.Exists(file)) return JsonSerializer.Deserialize<List<RecentProject>>(File.ReadAllText(file)) ?? [];
		}
		catch (Exception e) when (e is JsonException or IOException)
		{
			GD.PushWarning($"Could not read the project list from {file}: {e.Message}");
		}

		return [];
	}

	static void Save()
	{
		string file = ProjectSettings.GlobalizePath(RecentProjects.file);

		try
		{
			Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
			File.WriteAllText(file, JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true }));
		}
		catch (IOException e)
		{
			GD.PushWarning($"Could not save the project list to {file}: {e.Message}");
		}

		Changed?.Invoke();
	}
}

using EditSharp.Components;
using EditSharp.Components.Media;
using EditSharp.History;
using EditSharp.Rendering;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

// what a project file held: the project, the editor's view of it, and
// anything that loaded only as a placeholder
public sealed record LoadedProject(Project Project, JsonObject Gui, IReadOnlyList<string> Warnings);

// a project on disk: <folder>/<Name>.esproj, JSON. the edit itself is an
// EditSharp TimelineDocument; around it go the render settings, which
// timeline is the main one and the editor's own "gui" section. media inside
// the project folder is saved relative to it, so the folder can move whole
public static class ProjectFile
{
	public const string Extension = ".esproj";
	public const int FormatVersion = 1;

	static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

	// writes the project; `folder` is what media paths are relative to, the
	// file's own folder unless this is a backup kept elsewhere
	public static void Save(Project project, string path, JsonObject gui = null, string folder = null)
	{
		folder ??= Path.GetDirectoryName(Path.GetFullPath(path));

		JsonObject document = TimelineDocument.ToJson(project.Timelines, project.Media);
		foreach (JsonObject media in (document["media"]?.AsArray() ?? []).OfType<JsonObject>()) RelativePaths(media, folder);

		JsonObject root = new()
		{
			["formatVersion"] = FormatVersion,
			["renderSettings"] = JsonSerializer.SerializeToNode(project.RenderSettings, EditSharp.Components.ComponentSerializer.Options),
			["mainTimeline"] = project.Timeline.Id,
			["document"] = document,
			["gui"] = gui?.DeepClone() ?? new JsonObject(),
		};

		// written beside and moved over, so a crash mid-save never leaves half a file
		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
		string temp = $"{path}.saving";
		File.WriteAllText(temp, root.ToJsonString(Indented));
		File.Move(temp, path, overwrite: true);
	}

	public static LoadedProject Load(string path, string folder = null)
	{
		folder ??= Path.GetDirectoryName(Path.GetFullPath(path));

		JsonObject root = JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? throw new JsonException($"'{path}' is empty.");

		int version = root["formatVersion"]?.GetValue<int>() ?? 1;
		if (version > FormatVersion) throw new NotSupportedException($"'{Path.GetFileName(path)}' was saved by a newer EditSharp (project format {version}).");

		JsonObject document = root["document"]?.AsObject() ?? new JsonObject();
		foreach (JsonObject media in (document["media"]?.AsArray() ?? []).OfType<JsonObject>()) AbsolutePaths(media, folder);

		TimelineDocumentContent content = TimelineDocument.FromJson(document);

		Project project = new();
		using (Transaction.Suppress())
		{
			if (root["renderSettings"] is JsonNode settings)
				project.RenderSettings = settings.Deserialize<RenderSettings>(EditSharp.Components.ComponentSerializer.Options);

			foreach (IMedia media in content.Media) project.Media.Add(media);
			foreach (Timeline timeline in content.Timelines) project.AddTimeline(timeline);

			Guid main = root["mainTimeline"]?.GetValue<Guid>() ?? Guid.Empty;
			project.Timeline = content.Timelines.FirstOrDefault(t => t.Id == main) ?? content.Timelines.FirstOrDefault() ?? new Timeline();
			if (!project.Timelines.Contains(project.Timeline)) project.AddTimeline(project.Timeline);
		}

		return new LoadedProject(project, root["gui"]?.AsObject() ?? new JsonObject(), content.Warnings);
	}

	// a media's paths, and any it holds (a video's audio), relative to the folder when they're inside it
	static void RelativePaths(JsonObject json, string folder)
	{
		if (json["path"]?.GetValue<string>() is { Length: > 0 } path && Path.IsPathRooted(path))
		{
			string relative = Path.GetRelativePath(folder, path);
			if (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative)) json["path"] = relative.Replace('\\', '/');
		}

		foreach ((string _, JsonNode value) in json.ToList())
			if (value is JsonObject nested) RelativePaths(nested, folder);
	}

	static void AbsolutePaths(JsonObject json, string folder)
	{
		if (json["path"]?.GetValue<string>() is { Length: > 0 } path && !Path.IsPathRooted(path))
			json["path"] = Path.GetFullPath(Path.Combine(folder, path));

		foreach ((string _, JsonNode value) in json.ToList())
			if (value is JsonObject nested) AbsolutePaths(nested, folder);
	}
}

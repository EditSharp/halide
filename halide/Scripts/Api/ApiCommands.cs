using EditSharp;
using EditSharp.Components.Clips;
using EditSharp.Components.Media;
using Halide.Scripts.App.Commands;
using Halide.Scripts.UI.Docking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Halide.Api;

/// <summary>Commands over the services, taking JSON arguments: how scripts and remote callers edit without menus or dialogs.</summary>
/// <remarks>Times are seconds; clips are named by id; media by file path.</remarks>
public static class ApiCommands
{
	public static void RegisterAll()
	{
		Project("timeline.place", "Place Media", "Places a media on the timeline. Args: `media` (file path, must be in the project), `at` (seconds), `channel` (index, default 0), `video` (default true). Returns the new clips' ids.", (p, a) =>
		{
			IMedia media = p.Media.Find(Str(a, "media")) ?? throw new CommandException($"'{Str(a, "media")}' isn't in the project's media.");
			IReadOnlyList<Clip> made = p.Timeline.Place(media, Seconds(a, "at"), Int(a, "channel", 0), Bool(a, "video", true));
			return Ids(made);
		});
		Project("timeline.move", "Move Clip", "Moves a clip. Args: `clip` (id), `start` (seconds), `channel` (index of its kind, optional).", (p, a) =>
		{
			Clip clip = ClipOf(p, Str(a, "clip"));
			EditSharp.Components.Channels.Channel channel = a?["channel"] is JsonNode c ? p.Timeline.Timeline.Channels.FirstOrDefault(ch => ch.GetType() == clip.Channel.GetType() && ch.Index == c.GetValue<int>()) : null;
			p.Timeline.Move(clip, Seconds(a, "start"), channel);
			return null;
		});
		Project("timeline.trim", "Trim Clip", "Trims a clip. Args: `clip` (id), `start` and/or `end` (seconds to move in; negative extends).", (p, a) =>
		{
			Clip clip = ClipOf(p, Str(a, "clip"));
			if (a?["start"] is not null) p.Timeline.TrimStart(clip, Seconds(a, "start"));
			if (a?["end"] is not null) p.Timeline.TrimEnd(clip, Seconds(a, "end"));
			return null;
		});
		Project("timeline.splitAt", "Split Clips At", "Cuts clips in two. Args: `at` (seconds), `clips` (ids; default every clip covering `at`). Returns the new clips' ids.", (p, a) =>
		{
			HashSet<Clip> before = [.. p.Timeline.Clips];
			p.Timeline.Split(Seconds(a, "at"), a?["clips"] is JsonArray ids ? ids.Select(i => ClipOf(p, (string)i)) : null);
			return Ids(p.Timeline.Clips.Where(c => !before.Contains(c)));
		});
		Project("timeline.deleteClips", "Delete Clips", "Removes clips. Args: `clips` (ids), `ripple` (close the gap, default false).", (p, a) =>
		{
			p.Timeline.Delete(ClipsOf(p, a), Bool(a, "ripple", false));
			return null;
		});
		Project("timeline.duplicateClips", "Duplicate Clips", "Copies clips straight after themselves. Args: `clips` (ids). Returns the copies' ids.", (p, a) => Ids(p.Timeline.Duplicate(ClipsOf(p, a))));
		Project("timeline.select", "Select Clips", "Selects clips, replacing the selection. Args: `clips` (ids; empty clears it).", (p, a) =>
		{
			p.Timeline.Select(ClipsOf(p, a));
			return null;
		});
		Project("timeline.seek", "Move Playhead", "Moves the playhead and the picture. Args: `at` (seconds).", (p, a) =>
		{
			p.Timeline.Playhead = Seconds(a, "at");
			return null;
		});
		Project("timeline.addChannel", "Add Channel", "Adds a channel. Args: `video` (default true). Returns its index.", (p, a) => p.Timeline.AddChannel(Bool(a, "video", true)).Index);

		Project("playback.play", "Play", "Plays forward at normal speed.", (p, _) => { p.Playback.Play(); return null; });
		Project("playback.pause", "Pause", "Pauses.", (p, _) => { p.Playback.Pause(); return null; });
		Project("playback.setLoop", "Set Loop", "Turns looping on or off. Args: `on` (default true).", (p, a) => { p.Playback.Loop = Bool(a, "on", true); return null; });

		Project("layout.open", "Open View", "Opens a view where it last was. Args: `view` (id).", (p, a) => { p.Layout.Open(Str(a, "view")); return null; });
		Project("layout.close", "Close View", "Closes a view. Args: `view` (id).", (p, a) => { p.Layout.Close(Str(a, "view")); return null; });
		Project("layout.float", "Float View", "Floats a view in a window of its own. Args: `view` (id), `rect` ([x, y, width, height] in screen pixels, optional).", (p, a) =>
		{
			Godot.Rect2I? rect = a?["rect"] is JsonArray r && r.Count == 4 ? new Godot.Rect2I((int)r[0], (int)r[1], (int)r[2], (int)r[3]) : null;
			p.Layout.Float(Str(a, "view"), rect);
			return null;
		});
		Project("layout.dock", "Dock View", "Docks a view beside another. Args: `view`, `beside` (ids), `side` (Center, Left, Right, Top or Bottom; default Center). Returns whether it moved.", (p, a) =>
		{
			DockSide side = Enum.TryParse(Str(a, "side", "Center"), true, out DockSide s) ? s : DockSide.Center;
			return p.Layout.Dock(Str(a, "view"), Str(a, "beside"), side);
		});

		Project("media.importPaths", "Import Files", "Adds files to the project's media. Args: `paths` (file paths). Returns the paths added.", (p, a) =>
			new JsonArray([.. p.Media.Import((a?["paths"] as JsonArray ?? []).Select(x => (string)x)).Select(m => (JsonNode)m.Path)]));
		Project("project.saveTo", "Save To", "Saves the project to a path, which becomes its file. Args: `path`. Returns whether it saved.", (p, a) => p.SaveAs(Str(a, "path")));
		Project("history.batch", "Batch", "Runs several commands as one undo entry. Args: `name`, `commands` ([{\"id\", \"args\"}]). Returns each command's result.", (p, a) =>
		{
			// several commands as one undo entry: {"name": "...", "commands": [{"id": "...", "args": {...}}, ...]}
			JsonArray results = [];
			p.Batch(Str(a, "name", "Batch"), () =>
			{
				foreach (JsonNode step in a?["commands"] as JsonArray ?? [])
					results.Add(EditSharpApp.Instance.Commands.Run((string)step["id"], p, step["args"] as JsonObject)?.DeepClone());
			});
			return results;
		});

		Commands.Register(new Command
		{
			Id = "project.openPath",
			Title = "Open Project File",
			Help = "Opens a project file in a window of its own, without a dialog. Args: `path`. The project arrives a moment later; wait for `project.opened`.",
			Run = (ctx, a) =>
			{
				_ = EditSharpApp.Instance.Projects.OpenAsync(Str(a, "path"));
				return null;
			},
		});
		Commands.Register(new Command
		{
			Id = "project.create",
			Title = "Create Project",
			Help = "Creates a project folder and opens it, without a dialog. Args: `folder` (where it goes), `name`. Returns the project file's path.",
			Run = (ctx, a) =>
			{
				ProjectWindow window = ProjectManager.Singleton.CreateProject(Str(a, "folder"), Str(a, "name"), new EditSharp.Rendering.RenderSettings());
				return window is null ? throw new CommandException("The project couldn't be created.") : (JsonNode)window.Session.FilePath;
			},
		});
	}

	// a command needing a project: the one named by "project" in its args, else the context's
	static void Project(string id, string title, string help, Func<ProjectHandle, JsonObject, JsonNode> run) => Commands.Register(new Command
	{
		Id = id,
		Title = title,
		Help = help,
		CanRun = ctx => ctx.Window is not null,
		Run = (ctx, args) => run(EditSharpApp.Instance.Projects.Handle(ctx.Window), args),
	});

	static Clip ClipOf(ProjectHandle p, string id) =>
		Guid.TryParse(id, out Guid guid) && p.Timeline.Find(guid) is Clip clip ? clip : throw new CommandException($"There's no clip '{id}'.");

	static IEnumerable<Clip> ClipsOf(ProjectHandle p, JsonObject a) => (a?["clips"] as JsonArray ?? []).Select(i => ClipOf(p, (string)i)).ToList();

	static JsonArray Ids(IEnumerable<Clip> clips) => [.. clips.Select(c => (JsonNode)c.Id.ToString())];

	static string Str(JsonObject a, string key, string fallback = null) => a?[key] is JsonNode n ? (string)n : fallback ?? throw new CommandException($"'{key}' is missing.");

	static int Int(JsonObject a, string key, int fallback) => a?[key] is JsonNode n ? n.GetValue<int>() : fallback;

	static bool Bool(JsonObject a, string key, bool fallback) => a?[key] is JsonNode n ? n.GetValue<bool>() : fallback;

	static Time Seconds(JsonObject a, string key) => a?[key] is JsonNode n ? Time.FromSeconds(n.GetValue<double>()) : throw new CommandException($"'{key}' is missing.");
}

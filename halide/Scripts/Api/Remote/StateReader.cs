using EditSharp.Components.Clips;
using EditSharp.Components.Media;
using System.Linq;
using System.Text.Json.Nodes;

namespace Halide.Api.Remote;

/// <summary>What <c>state.get</c> answers: the app and a project's timeline, media, layout, playback, history and commands, as JSON.</summary>
/// <remarks>Times are seconds; clips and media carry their ids.</remarks>
public static class StateReader
{
	/// <summary>The state at <paramref name="path"/>: app, project, timeline, media, layout, playback, history or commands.</summary>
	/// <exception cref="CommandException">The path isn't one of those, or needs a project and none is open.</exception>
	public static JsonNode Read(string path, ProjectHandle project)
	{
		EditSharpApp app = EditSharpApp.Instance;

		if (path == "app")
		{
			return new JsonObject
			{
				["version"] = EditSharpApp.Version,
				["projects"] = new JsonArray([.. app.Projects.All.Select(p => (JsonNode)Summary(p))]),
				["focused"] = app.Projects.Focused?.Name,
			};
		}

		if (path == "commands")
		{
			var context = project?.Context ?? Halide.Scripts.App.Commands.CommandContext.Current;
			return new JsonArray([.. app.Commands.All.Select(c => (JsonNode)new JsonObject
			{
				["id"] = c.Id,
				["title"] = c.TitleIn(context),
				["enabled"] = c.EnabledIn(context),
				["checked"] = c.IsChecked?.Invoke(context),
			})]);
		}

		ProjectHandle p = project ?? app.Projects.Focused ?? throw new CommandException("No project is open.");

		return path switch
		{
			"project" => Summary(p),
			"timeline" => Timeline(p),
			"media" => new JsonArray([.. p.Media.All.Select(m => (JsonNode)Media(m))]),
			"layout" => new JsonObject
			{
				["active"] = p.Layout.Active,
				["layouts"] = new JsonArray([.. p.Layout.Layouts.Select(l => (JsonNode)l)]),
				["views"] = new JsonArray([.. p.Layout.Views.Select(v => (JsonNode)new JsonObject
				{
					["id"] = v.Id,
					["title"] = v.Title,
					["open"] = p.Layout.IsOpen(v.Id),
					["floating"] = p.Layout.IsFloating(v.Id),
				})]),
				["arrangement"] = p.Layout.Capture(),
			},
			"playback" => new JsonObject
			{
				["state"] = p.Playback.State.ToString(),
				["playing"] = p.Playback.Playing,
				["position"] = p.Playback.Position.Seconds,
				["loop"] = p.Playback.Loop,
			},
			"history" => new JsonObject
			{
				["canUndo"] = p.History.CanUndo,
				["canRedo"] = p.History.CanRedo,
				["undo"] = p.History.UndoDescription,
				["redo"] = p.History.RedoDescription,
			},
			_ => throw new CommandException($"There's no state '{path}'. Try app, project, timeline, media, layout, playback, history or commands."),
		};
	}

	static JsonObject Summary(ProjectHandle p) => new()
	{
		["name"] = p.Name,
		["file"] = p.FilePath,
		["dirty"] = p.Dirty,
		["layout"] = p.Layout.Active,
	};

	static JsonObject Timeline(ProjectHandle p)
	{
		var selected = p.Timeline.Selected.ToHashSet();
		return new JsonObject
		{
			["playhead"] = p.Timeline.Playhead.Seconds,
			["duration"] = p.Timeline.Timeline.Duration.Seconds,
			["pixelsPerSecond"] = p.Timeline.PixelsPerSecond,
			["channels"] = new JsonArray([.. p.Timeline.Timeline.Channels.Select(ch => (JsonNode)new JsonObject
			{
				["index"] = ch.Index,
				["kind"] = ch is EditSharp.Components.Channels.VideoChannel ? "video" : "audio",
				["name"] = ch.Name,
				["clips"] = new JsonArray([.. ch.Clips.Select(c => (JsonNode)Clip(c, selected.Contains(c)))]),
			})]),
		};
	}

	static JsonObject Clip(Clip c, bool selected) => new()
	{
		["id"] = c.Id.ToString(),
		["name"] = c.Name,
		["kind"] = c is VideoClip ? "video" : "audio",
		["start"] = c.Start.Seconds,
		["end"] = c.End.Seconds,
		["speed"] = c.Speed.Value,
		["linked"] = c.LinkGroupId?.ToString(),
		["selected"] = selected,
	};

	static JsonObject Media(IMedia m) => new()
	{
		["id"] = m.Id.ToString(),
		["name"] = m.Name,
		["path"] = m.Path,
		["kind"] = m is AudioMedia ? "audio" : "video",
	};
}

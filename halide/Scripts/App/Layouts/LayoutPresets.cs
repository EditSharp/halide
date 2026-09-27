using Halide.Scripts.UI.Docking;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Halide.Scripts.App.Layouts;

/// <summary>The layouts that ship with the app, in switcher order.</summary>
public static class LayoutPresets
{
	public const string Editing = "Editing";
	public const string Assembly = "Assembly";
	public const string Audio = "Audio";

	public static IReadOnlyList<string> Names { get; } = [Editing, Assembly, Audio];

	public static bool IsPreset(string name) => name is Editing or Assembly or Audio;

	/// <summary>A preset as a layout state; null for a name that isn't one.</summary>
	public static JsonObject State(string name)
	{
		DockNode root = name switch
		{
			// media on the left, the program and inspector beside it, the timeline under them all
			Editing => new DockSplit(true, 0.55f,
				new DockSplit(false, 0.25f, Stack("media"), new DockSplit(false, 0.7f, Stack("program"), Stack("inspector"))),
				Stack("timeline")),

			// picking clips: a big media pane beside the source and program viewers, the inspector a tab behind the media
			Assembly => new DockSplit(true, 0.62f,
				new DockSplit(false, 0.4f, Stack("media", "inspector"), new DockSplit(false, 0.5f, Stack("source"), Stack("program"))),
				Stack("timeline")),

			// a tall timeline under a small program viewer, the inspector down the side
			Audio => new DockSplit(false, 0.78f,
				new DockSplit(true, 0.3f, new DockSplit(false, 0.4f, Stack("media"), Stack("program")), Stack("timeline")),
				Stack("inspector")),

			_ => null,
		};

		return root is null ? null : new JsonObject { ["main"] = root.ToJson(), ["floats"] = new JsonArray() };
	}

	static DockStack Stack(params string[] views) => new([.. views]);
}

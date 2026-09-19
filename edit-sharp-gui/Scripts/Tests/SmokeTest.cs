using EditSharpGUI.Scripts.Tools.ThemeEditing;
using EditSharpGUI.Scripts.UI;
using EditSharpGUI.Scripts.UI.Inspecting;
using EditSharpGUI.Scripts.UI.Theming;
using EditSharpGUI.Scripts.UI.Thumbnails;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// runs the editor page headless and drives it the way a user would, to shake
// out runtime errors the compiler cannot: select every clip so the inspector
// builds rows for them, move the playhead, flip time fields to frames, undo
// and redo, and wait for thumbnails. prints SMOKE lines and quits.
//
//   godot --headless --path . res://Scenes/Tests/Smoke.tscn
public partial class SmokeTest : Node
{
	int failures;

	void Check(bool ok, string what)
	{
		GD.Print((ok ? "SMOKE PASS " : "SMOKE FAIL ") + what);
		if (!ok) failures++;
	}

	public override async void _Ready()
	{
		try
		{
			await Run();
		}
		catch (Exception e)
		{
			GD.PrintErr("SMOKE EXCEPTION " + e);
			failures++;
		}

		GD.Print(failures == 0 ? "SMOKE OK" : $"SMOKE {failures} FAILURES");
		GetTree().Quit(failures == 0 ? 0 : 1);
	}

	async Task Frames(int count)
	{
		for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	async Task Run()
	{
		Node editor = GD.Load<PackedScene>("res://Scenes/Views/Editor.tscn").Instantiate();
		AddChild(editor);
		await Frames(3);

		UITimeline timeline = editor.GetNode<UITimeline>("VSplitContainer/Timeline");
		Inspector inspector = editor.GetNode<Inspector>("VSplitContainer/HSplitContainer/Inspector");
		UIClipsView clipsView = (UIClipsView)timeline.Get("clipsView");

		Check(timeline.Timeline is not null, "timeline is set");
		Check(inspector is not null, "inspector is in the page");
		Check(timeline.Thumbnails is ThumbnailCache, "thumbnail cache is wired");

		// ---- the theme: every item themed, every bound colour resolved
		EditSharpTheme theme = ThemeDB.GetProjectTheme() as EditSharpTheme;
		Check(theme is not null, "the project theme is an EditSharpTheme");

		if (theme is not null)
		{
			Check(theme.GetStylebox("normal", "Button") is ThemedStyleBox, "buttons wear themed styleboxes");
			Check(theme.GetStylebox("panel", "ClipContent") is ThemedStyleBox content && content.CornerRadiusTopLeft == 5, "clip content keeps its rounded corners");
			Check(theme.GetColor("video", "Clip") == theme.VideoClipColor, "the named clip colour follows its definition");
			Check(theme.GetStylebox("scroll", "VScrollBar").GetMinimumSize().X >= 6f, $"the scroll bar has a width: {theme.GetStylebox("scroll", "VScrollBar").GetMinimumSize().X}");
			Check(theme.ColorBindings.Count > 30 && theme.FontBindings.Count > 3, $"bindings present: {theme.ColorBindings.Count} colours, {theme.FontBindings.Count} fonts");

			Color before = theme.GetColor("font_color", "Label");
			Color was = theme.FontColor1;
			theme.FontColor1 = new Color(0.5f, 0.25f, 0.75f);
			Check(theme.GetColor("font_color", "Label") == theme.FontColor1 && theme.GetColor("font_color", "Label") != before, "changing a definition recolours a bound item at once");
			ThemedStyleBox normal = (ThemedStyleBox)theme.GetStylebox("normal", "Button");
			theme.ButtonColor = new Color(0.1f, 0.6f, 0.2f);
			Check(normal.BgColor == theme.ButtonColor && normal.CornerRadiusTopLeft == 3, "changing a definition recolours a stylebox and keeps its shape");
			theme.FontColor1 = was;
			theme.ApplyPreset(EditSharpTheme.ColorPreset.Dark);
		}

		Check(Find<ThumbnailStrip>(clipsView.UIClips[0]).Count == 1 && Find<ThumbnailStrip>(clipsView.UIClips[0])[0].Material is ShaderMaterial, "the clip's strip comes from the scene with its rounding mask");

		// ---- the inspector on everything
		clipsView.SelectAll();
		await Frames(3);

		List<InspectorRow> rows = Find<InspectorRow>(inspector);
		List<InspectorSection> sections = Find<InspectorSection>(inspector);
		Check(timeline.SelectedClips.Count > 1, $"{timeline.SelectedClips.Count} clips selected");
		Check(sections.Count > 0, $"{sections.Count} inspector sections built");
		Check(rows.Count > 0, $"{rows.Count} inspector rows built");
		Check(Find<SpinSlider>(inspector).Count > 0, "spin sliders present");
		Check(Find<KeyColumn>(inspector).Count == rows.Count, "every row has a key column");

		// ---- one clip: every node of its graph gets a section
		clipsView.DeselectAll();
		await Frames(2);
		Check(Find<InspectorRow>(inspector).Count == 0, "deselecting empties the inspector");

		clipsView.SelectClip(clipsView.UIClips[0], SelectionMode.Exclusive);
		await Frames(3);
		sections = Find<InspectorSection>(inspector);
		rows = Find<InspectorRow>(inspector);
		Check(sections.Count >= 2, $"single clip: {sections.Count} sections (clip + nodes)");
		Check(rows.Count > 0, $"single clip: {rows.Count} rows");

		// ---- the playhead, frames, and history all refresh without complaint
		inspector.Playhead = TimeSpan.FromSeconds(1.5);
		await Frames(2);
		inspector.ShowFrames = true;
		await Frames(2);
		inspector.ShowFrames = false;
		await Frames(2);

		// ---- a move from outside any view, undone and redone under the views
		EditSharp.Components.Clips.Clip clip = clipsView.UIClips[0].Clip;
		TimeSpan start = clip.Start;
		int entries = timeline.History.Position;

		using (EditSharp.History.Transaction.Scope change = timeline.History.Begin("Move"))
		{
			clip.Move(start + TimeSpan.FromSeconds(0.25));
			change.Commit();
		}

		await Frames(2);
		Check(timeline.History.Position == entries + 1, "the move is one history entry");
		timeline.History.Undo();
		await Frames(2);
		Check(clip.Start == start, "undo put the clip back");
		timeline.History.Redo();
		await Frames(2);
		Check(clip.Start == start + TimeSpan.FromSeconds(0.25), "redo moved it again");
		timeline.History.Undo();
		await Frames(2);
		Check(clipsView.UIClips.Count == clipsView.UIClips.Count && Find<InspectorRow>(inspector).Count == rows.Count, "the inspector survived undo and redo");

		// ---- edits through the inspector: a value, and a keyframe
		InspectorRow speed = rows.Find(r => r.Label == "Speed");
		Check(speed is not null, "the clip's Speed row is there");

		if (speed is not null)
		{
			entries = timeline.History.Position;
			speed.Apply(2d);
			await Frames(2);
			Check(Math.Abs(clip.Speed - 2d) < 1e-9, $"Apply set the clip's speed: {clip.Speed}");
			Check(timeline.History.Position == entries + 1, "and recorded one entry");
			Check(clipsView.UIClips[0].Size.X > 0f, "the timeline re-read it");
			timeline.History.Undo();
			await Frames(2);
			Check(Math.Abs(clip.Speed - 1d) < 1e-9, $"undo restored the speed: {clip.Speed}");

			speed.Apply(3d);
			await Frames(1);
			speed.ResetToDefault();
			await Frames(1);
			Check(Math.Abs(clip.Speed - 1d) < 1e-9, $"reset put the speed back to its default: {clip.Speed}");
			timeline.History.Undo();
			await Frames(1);
			Check(Math.Abs(clip.Speed - 3d) < 1e-9, "and the reset is one undoable entry");
			timeline.History.Undo();
			await Frames(1);
		}

		InspectorRow rotation = rows.Find(r => r.Label == "Rotation");
		Check(rotation is not null && rotation.Animatable, "the transform's Rotation row is animatable");

		if (rotation is not null)
		{
			EditSharp.Components.IAnimatable animatable = rotation.Bindings[0].Animatable;
			inspector.Playhead = clip.Start + TimeSpan.FromSeconds(0.5);
			await Frames(1);

			rotation.ToggleKeyframe();
			await Frames(1);
			Check(animatable.Keyframes.Count == 1 && Math.Abs((animatable.Keyframes[0].Start - TimeSpan.FromSeconds(0.5)).TotalSeconds) < 1e-3, $"the diamond keyed the value at the playhead's content time: {string.Join(",", animatable.Keyframes.Select(k => k.Start.TotalSeconds))}");

			rotation.Apply(45d);
			await Frames(1);
			Check(animatable.Keyframes.Count == 1 && Convert.ToDouble(animatable.Keyframes[0].Value) == 45d, "an edit on a keyed row updates the keyframe");

			inspector.Playhead = clip.Start + TimeSpan.FromSeconds(1d);
			await Frames(1);
			rotation.Apply(90d);
			await Frames(1);
			Check(animatable.Keyframes.Count == 2 && animatable.IsAnimated, "an edit at another time adds a keyframe");

			rotation.ResetTrack();
			await Frames(1);
			Check(animatable.Keyframes.Count == 0 && Math.Abs(Convert.ToDouble(animatable.GetStaticValue()) - 90d) < 1e-6, $"the track reset drops the keyframes and keeps the value at the playhead: {animatable.GetStaticValue()}");
			timeline.History.Undo();
			await Frames(1);
			Check(animatable.Keyframes.Count == 2, "and is one undoable entry");

			rotation.ToggleKeyframe();
			await Frames(1);
			Check(animatable.Keyframes.Count == 1, "the diamond removes the keyframe under the playhead");

			rotation.ResetToDefault();
			await Frames(1);
			Check(animatable.Keyframes.Count == 2 && animatable.Keyframes.Any(k => Math.Abs((k.Start - TimeSpan.FromSeconds(1d)).TotalSeconds) < 1e-3 && Convert.ToDouble(k.Value) == 0d), "reset on a keyed row keys the default at the playhead");

			timeline.History.Undo();
			timeline.History.Undo();
			timeline.History.Undo();
			timeline.History.Undo();
			timeline.History.Undo();
			await Frames(2);
			Check(animatable.Keyframes.Count == 0, $"five undos clear the keyframes: {animatable.Keyframes.Count}");
		}

		// ---- the theme tool: every type's preview and pages build without complaint
		{
			ThemeEditor tool = GD.Load<PackedScene>("res://Scenes/Tools/ThemeEditor.tscn").Instantiate<ThemeEditor>();
			AddChild(tool);
			await Frames(2);

			Inspector pages = tool.GetNode<Inspector>("Layout/Split/Right/Properties");
			Check(Find<InspectorRow>(pages).Count > 20, $"the definitions page has rows: {Find<InspectorRow>(pages).Count}");
			Check(Find<ThemeSwatch>(tool).Count > 20, $"the definitions page has swatches: {Find<ThemeSwatch>(tool).Count}");

			int shown = 0;
			foreach (string type in theme.GetTypeList())
			{
				tool.Select(type);
				await Frames(1);
				shown++;
			}

			tool.Select("Button");
			await Frames(2);
			Check(Find<InspectorRow>(pages).Count > 40, $"a button's pages have rows: {Find<InspectorRow>(pages).Count}");
			Check(Find<ThemeSwatch>(tool).Count >= 16, $"a button's items have swatches: {Find<ThemeSwatch>(tool).Count}");
			Check(Find<Button>(tool.GetNode("Layout/Split/Right/PreviewScroll")).Count >= 1, "a button's live sample is a button");
			Check(shown == theme.GetTypeList().Length, $"every type showed: {shown}");

			// a pick made through the tool lands on the theme
			InspectorSection normalSection = Find<InspectorSection>(pages).Find(x => x.Title == "normal");
			InspectorRow background = normalSection is null ? null : Find<InspectorRow>(normalSection).Find(r => r.Label == "Background");
			Check(background is not null, "the normal stylebox's background pick is there");
			if (background is not null)
			{
				background.Apply(ThemeDefinition.AccentColor);
				await Frames(2);
				Check(theme.GetStylebox("normal", "Button") is ThemedStyleBox b && b.Background == ThemeDefinition.AccentColor && b.BgColor == theme.AccentColor, "picking a definition recolours the stylebox");
			}

			tool.QueueFree();
			await Frames(1);
		}

		// ---- thumbnails come in
		ThumbnailCache cache = timeline.Thumbnails;
		int waited = 0;
		while (cache.Count == 0 && waited < 600) { await Frames(1); waited++; }
		Check(cache.Count > 0, $"{cache.Count} thumbnails rendered ({cache.Bytes} bytes, {cache.Pending} pending) after {waited} frames");
	}

	static List<T> Find<T>(Node root) where T : Node
	{
		List<T> found = [];
		Collect(root, found);
		return found;
	}

	static void Collect<T>(Node node, List<T> into) where T : Node
	{
		if (node is T match) into.Add(match);
		foreach (Node child in node.GetChildren()) Collect(child, into);
	}
}

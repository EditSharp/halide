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

		// ---- the theme: styles resolved through the palette, bindings written from it
		EditSharpTheme theme = ThemeDB.GetProjectTheme() as EditSharpTheme;
		Check(theme is not null && theme.Palette is not null, "the project theme is an EditSharpTheme with a palette");

		if (theme is not null && theme.Palette is ThemePalette palette)
		{
			Check(theme.GetStylebox("normal", "Button") is ThemedStyleBox normal && normal.Style is not null && normal.State == StyleState.Normal, "buttons wear themed styleboxes over a style");
			Check(theme.GetStylebox("hover", "Button") is ThemedStyleBox hover && hover.State == StyleState.Hover && hover.Style == ((ThemedStyleBox)theme.GetStylebox("normal", "Button")).Style, "hover shares the base style with its state");
			Check(theme.GetStylebox("focus", "Button") is ThemedStyleBox focus && focus.State == StyleState.Focus, "focus is the same style in the focus state");
			Check(theme.GetStylebox("panel", "ClipContent") is ThemedStyleBox content && content.MinCornerRadius == 5, "clip content keeps its rounded corners");
			Check(theme.GetStylebox("hover", "CheckBox") is StyleBoxEmpty, "an empty base leaves empty states");
			Check(theme.GetColor("video", "Clip") == palette.VideoClipColor, "the named clip colour follows its definition");
			Check(theme.GetStylebox("scroll", "VScrollBar").GetMinimumSize().X >= 6f, $"the scroll bar has a width: {theme.GetStylebox("scroll", "VScrollBar").GetMinimumSize().X}");
			Check(theme.ColorBindings.Count > 30 && theme.FontBindings.Count > 3, $"bindings present: {theme.ColorBindings.Count} colours, {theme.FontBindings.Count} fonts");
			Check(theme.GetFont("font", "ClipName") is FontVariation, "a font binding serves a weighted variation");

			ThemedStyleBox box = (ThemedStyleBox)theme.GetStylebox("normal", "Button");
			Color wasButton = palette.ButtonColor;
			Color wasFont = palette.FontColor1;
			palette.ButtonColor = new Color(0.1f, 0.6f, 0.2f);
			Check(box.ResolvedBackground == palette.Resolve(box.Style.Background, ThemeShade.None, box.Style.BackgroundAlpha), "a stylebox resolves the new palette colour when it next draws");
			Check(((ThemedStyleBox)theme.GetStylebox("hover", "Button")).ResolvedBackground == palette.Resolve(box.Style.Background, ThemeShade.Hover, box.Style.BackgroundAlpha), "and its hover state shifts it by the palette rule");
			palette.FontColor1 = new Color(0.5f, 0.25f, 0.75f);
			theme.ApplyBindings();
			Check(theme.GetColor("font_color", "Label") == palette.FontColor1, "a bound colour item is rewritten from the palette");
			palette.ButtonColor = wasButton;
			palette.FontColor1 = wasFont;
			theme.ApplyBindings();

			StyleBoxFlat tinted = box.MakeFlat();
			Check(tinted is not null && tinted.CornerRadiusTopLeft == box.Style.CornerRadiusTopLeft, "a flat copy carries the style shape");

			// a style edit must reach the controls wearing it: their minimum size follows the margins
			Button probe = new() { Text = "probe" };
			AddChild(probe);
			await Frames(1);
			float before = probe.GetCombinedMinimumSize().X;
			float marginWas = box.Style.ContentMarginLeft;
			box.Style.ContentMarginLeft = marginWas + 40f;
			await Frames(2);
			Check(probe.GetCombinedMinimumSize().X >= before + 39f, $"a style edit re-themes controls: {before} -> {probe.GetCombinedMinimumSize().X}");
			box.Style.ContentMarginLeft = marginWas;
			await Frames(2);
			Check(Mathf.IsEqualApprox(probe.GetCombinedMinimumSize().X, before), "and back");
			probe.QueueFree();
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

		// ---- the mock scene builds every editor and control under the palette
		{
			ThemeMockPage mock = GD.Load<PackedScene>("res://Scenes/Tools/ThemeMock.tscn").Instantiate<ThemeMockPage>();
			AddChild(mock);
			await Frames(2);
			Check(Find<InspectorRow>(mock).Count > 40, $"the mock scene builds a live inspector: {Find<InspectorRow>(mock).Count} rows");
			Check(Find<Button>(mock).Count > 10, "the mock scene has live controls");
			Check(Find<SpinSlider>(mock).Count > 0 && Find<SpinSlider>(mock).All(x => x.GetThemeStylebox("normal") is ThemedStyleBox), "spin sliders are buttons wearing the theme");
			Check(Find<InspectorGlyph>(mock).All(g => g.Icon is not null), "every glyph shows an icon");
			mock.QueueFree();
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

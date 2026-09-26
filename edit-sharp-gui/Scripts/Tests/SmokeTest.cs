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
		Inspector inspector = Find<Inspector>(editor).FirstOrDefault();
		UIClipsView clipsView = (UIClipsView)timeline.Get("clipsView");

		Check(timeline.Timeline is not null, "timeline is set");
		Check(inspector is not null, "inspector is in the page");
		Check(timeline.Thumbnails is ThumbnailCache, "thumbnail cache is wired");

		// ---- the preview shows a frame without being played, and again after each change
		{
			UIPlayback preview = editor.GetNode<UIPlayback>("VSplitContainer/HSplitContainer/Viewers/Playback");
			var pb = (EditSharp.Playback.Playback)typeof(UIPlayback).GetField("playback", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(preview);
			int shown = 0;
			pb.VideoFrame += (_, _) => System.Threading.Interlocked.Increment(ref shown);

			async Task<bool> NewFrame(int since)
			{
				for (int i = 0; i < 300 && shown <= since; i++) await Frames(1);
				return shown > since;
			}

			// startup already asked for one; it may have landed before we listened
			await Frames(30);
			int atStart = shown;
			preview.RefreshFrame();
			Check(await NewFrame(atStart), "the preview renders the frame at the playhead while stopped");

			int before = shown;
			EditSharp.Components.Clips.Clip first = clipsView.UIClips[0].Clip;
			using (EditSharp.History.Transaction.Scope change = timeline.History.Begin("Nudge"))
			{
				first.Move(first.Start + TimeSpan.FromSeconds(0.1));
				change.Commit();
			}
			Check(await NewFrame(before), "a committed edit re-renders it");
			before = shown;
			timeline.History.Undo();
			Check(await NewFrame(before), "so does an undo");
		}

		// ---- the theme: styles resolved through the palette, bindings written from it
		EditSharpTheme theme = ThemeDB.GetProjectTheme() as EditSharpTheme;
		Check(theme is not null && theme.Palette is not null, "the project theme is an EditSharpTheme with a palette");

		if (theme is not null && theme.Palette is ThemePalette palette)
		{
			Check(theme.GetStylebox("normal", "Button") is ThemedStyleBox normal && normal.Style is not null && normal.State == StyleState.Normal, "buttons wear themed styleboxes over a style");
			Check(theme.GetStylebox("hover", "Button") is ThemedStyleBox hover && hover.State == StyleState.Hover && hover.Style == ((ThemedStyleBox)theme.GetStylebox("normal", "Button")).Style, "hover shares the base style with its state");
			Check(theme.GetStylebox("focus", "Button") is ThemedStyleBox focus && focus.State == StyleState.Focus, "focus is the same style in the focus state");
			Check(theme.GetStylebox("panel", "ClipContent") is ThemedStyleBox content && content.MinCornerRadius > 0, "clip content keeps its rounded corners");
			Check(theme.GetStylebox("hover", "CheckBox") is StyleBoxEmpty, "an empty base leaves empty states");
			Check(theme.GetColor("video", "Clip") == palette.Resolve(palette.VideoClip), "the named clip colour follows its kind's swatch");
			Check(theme.GetColor("poppy", "Clip") == palette.Resolve(ClipSwatch.Poppy), "a swatch item follows the palette swatch");
			Check(palette.AccentColor != palette.PlaybackColor, $"the accent is the palette's own, not the playback colour: {palette.AccentColor.ToHtml(false)}");
			Check(theme.GetStylebox("scroll", "VScrollBar").GetMinimumSize().X >= 6f, $"the scroll bar has a width: {theme.GetStylebox("scroll", "VScrollBar").GetMinimumSize().X}");
			Check(theme.ColorBindings.Count > 30 && theme.FontBindings.Count > 3, $"bindings present: {theme.ColorBindings.Count} colours, {theme.FontBindings.Count} fonts");
			FontBinding clipName = theme.FindFontBinding("ClipName");
			Check(clipName is not null && theme.GetFont("font", "ClipName") == palette.ResolveFont(clipName.Family, clipName.Weight), $"a font binding serves the palette font for its weight ({clipName?.Weight})");

			// the UI font is a variable font: its weight axis must change the glyphs
			Font regular = palette.ResolveFont(ThemeFontFamily.UI, ThemeFontWeight.Regular);
			Font bold = palette.ResolveFont(ThemeFontFamily.UI, ThemeFontWeight.Bold);
			Font light = palette.ResolveFont(ThemeFontFamily.UI, ThemeFontWeight.Light);
			var axes = palette.UIFont.GetSupportedVariationList();
			GD.Print("UI font axes: " + string.Join(", ", axes.Keys.Select(k => $"{k}={axes[k]}")));
			float wRegular = regular.GetStringSize("Weight check", HorizontalAlignment.Left, -1f, 16).X;
			float wBold = bold.GetStringSize("Weight check", HorizontalAlignment.Left, -1f, 16).X;
			float wLight = light.GetStringSize("Weight check", HorizontalAlignment.Left, -1f, 16).X;
			if (palette.UIFontIsVariable || palette.UIFontBold is not null)
				Check(wBold > wRegular && wLight < wRegular, $"weights change the UI font: light {wLight}, regular {wRegular}, bold {wBold}");
			else
				GD.PushWarning($"the UI font {palette.UIFont?.ResourcePath} has no weight axis and no static weights are set, so every weight looks the same");

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

		// ---- frame-relative values in pixels
		{
			clipsView.SelectClip(clipsView.UIClips.First(u => u.Clip is EditSharp.Components.Clips.VideoClip), SelectionMode.Exclusive);
			await Frames(3);

			InspectorRow position = Find<InspectorRow>(inspector).Find(r => r.Label == "Position");
			InspectorRow speedRow = Find<InspectorRow>(inspector).Find(r => r.Label == "Speed");
			InspectorRow rotationRow = Find<InspectorRow>(inspector).Find(r => r.Label == "Rotation");
			Button Toggle(InspectorRow r) => Find<Button>(r).Find(b => b.Name == "FrameToggle");
			Check(position is not null && Toggle(position) is { Visible: true }, "Position has the pixel toggle");
			Check(Toggle(speedRow) is null or { Visible: false } && Toggle(rotationRow) is null or { Visible: false }, "Speed and Rotation don't");

			if (position is not null)
			{
				var transform = (EditSharp.Components.Clips.ClipTransform)((PropertyBinding)position.Bindings[0]).Target;
				position.Apply(new System.Numerics.Vector2(0.5f, -0.25f));
				await Frames(1);

				Toggle(position).EmitSignal(BaseButton.SignalName.Pressed);
				await Frames(2);
				SpinSlider px = (SpinSlider)Find<VectorEditor>(position)[0].Get("x");
				SpinSlider py = (SpinSlider)Find<VectorEditor>(position)[0].Get("y");
				GD.Print($"frame {inspector.FrameSize}: position shows {px.Text}, {py.Text}; toggle {Toggle(position).Text}");
				Check(inspector.ShowPixels && px.Text == $"{0.5 * inspector.FrameSize.X / 2:0} px" && py.Text == $"{-0.25 * inspector.FrameSize.Y / 2:0} px", "the toggle shows positions in pixels from the centre");
				Check(Find<SpinSlider>(speedRow).All(sp => !sp.Text.EndsWith("px")), "and leaves Speed alone");

				// typed in pixels, stored as a fraction
				typeof(SpinSlider).GetMethod("StartTyping", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(px, null);
				((LineEdit)px.Get("entry")).Text = (inspector.FrameSize.X / 4).ToString();
				typeof(SpinSlider).GetMethod("OnEntrySubmitted", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(px, [""]);
				await Frames(2);
				Check(Math.Abs(transform.Position.StaticValue.X - 0.5f) < 1e-4, $"a quarter frame width typed in pixels is 0.5 half-widths: {transform.Position.StaticValue.X}");

				Toggle(position).EmitSignal(BaseButton.SignalName.Pressed);
				await Frames(2);
				Check(!inspector.ShowPixels && px.Text == "0.500", $"and back to fractions: {px.Text}");

				timeline.History.Undo();
				await Frames(1);
			}
		}

		// ---- an input node's kind, switched from its section's header picker
		{
			static EditSharp.Components.Nodes.Input.VideoInputNode InputOf(EditSharp.Components.Clips.Clip c) =>
				c.Graph.AllNodes.OfType<EditSharp.Components.Nodes.Input.VideoInputNode>().FirstOrDefault();

			List<UIClip> video = [.. clipsView.UIClips.Where(u => InputOf(u.Clip) is EditSharp.Components.Nodes.Input.VideoMediaNode)];
			Check(video.Count >= 2, $"{video.Count} clips with a video media input");

			// the audio clip plays the same file: the video media's own audio, not a media of its own
			{
				var shared = ((EditSharp.Components.Nodes.Input.VideoMediaNode)InputOf(video[0].Clip)).Media;
				var audioNodes = clipsView.UIClips.Select(u => u.Clip).OfType<EditSharp.Components.Clips.AudioClip>().SelectMany(a => a.Graph.AllNodes.OfType<EditSharp.Components.Nodes.Input.AudioMediaNode>()).ToList();
				Check(audioNodes.Count > 0 && audioNodes.All(n => ReferenceEquals(n.Media, shared.Audio)), "the audio clip plays the video media's own audio");
				Check(inspector.Media.For(typeof(EditSharp.Components.Media.AudioMedia)).Contains(shared.Audio) && !inspector.Media.Contains(shared.Audio), "which the project offers for audio without listing it as an entry");
			}

			if (video.Count >= 2)
			{
				EditSharp.Components.Nodes.Input.VideoInputNode original = InputOf(video[0].Clip);
				var originalMedia = ((EditSharp.Components.Nodes.Input.VideoMediaNode)original).Media;
				TimeSpan? inPoint = original.Start;

				clipsView.SelectClip(video[0], SelectionMode.Exclusive);
				await Frames(3);

				InspectorSection InputSection() => Find<InspectorSection>(inspector).Find(s => s.HasKindPicker);
				List<string> InputRows() => [.. Find<InspectorRow>(InputSection()).Select(r => r.Label)];
				OptionButton picker = InputSection()?.Picker;
				Check(picker is not null && picker.Visible, "the input node's section has a kind picker");

				// the media section under it: the shared media's rows, with a picker of the project's media
				InspectorSection mediaSection = Find<InspectorSection>(inspector).Find(s => s.MediaDescriptor is not null);
				List<string> mediaRows = mediaSection is null ? [] : [.. Find<InspectorRow>(mediaSection).Select(r => r.Label)];
				GD.Print("media rows: " + string.Join(", ", mediaRows));
				Check(mediaSection is not null && mediaSection.Picker.Visible && mediaRows.Contains("File") && mediaRows.Contains("Name"), "the Media section shows the media's name and file with a media picker");
				Check(mediaSection is not null && mediaSection.Picker.Text == originalMedia.Name, $"the media picker shows the media's name: {mediaSection?.Picker.Text}");
				Check(mediaSection is not null && mediaSection.Picker.GetItemText(mediaSection.Picker.ItemCount - 1) == "Browse…", "and offers to browse for a file");

				if (picker is not null)
				{
					List<string> items = [.. Enumerable.Range(0, picker.ItemCount).Select(picker.GetItemText)];
					GD.Print("kinds: " + string.Join(", ", items));
					Check(items.SequenceEqual(["Color", "Noise", "Text", "Video"]), "video input kinds listed by name, Timeline hidden");
					Check(picker.Text == EditSharp.Components.Nodes.NodeKinds.Of(original).DisplayName, $"the picker shows the current kind: {picker.Text}");

					List<string> rowsBefore = InputRows();
					entries = timeline.History.Position;
					picker.Select(items.IndexOf("Color"));
					picker.EmitSignal(OptionButton.SignalName.ItemSelected, (long)items.IndexOf("Color"));
					await Frames(2);

					EditSharp.Components.Nodes.Input.VideoInputNode switched = InputOf(video[0].Clip);
					Check(switched is EditSharp.Components.Nodes.Input.ColorNode && switched.Start == inPoint, "switching makes a Color node, keeping its in-point");
					Check(!video[0].Clip.Graph.AllNodes.Contains(original), "and takes the media node out of the graph");
					Check(timeline.History.Position == entries + 1, "the switch is one history entry");
					Check(InputRows().Contains("Color"), "the Color row appears");
					Check(video[0].Color == inspector.GetThemeColor("generator_video", "Clip"), "and the clip on the timeline recolours as a generator");
					Find<InspectorRow>(inspector).Find(r => r.Label == "Name").Apply("Renamed clip");
					await Frames(2);
					Check(((Label)video[0].Get("clipName")).Text == "Renamed clip", "renaming the clip in the inspector renames it on the timeline");
					timeline.History.Undo();
					await Frames(2);
					Check(((Label)video[0].Get("clipName")).Text == video[0].Clip.Name && video[0].Clip.Name != "Renamed clip", "and undo puts the old name back on the timeline");
					Check(Find<InspectorSection>(inspector).Find(s => s.MediaDescriptor is not null) is null, "and the Media section is gone");

					timeline.History.Undo();
					await Frames(2);
					Check(InputOf(video[0].Clip) == original, "undo brings the original node back");
					Check(InputRows().SequenceEqual(rowsBefore), $"and its rows: {string.Join(", ", InputRows())}");
					picker = InputSection()?.Picker;
					Check(picker?.Text == EditSharp.Components.Nodes.NodeKinds.Of(original).DisplayName, $"and its kind in the picker: {picker?.Text}");

					timeline.History.Redo();
					await Frames(2);
					Check(InputOf(video[0].Clip) is EditSharp.Components.Nodes.Input.ColorNode && InputRows().Contains("Color"), "redo switches again");

					// two clips of different kinds: a dash, and only what every input has
					clipsView.SelectClip(video[0], SelectionMode.Exclusive);
					clipsView.SelectClip(video[1], SelectionMode.Inclusive);
					await Frames(3);
					picker = InputSection()?.Picker;
					List<string> labels = InputSection() is null ? [] : InputRows();
					GD.Print("mixed input rows: " + string.Join(", ", labels));
					Check(picker is not null && picker.Text == "—" && picker.Selected == -1, "mixed kinds show a dash");
					Check(labels.Count > 0 && labels.All(l => l is "In point" or "Duration" or "Loop"), "and only the rows every input has");

					entries = timeline.History.Position;
					picker.EmitSignal(OptionButton.SignalName.ItemSelected, (long)items.IndexOf("Noise"));
					await Frames(2);
					Check(InputOf(video[0].Clip) is EditSharp.Components.Nodes.Input.NoiseNode && InputOf(video[1].Clip) is EditSharp.Components.Nodes.Input.NoiseNode, "picking a kind switches both");
					Check(timeline.History.Position == entries + 1, "in one entry");

					timeline.History.Undo();
					timeline.History.Undo();
					await Frames(2);
					Check(InputOf(video[0].Clip) == original && ReferenceEquals(((EditSharp.Components.Nodes.Input.VideoMediaNode)original).Media, originalMedia), "undoing both puts the first clip back as it was, media included");

					// a colour popup left open when the selection changes must not leave its edit open
					clipsView.SelectClip(video[0], SelectionMode.Exclusive);
					await Frames(2);
					picker = InputSection().Picker;
					picker.EmitSignal(OptionButton.SignalName.ItemSelected, (long)items.IndexOf("Color"));
					await Frames(2);
					InspectorRow colourRow = Find<InspectorRow>(InputSection()).Find(r => r.Label == "Color");
					ColorEditor colourEditor = colourRow is null ? null : Find<ColorEditor>(colourRow).FirstOrDefault();
					Check(colourEditor is not null, "the Color node has a colour editor");
					if (colourEditor is not null)
					{
						((Button)colourEditor.Get("swatch")).EmitSignal(BaseButton.SignalName.Pressed);
						await Frames(2);
						Check(EditSharp.History.Transaction.IsOpen, "opening the picker opens an edit");
						clipsView.SelectClip(video[1], SelectionMode.Exclusive);
						await Frames(3);
						Check(!EditSharp.History.Transaction.IsOpen, "changing the selection with the picker open closes the edit");
						int before = timeline.History.Position;
						timeline.History.Undo();
						await Frames(2);
						Check(timeline.History.Position == before - 1, "and undo still works after it");
					}

					// a text node: its font from the installed ones, its size and wrap
					clipsView.SelectClip(video[0], SelectionMode.Exclusive);
					await Frames(2);
					picker = InputSection().Picker;
					picker.EmitSignal(OptionButton.SignalName.ItemSelected, (long)items.IndexOf("Text"));
					await Frames(2);
					List<string> textRows = InputRows();
					GD.Print("text rows: " + string.Join(", ", textRows));
					Check(new[] { "Text", "Font", "Size", "Box", "Wrap", "Horizontal alignment", "Vertical alignment" }.All(textRows.Contains), "a text node shows its font, size, box, wrap and alignment rows");
					OptionButton wrapMode = Find<DropdownEditor>(Find<InspectorRow>(inspector).Find(r => r.Label == "Wrap")).Select(d => (OptionButton)d.Get("options")).FirstOrDefault();
					OptionButton across = Find<DropdownEditor>(Find<InspectorRow>(inspector).Find(r => r.Label == "Horizontal alignment")).Select(d => (OptionButton)d.Get("options")).FirstOrDefault();
					GD.Print($"wrap: {string.Join(", ", Enumerable.Range(0, wrapMode?.ItemCount ?? 0).Select(wrapMode.GetItemText))}; horizontal: {string.Join(", ", Enumerable.Range(0, across?.ItemCount ?? 0).Select(across.GetItemText))}");
					Check(wrapMode?.ItemCount == 3 && across?.ItemCount == 4, "wrap offers three modes and horizontal alignment four");
					OptionButton fonts = Find<DropdownEditor>(Find<InspectorRow>(inspector).Find(r => r.Label == "Font")).Select(d => (OptionButton)d.Get("options")).FirstOrDefault();
					Check(fonts is not null && fonts.ItemCount > 20 && fonts.Text == "Arial", $"the font dropdown lists the installed fonts: {fonts?.ItemCount}, showing {fonts?.Text}");

					List<string> WeightItems()
					{
						OptionButton w = Find<DropdownEditor>(Find<InspectorRow>(inspector).Find(r => r.Label == "Weight")).Select(d => (OptionButton)d.Get("options")).FirstOrDefault();
						return w is null ? [] : [.. Enumerable.Range(0, w.ItemCount).Select(w.GetItemText)];
					}
					GD.Print("Arial weights: " + string.Join(", ", WeightItems()));
					Check(WeightItems().SequenceEqual(["Regular", "Bold", "Black"]) && textRows.Contains("Italic"), "Weight lists the font's weights by name, and Italic is there");
					Find<InspectorRow>(inspector).Find(r => r.Label == "Font").Apply("Segoe UI");
					await Frames(2);
					GD.Print("Segoe UI weights: " + string.Join(", ", WeightItems()));
					Check(WeightItems().Contains("Semibold") && WeightItems().Contains("Light"), "changing the font refreshes the weights on offer");

					// typing: the text changes live, the preview follows, and it's one entry when the field is left
					{
						var textNode = (EditSharp.Components.Nodes.Input.TextNode)((PropertyBinding)Find<InspectorRow>(inspector).Find(r => r.Label == "Text").Bindings[0]).Target;
						TextEdit field = Find<TextEdit>(Find<InspectorRow>(inspector).Find(r => r.Label == "Text")).First();
						UIPlayback preview = editor.GetNode<UIPlayback>("VSplitContainer/HSplitContainer/Viewers/Playback");
						var pb = (EditSharp.Playback.Playback)typeof(UIPlayback).GetField("playback", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(preview);
						int frames = 0;
						pb.VideoFrame += (_, _) => System.Threading.Interlocked.Increment(ref frames);

						int entriesBefore = timeline.History.Position;
						field.GrabFocus();
						await Frames(1);
						foreach (string typed in new[] { "H", "He", "Hel", "Hell", "Hello" })
						{
							field.Text = typed;
							field.EmitSignal(TextEdit.SignalName.TextChanged);
							await Frames(1);
							Check(textNode.Content == typed, $"typing '{typed}' changes the text right away");
						}
						for (int i = 0; i < 120 && frames == 0; i++) await Frames(1);
						Check(frames > 0, $"and the preview re-renders while typing ({frames} frames)");
						Check(timeline.History.Position == entriesBefore, "nothing is recorded mid-typing");

						// a click on empty inspector space leaves the field
						Vector2I windowWas = GetWindow().Size;
						GetWindow().Size = new Vector2I(1920, 1080);
						await Frames(3);
						Label emptyish = Find<Label>(Find<InspectorRow>(inspector).Find(r => r.Label == "Font"))[0];
						Vector2 at = emptyish.GetGlobalRect().GetCenter();
						GetViewport().PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at });
						await Frames(1);
						GetViewport().PushInput(new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = MouseButton.Left, Pressed = true });
						GetViewport().PushInput(new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = MouseButton.Left, Pressed = false });
						await Frames(2);
						GetWindow().Size = windowWas;
						Check(!field.HasFocus(), "clicking empty inspector space takes focus out of the field");
						Check(timeline.History.Position == entriesBefore + 1, $"and the typing becomes one entry: {timeline.History.Position - entriesBefore}");
						timeline.History.Undo();
						await Frames(1);
					}

					timeline.History.Undo();
					timeline.History.Undo();
					await Frames(2);

					// the media picker: a second clip's node takes the first's media, and undo gives it back its own
					{
						var second = InputOf(video[1].Clip) as EditSharp.Components.Nodes.Input.VideoMediaNode;
						Check(second is not null && ReferenceEquals(second.Media, originalMedia), "the test clips share one media");

						if (second is not null)
						{
							var other = EditSharp.History.Transaction.Suppressed(() => new EditSharp.Components.Media.VideoMedia { Path = originalMedia.Path, Name = "Second copy" });
							inspector.Media.Add(other);
							using (EditSharp.History.Transaction.Scope change = timeline.History.Begin("Give the second clip its own media"))
							{
								second.Media = other;
								change.Commit();
							}

							clipsView.SelectClip(video[1], SelectionMode.Exclusive);
							await Frames(3);
							InspectorSection section = Find<InspectorSection>(inspector).Find(s => s.MediaDescriptor is not null);
							List<string> mediaItems = section is null ? [] : [.. Enumerable.Range(0, section.Picker.ItemCount).Select(section.Picker.GetItemText)];
							GD.Print("media items: " + string.Join(", ", mediaItems));
							Check(section is not null && section.Picker.Text == "Second copy" && mediaItems.Contains(originalMedia.Name), "the picker shows the clip's media and lists the project's");

							entries = timeline.History.Position;
							section.Picker.EmitSignal(OptionButton.SignalName.ItemSelected, (long)mediaItems.IndexOf(originalMedia.Name));
							await Frames(2);
							Check(ReferenceEquals(second.Media, originalMedia), "picking a media assigns it to the node");
							Check(timeline.History.Position == entries + 1, "in one entry");
							Check(originalMedia.UsedBy.Contains(second) && !other.UsedBy.Contains(second), "and the media know who reads them");

							timeline.History.Undo();
							await Frames(2);
							Check(ReferenceEquals(second.Media, other), "undo gives the node its own media back");
							section = Find<InspectorSection>(inspector).Find(s => s.MediaDescriptor is not null);
							Check(section?.Picker.Text == "Second copy", $"and the picker follows: {section?.Picker.Text}");

							timeline.History.Undo();
							inspector.Media.Remove(other);
							await Frames(2);
							Check(ReferenceEquals(second.Media, originalMedia), "back to the shared media");
						}
					}
				}
			}
		}

		// ---- two touching selected clips: the handles under the cursor stay where they are
		{
			var pair = clipsView.UIClips.GroupBy(u => u.Clip.Channel).Select(g => g.OrderBy(u => u.Clip.Start).Take(2).ToList()).FirstOrDefault(g => g.Count == 2);
			Check(pair is not null, "a channel with two clips");

			if (pair is not null)
			{
				UIClip a = pair[0], b = pair[1];
				int mark = timeline.History.Position;

				// b starts right where a ends
				using (EditSharp.History.Transaction.Scope change = timeline.History.Begin("Butt"))
				{
					b.Clip.Move(a.Clip.End);
					change.Commit();
				}

				// zoomed out so both are on screen, in a window big enough to hold a view
				Vector2I windowWas = GetWindow().Size;
				GetWindow().Size = new Vector2I(1920, 1080);
				await Frames(3);
				double zoomWas = timeline.PixelsPerSecond;
				timeline.PixelsPerSecond = 40;
				clipsView.Reconcile();
				await Frames(3);

				clipsView.SelectClip(a, SelectionMode.Exclusive);
				clipsView.SelectClip(b, SelectionMode.Inclusive);
				InputManager.Singleton.Mouse.CurrentPosition = a.GetGlobalRect().GetCenter();
				await Frames(3);

				Control aEnd = (Control)a.Get("endControls");
				Check(aEnd.IsVisibleInTree(), "the clip under the cursor wears the handles");

				Vector2 onHandle = aEnd.GetGlobalRect().GetCenter();

				// the way a hand goes: from inside the clip across its edge, through the gap, onto the handle
				bool keptAll = true;
				for (float x = a.GetGlobalRect().End.X - 10f; x <= onHandle.X; x += 1f)
				{
					InputManager.Singleton.Mouse.CurrentPosition = new Vector2(x, onHandle.Y);
					await Frames(1);
					if (!aEnd.IsVisibleInTree()) { keptAll = false; GD.Print($"handles lost at x {x} (clip ends {a.GetGlobalRect().End.X}, handle box {aEnd.GetGlobalRect()})"); break; }
				}
				Check(keptAll, "moving the cursor from the clip onto its end handle keeps the handles the whole way");
				Check(b.GetGlobalRect().HasPoint(onHandle) && timeline.ViewContains(onHandle), "the handle overhangs the next clip, in view");
				InputManager.Singleton.Mouse.CurrentPosition = onHandle;
				await Frames(3);
				Check(aEnd.IsVisibleInTree(), "its end handle stays while the cursor is on it, over the next clip");

				InputManager.Singleton.Mouse.CurrentPosition = b.GetGlobalRect().GetCenter();
				await Frames(3);
				Check(!aEnd.IsVisibleInTree() && ((Control)b.Get("endControls")).IsVisibleInTree(), "moving onto the other clip hands them over");

				// nothing selected: still the nearest clip
				clipsView.DeselectAll();
				InputManager.Singleton.Mouse.CurrentPosition = a.GetGlobalRect().GetCenter();
				await Frames(3);
				Check(aEnd.IsVisibleInTree() && !((Control)b.Get("endControls")).IsVisibleInTree(), "with nothing selected the nearest clip wears the handles");

				// grabbing an unselected clip's handle selects it alone
				clipsView.SelectClip(b, SelectionMode.Exclusive);
				InputManager.Singleton.Mouse.CurrentPosition = a.GetGlobalRect().GetCenter();
				await Frames(3);
				UIDragHandle grip = Find<UIDragHandle>(aEnd).First();
				clipsView.BeginEdgeDrag(a, grip, UIClipsView.EdgeDragKind.Extend);
				Check(clipsView.SelectedClips.Count == 1 && clipsView.SelectedClips[0] == a.Clip, "grabbing an unselected clip's handle selects just that clip");
				clipsView.CancelEdgeDrag(grip);
				await Frames(2);

				while (timeline.History.Position > mark) timeline.History.Undo();
				timeline.PixelsPerSecond = zoomWas;
				GetWindow().Size = windowWas;
				await Frames(2);
			}
		}

		// ---- the mock scene builds every editor and control under the palette
		{
			ThemeMockPage mock = GD.Load<PackedScene>("res://Tools/Scenes/Tools/ThemeMock.tscn").Instantiate<ThemeMockPage>();
			AddChild(mock);
			await Frames(2);
			Check(Find<InspectorRow>(mock).Count > 40, $"the mock scene builds a live inspector: {Find<InspectorRow>(mock).Count} rows");
			Check(Find<Button>(mock).Count > 10, "the mock scene has live controls");
			Check(Find<SpinSlider>(mock).Count > 0 && Find<SpinSlider>(mock).All(x => x.GetThemeStylebox("normal") is ThemedStyleBox), "spin sliders are buttons wearing the theme");
			Check(Find<InspectorGlyph>(mock).All(g => g.Icon is not null), "every glyph shows an icon");
			mock.QueueFree();
			await Frames(1);
		}

		// ---- the media viewer, the menus, swatches, drops and waveforms
		{
			MediaViewer viewer = Find<MediaViewer>(editor).FirstOrDefault();
			Check(viewer is not null, "the media viewer is in the page");

			if (viewer is not null)
			{
				await Frames(2);
				List<UIMediaItem> tiles = Find<UIMediaItem>(viewer);
				Check(tiles.Count >= 1 && tiles.All(t => t.Media is EditSharp.Components.Media.VideoMedia), $"{tiles.Count} video tiles for the project's media");

				TabBar tabs = Find<TabBar>(viewer).First();
				tabs.CurrentTab = 2;
				tabs.EmitSignal(TabBar.SignalName.TabChanged, 2L);
				await Frames(2);
				Check(Find<UIMediaItem>(viewer).Any(t => t.Media is EditSharp.Components.Media.AudioMedia), "the Audio tab lists the video's soundtrack");
				tabs.CurrentTab = 3;
				tabs.EmitSignal(TabBar.SignalName.TabChanged, 3L);
				await Frames(2);
				Check(Find<UIMediaItem>(viewer).Any(t => t.Timeline is not null), "the Timelines tab lists the project's timeline");
				tabs.CurrentTab = 4;
				tabs.EmitSignal(TabBar.SignalName.TabChanged, 4L);
				await Frames(2);
				Check(Find<UIMediaItem>(viewer).Count == tiles.Count + 1, "All lists the media and the timeline");
				tabs.CurrentTab = 0;
				tabs.EmitSignal(TabBar.SignalName.TabChanged, 0L);
				await Frames(2);

				// menus load from their resources and are found by id
				var clipMenu = GD.Load<EditSharpGUI.Scripts.UI.ContextMenu.ContextMenu>("res://Menus/Clip.tres");
				Check(clipMenu is not null && clipMenu.Find("clip.cut") is not null && clipMenu.Find<EditSharpGUI.Scripts.UI.ContextMenu.ContextRadioList>("clip.color.swatches")?.Buttons.Count == 18, "the clip menu loads with its ids and 18 swatches");
				Check(clipMenu?.Clone().Find("clip.link") is EditSharpGUI.Scripts.UI.ContextMenu.ContextButton { Type: EditSharpGUI.Scripts.UI.ContextMenu.ContextButton.CheckType.Check }, "a clone keeps the ids and the check type");
				foreach (string name in new[] { "ClipsView", "Channel", "Tabs", "MediaTile", "MediaViewer", "Sort", "Filter" })
					Check(GD.Load<EditSharpGUI.Scripts.UI.ContextMenu.ContextMenu>($"res://Menus/{name}.tres") is { Elements.Count: > 0 }, $"the {name} menu loads");

				// placing media from the viewer: linked video and audio, end to end
				EditSharp.Components.Media.IMedia first = tiles[0].Media;
				int clipsBefore = clipsView.UIClips.Count;
				int mark = timeline.History.Position;
				timeline.PlaceMedia([first, first], TimeSpan.FromSeconds(30), video: true, channelIndex: 0);
				await Frames(2);
				List<UIClip> placed = [.. clipsView.UIClips.Where(u => u.Clip.Start >= TimeSpan.FromSeconds(30))];
				Check(clipsView.UIClips.Count == clipsBefore + 4 && placed.Count == 4, $"two videos placed as four clips: {clipsView.UIClips.Count - clipsBefore}");
				Check(placed.Where(p => p.Clip is EditSharp.Components.Clips.VideoClip).Select(p => p.Clip.Start).Distinct().Count() == 2, "end to end, not stacked");
				Check(placed.All(p => p.Clip.LinkGroupId is not null), "each video is linked to its soundtrack");
				Check(timeline.History.Position == mark + 1, "the placement is one history entry");
				timeline.History.Undo();
				await Frames(2);
				Check(clipsView.UIClips.Count == clipsBefore, "undo takes them out again");

				// a gap at zero pushes everything along
				mark = timeline.History.Position;
				clipsView.InsertGap(TimeSpan.Zero, TimeSpan.FromSeconds(1));
				await Frames(2);
				Check(clipsView.UIClips.All(u => u.Clip.Start >= TimeSpan.FromSeconds(1)), "a gap at zero shifts every clip by a second");
				Check(timeline.History.Position == mark + 1, "in one entry");
				timeline.History.Undo();
				await Frames(2);

				// clip colours: the kind's swatch, a named swatch, a hex value
				UIClip video0 = clipsView.UIClips.First(u => u.Clip is EditSharp.Components.Clips.VideoClip);
				string kind = EditSharpGUI.Scripts.UI.ClipColors.Kind(video0.Clip);
				Check(video0.Clip.Color is null && video0.Color == inspector.GetThemeColor(kind, "Clip"), $"a clip with no colour wears its kind's swatch ({kind})");
				using (EditSharp.History.Transaction.Scope change = timeline.History.Begin("Colour")) { video0.Clip.Color = "poppy"; change.Commit(); }
				video0.Refresh();
				await Frames(1);
				Check(video0.Color == inspector.GetThemeColor("poppy", "Clip"), "a swatch name colours the clip from the theme");
				using (EditSharp.History.Transaction.Scope change = timeline.History.Begin("Colour")) { video0.Clip.Color = "#336699"; change.Commit(); }
				video0.Refresh();
				await Frames(1);
				Check(video0.Color == Color.FromHtml("#336699"), "a hex value colours it directly");
				timeline.History.Undo();
				timeline.History.Undo();
				video0.Refresh();
				await Frames(1);
				Check(video0.Color == inspector.GetThemeColor(kind, "Clip"), "and undo puts the kind colour back");

				// waveforms: the audio clip wears a strip and the cache fills
				UIClip audio0 = clipsView.UIClips.FirstOrDefault(u => u.Clip is EditSharp.Components.Clips.AudioClip);
				Check(audio0 is not null && Find<WaveformStrip>(audio0).Count == 1, "the audio clip carries a waveform strip");
				Check(timeline.Waveforms is WaveformCache, "the waveform cache is wired");
				int w = 0;
				while (timeline.Waveforms.Count == 0 && w < 1800) { await Frames(1); w++; }
				Check(timeline.Waveforms.Count > 0, $"{timeline.Waveforms.Count} clip envelopes built from the analysis after {w} frames");
				await Frames(2);
				Check(audio0 is not null && Find<WaveformStrip>(audio0)[0].Visible && Find<WaveformStrip>(audio0)[0].Material is ShaderMaterial, "the strip shows its envelope through the waveform shader");
				Check(EditSharp.Audio.Analysis.AudioAnalysisCache.TryGet(first.Path, out EditSharp.Audio.Analysis.AudioAnalysis analysis) && analysis.FrameCount > 100 && analysis.Peak.Max() > 0.01f, $"the media's analysis is in memory: {(EditSharp.Audio.Analysis.AudioAnalysisCache.TryGet(first.Path, out var a2) ? a2.FrameCount : 0)} frames");

				// the frequency-domain chain: a gain node halves the envelope
				{
					var audioClip = (EditSharp.Components.Clips.AudioClip)audio0.Clip;
					EditSharp.Audio.Analysis.SpectralEnvelope plain = EditSharp.Audio.Analysis.ClipSpectrum.Evaluate(audioClip, TimeSpan.Zero, 200);
					var gain = EditSharp.History.Transaction.Suppressed(() => new EditSharp.Components.Nodes.Effects.GainNode { Gain = 0.5f });
					EditSharp.Components.Nodes.Node inputNode = audioClip.Graph.AllNodes.OfType<EditSharp.Components.Nodes.Input.AudioMediaNode>().First();
					using (EditSharp.History.Transaction.Scope change = timeline.History.Begin("Gain"))
					{
						audioClip.Graph.AddNode(gain);
						foreach (EditSharp.Components.Nodes.Connection wire in audioClip.Graph.Connections.Where(c => c.ToNodeId == audioClip.Graph.OutputNode.Id).ToList()) audioClip.Graph.Disconnect(wire);
						audioClip.Graph.Connect(inputNode.Id, "Audio", gain.Id, "Audio");
						audioClip.Graph.Connect(gain.Id, "Audio", audioClip.Graph.OutputNode.Id, "Audio");
						change.Commit();
					}
					EditSharp.Audio.Analysis.SpectralEnvelope halved = EditSharp.Audio.Analysis.ClipSpectrum.Evaluate(audioClip, TimeSpan.Zero, 200);
					float ratio = plain is not null && halved is not null && plain.Peak.Max() > 0f ? halved.Peak.Max() / plain.Peak.Max() : -1f;
					Check(Math.Abs(ratio - 0.5f) < 0.01f, $"a gain of 0.5 halves the peak in the frequency domain: {ratio}");

					// a live edit, off history, re-folds the strip's envelope
					int updates = 0;
					timeline.Waveforms.Updated += _ => updates++;
					int w2 = 0;
					while (w2 < 600 && timeline.Waveforms.Count == 0) { await Frames(1); w2++; }
					using (EditSharp.History.Transaction.Suppress()) gain.Gain.StaticValue = 0.25f;
					timeline.Waveforms.RefreshEdited();
					int w3 = 0;
					while (w3 < 600 && updates < 2) { await Frames(1); w3++; }
					Check(updates >= 2, $"a live gain change re-folds the waveform ({updates} updates after {w3} frames)");

					timeline.History.Undo();
					await Frames(2);
				}

				// the theme: swatch items and the playback colour
				ThemePalette pal = theme?.Palette;
				Check(pal is not null && theme.GetColor("navy", "Node") == pal.Resolve(NodeSwatch.Navy), "node swatch items follow the palette");
				Check(pal is not null && theme.GetColor("playhead", "Timeline") == pal.PlaybackColor && theme.GetColor("key", "Inspector") == pal.PlaybackColor, "the playhead and keyframe colours are the playback colour");
				Check(GD.Load<ThemePalette>("res://Themes/Light.tres") is ThemePalette light && light.Resolve(ClipSwatch.Blue) != pal.Resolve(ClipSwatch.Blue), "the light palette tunes its own swatches");

				// the media's tags are an editable list in the inspector, empty or not
				{
					inspector.ShowMedia([first]);
					await Frames(3);
					ListRow tagsRow = Find<ListRow>(inspector).Find(r => r.Label == "Tags");
					Check(tagsRow is not null, "a media shows a Tags list in the inspector");
					if (tagsRow is not null)
					{
						int before = first.Tags.Count;
						Find<Button>(tagsRow).First(b => b.Name == "Add").EmitSignal(BaseButton.SignalName.Pressed);
						await Frames(2);
						Check(first.Tags.Count == before + 1, "the list's add button adds a tag");
						InspectorRow tagRow = Find<InspectorRow>(tagsRow).LastOrDefault();
						tagRow?.Apply("Interview");
						await Frames(1);
						Check(first.Tags.Contains("Interview"), $"typing names it: {string.Join(", ", first.Tags)}");
						timeline.History.Undo();
						timeline.History.Undo();
						await Frames(1);
						Check(first.Tags.Count == before, "undo takes it back out");
					}
					inspector.Clear();
				}

				// tags on two media at once: the shared ones show, edits reach both
				{
					var second = EditSharp.History.Transaction.Suppressed(() => new EditSharp.Components.Media.VideoMedia { Path = first.Path, Name = "Second" });
					using (EditSharp.History.Transaction.Suppress()) { first.AddTag("Both"); first.AddTag("OnlyFirst"); second.AddTag("Both"); }

					inspector.ShowMedia([first, second]);
					await Frames(3);
					ListRow both = Find<ListRow>(inspector).Find(r => r.Label == "Tags");
					Check(both is not null && Find<InspectorRow>(both).Count == 1, $"two media show the tag they share: {(both is null ? 0 : Find<InspectorRow>(both).Count)} rows");

					if (both is not null)
					{
						Find<Button>(both).First(b => b.Name == "Add").EmitSignal(BaseButton.SignalName.Pressed);
						await Frames(2);
						Find<InspectorRow>(both).Last().Apply("Shared");
						await Frames(1);
						Check(first.Tags.Contains("Shared") && second.Tags.Contains("Shared"), "a tag added and named lands on both");

						InspectorRow sharedRow = Find<InspectorRow>(both).First();
						Button removeButton = Find<Button>(sharedRow).FirstOrDefault(b => b.Text == "×");
						removeButton?.EmitSignal(BaseButton.SignalName.Pressed);
						await Frames(2);
						Check(removeButton is not null && !first.Tags.Contains("Both") && !second.Tags.Contains("Both") && first.Tags.Contains("OnlyFirst"), $"removing the shared tag takes it off both and leaves the rest: {string.Join(",", first.Tags)} | {string.Join(",", second.Tags)}");
					}

					using (EditSharp.History.Transaction.Suppress()) { foreach (string t in first.Tags.ToList()) first.RemoveTag(t); }
					inspector.Clear();
				}

				// a still image offers no soundtrack once probed
				{
					string still = System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), "test image horizontal.png");
					var image = EditSharp.History.Transaction.Suppressed(() => new EditSharp.Components.Media.VideoMedia { Path = still });
					int probeWait = 0;
					while (!image.TryGetInfo(out _) && probeWait < 600) { await Frames(1); probeWait++; }
					Check(image.TryGetInfo(out EditSharp.Video.MediaInfo stillInfo) && stillInfo.IsStillImage, $"the png probes as a still after {probeWait} frames");
					Check(image.Audio is null, "and offers no audio");
					Check(first is EditSharp.Components.Media.VideoMedia { Audio: not null }, "while the video keeps its soundtrack");
				}

				// tile pictures come in
				int p = 0;
				while (p < 900 && !Find<TextureRect>(tiles[0]).Any(t => t.Name == "Picture" && t.Texture is not null)) { await Frames(1); p++; }
				Check(Find<TextureRect>(tiles[0]).Any(t => t.Name == "Picture" && t.Texture is not null), $"the video tile got its picture after {p} frames");
			}
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

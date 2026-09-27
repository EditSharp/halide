using Halide.Scripts.Input;
using Godot;
using System.Linq;
using System.Threading.Tasks;

// runs the editor page in a window, opens a few of its menus through the
// godot-drawn handler, and saves screenshots to user://preview-*.png, for
// looking at the layout without a hand on the mouse
//
//   godot --path . res://Tools/Scenes/Tests/Preview.tscn
public partial class PreviewShot : Node
{
	public override async void _Ready()
	{
		GetWindow().Size = new Vector2I(1600, 900);
		// the test project, as a project window would hold it
		if (ProjectSession.Of(this) is null) ProjectSession.Attach(this, Project.FromBlueprint(Tests.TestBlueprint));
		Node editor = GD.Load<PackedScene>("res://Scenes/Views/Editor.tscn").Instantiate();
		AddChild(editor);

		await Frames(300);
		Save("preview-editor");

		// the audio channels, further down the timeline
		{
			UITimeline scrolled = editor.FindChildren("*", "", true, false).OfType<UITimeline>().First();
			scrolled.ScrollViewNow(new Vector2(0f, 420f));
			await Frames(30);
			Save("preview-audio");
			scrolled.ScrollViewNow(new Vector2(0f, -420f));
			await Frames(5);
		}

		// the media viewer's tile menu
		MediaViewer viewer = Find<MediaViewer>(editor).FirstOrDefault();
		UIMediaItem tile = viewer is null ? null : Find<UIMediaItem>(viewer).FirstOrDefault();
		if (tile is not null)
		{
			Button options = Find<Button>(tile).First(b => b.Name == "Options");
			options.EmitSignal(BaseButton.SignalName.Pressed);
			await Frames(20);
			Save("preview-media-menu");
			DismissMenus();
			await Frames(5);
		}

		// a clip's menu from its options button
		UITimeline timeline = editor.FindChildren("*", "", true, false).OfType<UITimeline>().First();
		UIClipsView clipsView = (UIClipsView)timeline.Get("clipsView");
		UIClip clip = clipsView.UIClips.FirstOrDefault(u => u.Clip is EditSharp.Components.Clips.VideoClip);
		if (clip is not null)
		{
			clipsView.SelectClip(clip, Halide.Scripts.UI.SelectionMode.Exclusive);
			Button options = Find<Button>(clip).First(b => b.Name == "Options");
			options.EmitSignal(BaseButton.SignalName.Pressed);
			await Frames(20);
			Save("preview-clip-menu");
			DismissMenus();
			await Frames(5);
		}

		// the media viewer's filter menu
		if (viewer is not null)
		{
			Button filter = Find<Button>(viewer).First(b => b.Name == "Filter");
			filter.EmitSignal(BaseButton.SignalName.Pressed);
			await Frames(20);
			Save("preview-filter-menu");
			DismissMenus();
		}

		GetTree().Quit();
	}

	void DismissMenus()
	{
		foreach (Window w in Find<Window>(GetTree().Root).Where(w => w != GetTree().Root && w.Visible)) w.Hide();
	}

	void Save(string name)
	{
		Image image = GetViewport().GetTexture().GetImage();
		string path = ProjectSettings.GlobalizePath($"user://{name}.png");
		image.SavePng(path);
		GD.Print($"PREVIEW saved {path}");
	}

	async Task Frames(int count)
	{
		for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	static System.Collections.Generic.List<T> Find<T>(Node root) where T : Node
	{
		System.Collections.Generic.List<T> found = [];
		Collect(root, found);
		return found;
	}

	static void Collect<T>(Node node, System.Collections.Generic.List<T> into) where T : Node
	{
		if (node is T match) into.Add(match);
		foreach (Node child in node.GetChildren()) Collect(child, into);
	}
}

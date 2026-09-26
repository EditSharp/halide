using EditSharpGUI.Scripts.UI.ContextMenu;
using Godot;
using System.Linq;
using System.Threading.Tasks;

// opens the media viewer's filter menu through this OS's native handler,
// checks godot keeps running frames while it is up, picks its first check
// item from the keyboard, and reports whether the viewer followed the pick
// while the menu stayed open
//
//   godot --path . res://Tools/Scenes/Tests/MenuProbe.tscn
public partial class MenuProbe : Node
{
	int frames;

	public override void _Process(double delta) => frames++;

	public override async void _Ready()
	{
		GetWindow().Size = new Vector2I(1600, 900);
		Node editor = GD.Load<PackedScene>("res://Scenes/Views/Editor.tscn").Instantiate();
		AddChild(editor);
		await Frames(60);

		MediaViewer viewer = Find<MediaViewer>(editor).First();
		Button filter = Find<Button>(viewer).First(b => b.Name == "Filter");
		int tilesBefore = Find<UIMediaItem>(viewer).Count(t => t.Visible && t.GetParent() is not null);

		filter.EmitSignal(BaseButton.SignalName.Pressed);

		// the menu opens on a deferred call; then a second of frames while it is up
		await Frames(3);
		int at = frames;
		double started = Time.GetTicksMsec();
		while (Time.GetTicksMsec() - started < 1000) await Frames(1);
		int during = frames - at;
		Rect2I? rect = ContextMenus.Handler.OpenMenuRect();
		GD.Print($"MENU open={rect is not null} framesWhileOpen={during}");

		// "Used in a Timeline" is the first item; unchecking it hides the used media
		ContextMenus.Handler.HighlightNext();
		await Frames(5);
		ContextMenus.Handler.ActivateHighlighted();
		await Frames(30);

		int tilesAfter = Find<UIMediaItem>(viewer).Count(t => t.GetParent() is not null);
		bool stillOpen = ContextMenus.Handler.OpenMenuRect() is not null;
		GD.Print($"MENU tilesBefore={tilesBefore} tilesAfter={tilesAfter} reopened={stillOpen}");

		ContextMenus.Handler.Dismiss();
		await Frames(10);

		bool ok = rect is not null && during >= 20 && tilesAfter < tilesBefore && stillOpen;
		GD.Print(ok ? "MENU OK" : "MENU FAIL");
		GetTree().Quit(ok ? 0 : 1);
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

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

		// --clip: the first clip's options button instead; --tile: the media tile's
		string[] args = OS.GetCmdlineUserArgs();
		if (args.Contains("--clip"))
		{
			UITimeline timeline = editor.GetNode<UITimeline>("VSplitContainer/Timeline");
			UIClipsView clipsView = (UIClipsView)timeline.Get("clipsView");
			// the topmost clip whose button is in view: the rows at the bottom sit under the scrollbar
			filter = clipsView.UIClips.Select(c => Find<Button>(c).First(b => b.Name == "Options"))
				.Where(b => b.IsVisibleInTree() && timeline.ViewContains(b.GetGlobalRect().GetCenter()))
				.OrderBy(b => b.GlobalPosition.Y).First();
		}
		else if (args.Contains("--tile"))
		{
			filter = Find<Button>(Find<UIMediaItem>(viewer).First()).First(b => b.Name == "Options");
		}
		int tilesBefore = Find<UIMediaItem>(viewer).Count(t => t.Visible && t.GetParent() is not null);

		// --cascade: open the filter menu, click a clip's options button while it is up (which dismisses
		// the menu; the click should open the clip's), dismiss that, then open the filter menu again
		if (args.Contains("--cascade"))
		{
			UITimeline timeline = editor.GetNode<UITimeline>("VSplitContainer/Timeline");
			UIClipsView clipsView = (UIClipsView)timeline.Get("clipsView");
			Button clipOptions = clipsView.UIClips.Select(c => Find<Button>(c).First(b => b.Name == "Options"))
				.Where(b => b.IsVisibleInTree() && timeline.ViewContains(b.GetGlobalRect().GetCenter()))
				.OrderBy(b => b.GlobalPosition.Y).First();

			bool allOk = true;

			async Task<bool> ClickAndCheck(Control button, string what)
			{
				Vector2I screen = DisplayServer.WindowGetPosition(GetWindow().GetWindowId()) + (Vector2I)(GetViewport().GetScreenTransform() * button.GetGlobalRect().GetCenter()).Round();
				SetCursorPos(screen.X, screen.Y);
				await Frames(3);
				mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
				await Frames(2);
				mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
				double t = Time.GetTicksMsec();
				while (Time.GetTicksMsec() - t < 700) await Frames(1);
				Rect2I? r = ContextMenus.Handler.OpenMenuRect();
				GD.Print($"MENU {what}: open={r is not null} rect={r}");
				return r is not null;
			}

			allOk &= await ClickAndCheck(filter, "filter first");
			Rect2I? firstRect = ContextMenus.Handler.OpenMenuRect();
			allOk &= await ClickAndCheck(clipOptions, "clip options while filter menu open");
			Rect2I? secondRect = ContextMenus.Handler.OpenMenuRect();
			bool different = firstRect != secondRect;
			GD.Print($"MENU the clip menu replaced the filter menu: {different}");
			allOk &= different;

			ContextMenus.Handler.Dismiss();
			await Frames(20);
			GD.Print($"MENU after dismiss open={ContextMenus.Handler.OpenMenuRect() is not null}");
			allOk &= ContextMenus.Handler.OpenMenuRect() is null;

			allOk &= await ClickAndCheck(filter, "filter again");
			ContextMenus.Handler.Dismiss();
			await Frames(10);
			allOk &= await ClickAndCheck(clipOptions, "clip options again");
			ContextMenus.Handler.Dismiss();
			await Frames(10);

			GD.Print(allOk ? "MENU OK" : "MENU FAIL");
			GetTree().Quit(allOk ? 0 : 1);
			return;
		}

		// --click: a real mouse click on the button through the OS, as a hand would do it
		if (OS.GetCmdlineUserArgs().Contains("--click"))
		{
			Vector2 centre = filter.GetGlobalRect().GetCenter();
			Vector2I screen = DisplayServer.WindowGetPosition(GetWindow().GetWindowId()) + (Vector2I)(GetViewport().GetScreenTransform() * centre).Round();
			SetCursorPos(screen.X, screen.Y);
			await Frames(5);
			GD.Print($"MENU clicking {filter.GetPath()} at {screen}, hovered={GetViewport().GuiGetHoveredControl()?.GetPath()}");
			filter.Pressed += () => GD.Print("MENU button pressed");
			mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
			if (!args.Contains("--fast")) await Frames(3);
			mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
		}
		else filter.EmitSignal(BaseButton.SignalName.Pressed);

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

		bool filterMenu = !args.Contains("--clip") && !args.Contains("--tile");
		bool ok = rect is not null && during >= 20 && (!filterMenu || (tilesAfter < tilesBefore && stillOpen));
		GD.Print(ok ? "MENU OK" : "MENU FAIL");
		GetTree().Quit(ok ? 0 : 1);
	}

	const uint MOUSEEVENTF_LEFTDOWN = 0x2, MOUSEEVENTF_LEFTUP = 0x4;
	[System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
	[System.Runtime.InteropServices.DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, uint data, nuint extra);

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

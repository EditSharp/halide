using Godot;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EditSharpGUI.Scripts.App.Platform;
using EditSharpGUI.Scripts.UI.Dialogs;
using EditSharpGUI.Scripts.UI.ContextMenu;

// the multi-window app driven from code; prints WINDOWS OK
public partial class WindowsProbe : Node
{
	bool ok = true;

	// a one-row menu at the middle of a control, its screen rect once open, then closed again
	async Task<Rect2I?> MenuAt(Control control)
	{
		ContextMenu menu = new() { Elements = [new ContextButton { Id = "probe", Text = new("Probe") }] };
		ContextMenus.ShowContextMenu(menu, control, control.GetGlobalRect().GetCenter());

		Rect2I? rect = null;
		double until = Godot.Time.GetTicksMsec() + 5000;
		while ((rect = ContextMenus.Handler.OpenMenuRect()) is null && Godot.Time.GetTicksMsec() < until) await Frames(1);

		ContextMenus.Handler.Dismiss();
		until = Godot.Time.GetTicksMsec() + 5000;
		while (ContextMenus.Handler.OpenMenuRect() is not null && Godot.Time.GetTicksMsec() < until) await Frames(1);
		await Frames(3);
		return rect;
	}

	[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] struct POINT { public int x, y; }
	[System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetCursorPos(out POINT point);
	[System.Runtime.InteropServices.DllImport("user32.dll")] static extern nint SendMessageW(nint hwnd, uint msg, nint wParam, nint lParam);

	void Check(bool condition, string what)
	{
		ok &= condition;
		GD.Print($"PROBE {(condition ? "ok  " : "BAD ")} {what}");
	}

	public override async void _Ready()
	{
		GetTree().Root.GuiEmbedSubwindows = false;
		Callable.From(() => HostWindow.Hide(GetTree().Root)).CallDeferred();

		// nothing waits on a person: dialogs discard, or cancel when there's nothing to discard
		Dialogs.Answering = dialog =>
		{
			string button = dialog.Buttons.FirstOrDefault(b => b.Role == DialogButtonRole.Destructive)?.Id ?? dialog.Cancel?.Id;
			GD.Print($"PROBE dialog \"{dialog.Title}\" ({dialog.Find<DialogText>("message")?.Text}) answered {button}");
			return button;
		};

		// closing the last window would quit mid-probe; kept in memory only, never saved
		AppSettings.UseFile(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"editsharp-settings-{Guid.NewGuid():N}.json"));
		AppSettings.Current.OnLastWindowClosed = LastWindowAction.StayInTray;
		await Frames(2);

		string folder = Path.Combine(Path.GetTempPath(), $"editsharp-windows-{Guid.NewGuid():N}");

		// a list of its own, so the user's recent projects are left alone
		RecentProjects.UseFile(Path.Combine(folder, "projects.json"));
		string path = Path.Combine(folder, "Probe" + ProjectFile.Extension);
		Project source = Project.FromBlueprint(Tests.TestBlueprint);
		ProjectFile.Save(source, path);
		Check(File.Exists(path), $"the test project saves to {path}");

		ProjectManager manager = ProjectManager.Singleton;
		ProjectWindow window = manager.OpenProject(path);
		await Frames(10);
		Check(window is not null && manager.OpenProjects.Count == 1 && window.Visible, "it opens in a window of its own");
		if (Screens.Preferred >= 0) Check(window?.CurrentScreen == Screens.Preferred && GetTree().Root.CurrentScreen == Screens.Preferred, $"on screen {Screens.Preferred}, as EDITSHARP_SCREEN asks: {window?.CurrentScreen}");
		Check(window?.Session.Project.Timeline.Channels.Sum(c => c.Clips.Count) == source.Timeline.Channels.Sum(c => c.Clips.Count), "with every clip");
		Check(window is not null && Descendants(window).OfType<Editor>().Any() && Descendants(window).OfType<UITimeline>().Any(), "and an editor in it");
		Check(ProjectSession.Of(Descendants(window).OfType<UITimeline>().First()) == window.Session, "whose views find their session");
		Check(EditSharp.History.History.Active == window.Session.Project.History, "and whose history is the active one");

		ProjectWindow again = manager.OpenProject(path);
		Check(again == window && manager.OpenProjects.Count == 1, "opening it again focuses the same window");

		Check(!window.Session.Dirty, "it starts with nothing unsaved");
		EditSharp.Components.Clips.Clip clip = window.Session.Project.Timeline.Channels.SelectMany(c => c.Clips).First();
		using (EditSharp.History.Transaction.Scope change = window.Session.Project.History.Begin("move"))
		{
			clip.Move(clip.Start + Time.FromSeconds(1));
			change.Commit();
		}
		await Frames(2);
		Check(window.Session.Dirty && window.Title.StartsWith("•"), $"an edit leaves it unsaved, and the title says so: {window.Title}");

		Check(manager.Save(window.Session) && !window.Session.Dirty, "saving clears that");
		var reloaded = ProjectFile.Load(path);
		Check(reloaded.Project.Timeline.Channels.SelectMany(c => c.Clips).Any(c => c.Id == clip.Id && c.Start == clip.Start), "and the file has the edit");
		Check(RecentProjects.All.FirstOrDefault()?.Path == Path.GetFullPath(path), "the project heads the recent list");

		// the view comes back as it was left
		UITimeline view = Descendants(window).OfType<UITimeline>().First();
		MediaViewer media = Descendants(window).OfType<MediaViewer>().First();
		view.PixelsPerSecond = 173d;
		view.PlayheadTime = Time.FromSeconds(3);
		Guid pick = window.Session.Project.Timeline.Channels.SelectMany(c => c.Clips).Skip(1).First().Id;
		Descendants(window).OfType<UIClipsView>().First().SelectClips(window.Session.Project.Timeline.Channels.SelectMany(c => c.Clips).Where(c => c.Id == pick));
		media.RestoreState(new System.Text.Json.Nodes.JsonObject { ["tab"] = "Audio" });
		Descendants(window).OfType<EditSharpGUI.Scripts.UI.Docking.DockManager>().First().Close("inspector");
		await Frames(3);
		manager.Save(window.Session);
		await window.CloseAsync();
		await Frames(3);

		window = manager.OpenProject(path);
		await Frames(15);
		view = Descendants(window).OfType<UITimeline>().First();
		media = Descendants(window).OfType<MediaViewer>().First();
		Check(view.PixelsPerSecond == 173d && view.PlayheadTime == Time.FromSeconds(3), $"zoom and playhead come back: {view.PixelsPerSecond}, {view.PlayheadTime}");
		Check(view.SelectedClips.Select(c => c.Id).SequenceEqual([pick]), "so does the selection");
		Check(media.SaveState()["tab"]?.GetValue<string>() == "Audio", "and the media viewer's tab");
		Check(!Descendants(window).OfType<EditSharpGUI.Scripts.UI.Docking.DockManager>().First().IsOpen("inspector"), "and the dock layout");

		// the drawn top bar: the project's name, and the caption buttons where Windows draws them
		UITopBar bar = Descendants(window).OfType<UITopBar>().First();
		await Frames(5);
		SaveShot(window, "project-topbar");
		Check(bar.Title == "Probe", $"the top bar shows the project's name: {bar.Title}");
		Check(bar.RegionAt(bar.GetGlobalRect().GetCenter() + new Vector2(-200, 0)) == UITopBar.Region.Caption, "its empty space drags the window");
		if (OS.GetName() == "Windows")
		{
			// what windows itself is told is under the pointer, asked the way it asks
			nint hwnd = (nint)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, window.GetWindowId());
			int HitAt(Vector2 viewport)
			{
				Vector2I screen = EditSharpGUI.Scripts.UI.ContextMenu.ContextMenus.ToScreen(window, viewport);
				GetCursorPos(out POINT now);
				Vector2I win32 = screen + new Vector2I(now.x, now.y) - DisplayServer.MouseGetPosition();
				return (int)SendMessageW(hwnd, 0x84, 0, (nint)((win32.Y << 16) | (win32.X & 0xFFFF)));
			}
			Button maximizeButton = Descendants(bar).OfType<Button>().First(b => b.TooltipText is "Maximize" or "Restore");
			Editor editorView = Descendants(window).OfType<Editor>().First();
			Check(HitAt(bar.GetGlobalRect().GetCenter() + new Vector2(-200, 0)) == 2, "Windows sees the bar's empty space as the caption");
			Check(HitAt(maximizeButton.GetGlobalRect().GetCenter()) == 9, "and the maximize button as its own, for Snap Layouts");
			Check(HitAt(new Vector2(bar.GetGlobalRect().GetCenter().X - 200, 1)) == 12, "the top edge still resizes");
			Check(HitAt(editorView.GetGlobalRect().GetCenter()) == 1, "and the editor is plain client area");

			// the bar is the monitor's size, whatever the interface scale
			float Physical() => bar.GetGlobalTransform().Scale.Y * bar.Size.Y * window.ContentScaleFactor;
			float barBefore = Physical(), scaleBefore = window.ContentScaleFactor;
			InterfaceScale scaleWas = AppSettings.Current.InterfaceScale;
			AppSettings.Current.InterfaceScale = scaleBefore > 1.2f ? InterfaceScale.Percent100 : InterfaceScale.Percent200;
			AppSettings.Current.Save();
			await Frames(5);
			float barAfter = Physical();
			Check(!Mathf.IsEqualApprox(window.ContentScaleFactor, scaleBefore) && Mathf.Abs(barBefore - barAfter) < 1f,
				$"the title bar keeps its height when the interface scale changes ({scaleBefore} to {window.ContentScaleFactor}): {barBefore:0.#} then {barAfter:0.#} px");
			AppSettings.Current.InterfaceScale = scaleWas;
			AppSettings.Current.Save();
			await Frames(5);
		}

		// ---- a channel dragged by its handle ----
		EditSharp.Components.Timeline timeline = window.Session.Project.Timeline;
		using (EditSharp.History.Transaction.Suppress())
			while (timeline.VideoChannels.Count < 3) timeline.AddChannel(new EditSharp.Components.Channels.VideoChannel());
		view.Reconcile();
		await Frames(5);

		int videoCount = timeline.VideoChannels.Count;
		EditSharp.Components.Channels.Channel top = timeline.VideoChannels.Last(c => c.Clips.Count > 0);
		int startIndex = top.Index;
		int topClips = top.Clips.Count;
		UIChannelEdit topEdit = Descendants(view).OfType<UIChannelEdit>().First(e => ReferenceEquals(e.Channel, top));
		float y0 = topEdit.GetGlobalRect().GetCenter().Y;
		float rowHeight = (float)view.VerticalScale;

		view.BeginChannelDrag(topEdit, y0);
		float startRow = videoCount - 1 - startIndex;
		view.UpdateChannelDrag(y0 + rowHeight * 1.5f);
		await Frames(3);
		UIClip lifted = Descendants(view).OfType<UIClip>().First(c => ReferenceEquals(c.Clip.Channel, top));
		Check(Mathf.Abs(lifted.Position.Y - rowHeight * (startRow + 1.5f)) < 1f, $"the dragged channel's clips follow the pointer: {lifted.Position.Y}");
		SaveShot(window, "channel-drag");

		view.UpdateChannelDrag(y0 + rowHeight * 50f);
		await Frames(2);
		view.FinishChannelDrag();
		await Frames(3);
		Check(top.Index == 0 && top.Clips.Count == topClips && timeline.VideoChannels.Count == videoCount, $"dropping moves it within its kind, clips and all, and stops at the audio: index {top.Index}");

		window.Session.Project.History.Undo();
		view.Reconcile();
		await Frames(2);
		Check(top.Index == startIndex, "one undo puts it back");

		topEdit = Descendants(view).OfType<UIChannelEdit>().First(e => ReferenceEquals(e.Channel, top));
		view.BeginChannelDrag(topEdit, y0);
		view.UpdateChannelDrag(y0 + rowHeight * 2f);
		view.CancelChannelDrag();
		await Frames(2);
		Check(top.Index == startIndex && !view.DraggingChannel, "escape leaves it where it was");

		bool closed = await window.CloseAsync();
		await Frames(3);
		Check(closed && manager.OpenProjects.Count == 0, "closing the window closes the project");

		// ---- Home ----
		string homeFolder = Path.Combine(folder, "home");
		Directory.CreateDirectory(homeFolder);
		ProjectWindow a = manager.CreateProject(homeFolder, "Alpha Probe", new EditSharp.Rendering.RenderSettings());
		ProjectWindow b = manager.CreateProject(homeFolder, "Beta Probe", new EditSharp.Rendering.RenderSettings { Resolution = new(1080, 1920) });
		await Frames(5);
		Check(a is not null && b is not null && File.Exists(Path.Combine(homeFolder, "Alpha Probe", "Alpha Probe" + ProjectFile.Extension)), "New projects are made in folders of their own");
		Check(a.Session.Project.Timeline.VideoChannels.Count == 3 && a.Session.Project.Timeline.AudioChannels.Count == 3, "with three video and three audio channels");
		Check(!a.Session.Dirty && !b.Session.Dirty, "and nothing unsaved");

		// the tile pictures land in the background
		string thumbs = Path.Combine(homeFolder, "Alpha Probe", "Thumbnails");
		double until = Godot.Time.GetTicksMsec() + 20000;
		while (!(File.Exists(Path.Combine(thumbs, "poster.jpg")) && File.Exists(Path.Combine(thumbs, "frames.jpg"))) && Godot.Time.GetTicksMsec() < until) await Frames(5);
		Check(File.Exists(Path.Combine(thumbs, "frames.jpg")), "and get their pictures");

		// a real project for a picture worth looking at
		ProjectWindow real = manager.OpenProject(path);
		manager.Save(real.Session);
		string realThumbs = Path.Combine(folder, "Thumbnails", "frames.jpg");
		until = Godot.Time.GetTicksMsec() + 30000;
		while (!File.Exists(realThumbs) && Godot.Time.GetTicksMsec() < until) await Frames(5);
		var (poster, frames) = ProjectThumbnails.Load(folder);
		Check(poster is not null && frames.Length == 10, $"the saved project has a poster and ten frames: {frames.Length}");

		manager.ShowHome();
		await Frames(10);
		Home home = Descendants(GetTree().Root).OfType<Home>().First();
		var tiles = Descendants(home).OfType<UIProjectItem>().ToList();
		Check(tiles.Any(t => t.Project.Name == "Alpha Probe") && tiles.Any(t => t.Project.Name == "Beta Probe") && tiles.Any(t => t.Project.Name == "Probe"), $"Home shows the projects: {string.Join(", ", tiles.Select(t => t.Project.Name))}");

		LineEdit search = Descendants(home).OfType<LineEdit>().First(l => l.PlaceholderText == "Search projects");
		search.Text = "beta";
		search.EmitSignal(LineEdit.SignalName.TextChanged, "beta");
		await Frames(3);
		Check(Descendants(home).OfType<UIProjectItem>().Where(t => !t.IsQueuedForDeletion()).Select(t => t.Project.Name).SequenceEqual(["Beta Probe"]), "searching narrows them");
		search.Text = "";
		search.EmitSignal(LineEdit.SignalName.TextChanged, "");
		await Frames(3);

		HomeSort sortWas = AppSettings.Current.HomeSort;
		AppSettings.Current.HomeSort = HomeSort.Name;
		home.Rebuild();
		await Frames(2);
		var byName = Descendants(home).OfType<UIProjectItem>().Where(t => !t.IsQueuedForDeletion()).OrderBy(t => t.GetIndex()).Select(t => t.Project.Name).ToList();
		Check(byName.IndexOf("Alpha Probe") < byName.IndexOf("Beta Probe"), "sorting by name puts Alpha before Beta");
		AppSettings.Current.HomeSort = sortWas;
		home.Rebuild();

		// renaming a closed project renames its folder and file
		await b.CloseAsync();
		await Frames(3);
		UIProjectItem beta = Descendants(home).OfType<UIProjectItem>().First(t => t.Project.Name == "Beta Probe");
		home.Rename(beta, "Gamma Probe");
		await Frames(3);
		Check(File.Exists(Path.Combine(homeFolder, "Gamma Probe", "Gamma Probe" + ProjectFile.Extension)) && RecentProjects.All.Any(p => p.Name == "Gamma Probe"), "renaming moves the folder and the file, and the list follows");

		await Frames(10);
		SaveShot(home.GetWindow(), "home");

		// menus open where they're asked in Home's window, wherever it has moved to
		UIProjectItem gamma = Descendants(home).OfType<UIProjectItem>().First(t => t.Project.Name == "Gamma Probe");
		Rect2I? first = await MenuAt(gamma);
		Vector2I asked = ContextMenus.ToScreen(gamma.GetViewport(), gamma.GetGlobalRect().GetCenter());
		Check(first is Rect2I r1 && (r1.Position - asked).Length() <= 2, $"a menu opens where Home asks: {first?.Position} for {asked}");
		home.GetWindow().Position += new Vector2I(120, 60);
		await Frames(5);
		Rect2I? moved = await MenuAt(gamma);
		Check(first is Rect2I before && moved is Rect2I after && after.Position - before.Position == new Vector2I(120, 60), $"and follows the window when it moves: {first?.Position} then {moved?.Position}");

		// a tile's menu from a right click opens at the cursor, not at the view's corner
		UIProjectItem gammaTile = Descendants(home).OfType<UIProjectItem>().First(t => t.Project.Name == "Gamma Probe");
		Vector2I cursor = ContextMenus.ToScreen(gammaTile.GetViewport(), gammaTile.GetGlobalRect().GetCenter());
		gammaTile.GetViewport().WarpMouse(gammaTile.GetGlobalRect().GetCenter());
		await Frames(3);
		home.ShowTileMenu(gammaTile, null);
		Rect2I? tileMenu = null;
		double menuUntil = Godot.Time.GetTicksMsec() + 5000;
		while ((tileMenu = ContextMenus.Handler.OpenMenuRect()) is null && Godot.Time.GetTicksMsec() < menuUntil) await Frames(1);
		ContextMenus.Handler.Dismiss();
		await Frames(5);
		Check(tileMenu is Rect2I tm && (tm.Position - cursor).Length() <= 4, $"a tile's own menu opens at the cursor: {tileMenu?.Position} for {cursor}");

		// the new project form: a name that's taken is refused, a free one is made
		Dialog form = NewProjectDialog.Build();
		form.Find<DialogPathField>("location").Value = homeFolder;
		form.Find<DialogTextField>("name").Value = "Alpha Probe";
		Check(form.Problem() is not null, $"the form refuses a name that's already a folder there: {form.Problem()}");
		form.Find<DialogTextField>("name").Value = "Delta Probe";
		Check(form.Problem() is null, "and takes a new one");
		form.UserEdited(form.Find<DialogDropdown>("resolution"), 4);
		form.UserEdited(form.Find<DialogNumber>("customRate"), 29.97);
		Check(form.Find<DialogNumber>("width").Visible && !form.Find<DialogCheckbox>("vertical").Visible && !form.Find<DialogDropdown>("rate").Visible,
			"Custom shows the size and rate fields and hides Vertical and the rate presets");
		Check(form.Find<DialogText>("summary").Text == "1920 × 1080, 29.97 fps", $"and a typed 29.97 is the NTSC rate: {form.Find<DialogText>("summary").Text}");

		// answered the way a person would: typed in, then Create
		Func<Dialog, string> answering = Dialogs.Answering;
		Dialogs.Answering = d =>
		{
			d.Find<DialogPathField>("location").Value = homeFolder;
			d.Find<DialogTextField>("name").Value = "Delta Probe";
			return "create";
		};
		await NewProjectDialog.ShowAsync(home);
		Dialogs.Answering = answering;
		await Frames(10);
		Check(manager.OpenProjects.Any(w => w.Session.FilePath?.EndsWith("Delta Probe" + ProjectFile.Extension) == true), "Create makes the project and opens it");
		Check(!IsInstanceValid(home) || home.IsQueuedForDeletion() || home.GetWindow().IsQueuedForDeletion(), "and Home closes");

		foreach (ProjectWindow w in manager.OpenProjects.ToList()) await w.CloseAsync();
		AppSettings.Current.ProjectsFolder = AppSettings.Load().ProjectsFolder;

		// the pictures, kept for looking at
		foreach (string jpg in Directory.GetFiles(folder, "*.jpg", SearchOption.AllDirectories)) File.Copy(jpg, ProjectSettings.GlobalizePath($"user://probe-{Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(jpg)))}-{Path.GetFileName(jpg)}"), true);
		try { Directory.Delete(folder, recursive: true); } catch (IOException) { }

		GD.Print(ok ? "WINDOWS OK" : "WINDOWS FAILED");
		GetTree().Quit();
	}

	// a window's picture, for looking at
	static void SaveShot(Window window, string name)
	{
		string file = ProjectSettings.GlobalizePath($"user://probe-{name}.png");
		window.GetTexture().GetImage().SavePng(file);
		GD.Print($"PROBE shot {file}");
	}

	static System.Collections.Generic.IEnumerable<Node> Descendants(Node node)
	{
		foreach (Node child in node.GetChildren())
		{
			yield return child;
			foreach (Node below in Descendants(child)) yield return below;
		}
	}

	async Task Frames(int count)
	{
		for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}
}

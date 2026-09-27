using Halide.Scripts.App.Platform;
using Halide.Scripts.UI.Dialogs;
using Halide.Scripts.UI.Docking;
using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

// drives tab drags through a project window's dock layout with a steered pointer, no OS input, and checks where views land;
// saves what each window drew beside --shots=DIR
public partial class DockProbe : Node
{
	ProjectWindow window;
	DockManager docks;
	Vector2I pointer;
	bool held;
	string shots;
	int failures;
	bool ghostShown, tabDimmed;

	public override async void _Ready()
	{
		string[] args = OS.GetCmdlineUserArgs();
		shots = args.FirstOrDefault(a => a.StartsWith("--shots="))?["--shots=".Length..];
		if (shots is not null) Directory.CreateDirectory(shots);

		GetTree().Root.GuiEmbedSubwindows = false;
		HostWindow.Hide(GetTree().Root);
		string folder = Path.Combine(Path.GetTempPath(), $"editsharp-dock-{Guid.NewGuid():N}");
		AppSettings.UseFile(Path.Combine(folder, "settings.json"));
		AppSettings.Current.OnLastWindowClosed = LastWindowAction.StayInTray;
		RecentProjects.UseFile(Path.Combine(folder, "projects.json"));
		Halide.Scripts.UI.Theming.EditSharpTheme.SettingsFile = Path.Combine(folder, "theme.json");
		Dialogs.Answering = d => d.Buttons.FirstOrDefault(b => b.Role == DialogButtonRole.Destructive)?.Id ?? d.Cancel?.Id;
		DockDrag.Pointer = () => pointer;
		DockDrag.Held = () => held;

		// never left hanging: whatever happens, the windows close and the probe ends
		GetTree().CreateTimer(60).Timeout += () =>
		{
			GD.PrintErr("DOCK PROBE TIMED OUT");
			foreach (Window w in GetTree().Root.FindChildren("*", "Window", true, false).OfType<Window>()) w.QueueFree();
			GetTree().Quit(2);
		};

		try { await Run(folder); }
		catch (Exception e)
		{
			GD.PrintErr($"DOCK PROBE CRASHED {e}");
			failures++;
		}
		finally
		{
			held = false;
			foreach (ProjectWindow w in ProjectManager.Singleton.OpenProjects.ToArray()) w.QueueFree();
			try { Directory.Delete(folder, true); } catch (IOException) { }
			GD.Print(failures == 0 ? "DOCK PROBE PASSED" : $"DOCK PROBE FAILED ({failures})");
			GetTree().Quit(failures == 0 ? 0 : 1);
		}
	}

	async System.Threading.Tasks.Task Run(string folder)
	{
		await Frames(3);

		window = ProjectManager.Singleton.CreateProject(folder, "Dock Probe", new EditSharp.Rendering.RenderSettings());
		Rect2I usable = DisplayServer.ScreenGetUsableRect(window.CurrentScreen);
		window.Size = new Vector2I(Math.Min(1200, usable.Size.X - 96), Math.Min(720, usable.Size.Y - 96));
		window.Position = usable.Position + (usable.Size - window.Size) / 2;
		await Wait(1.5);
		docks = window.FindChild("Docks", true, false) as DockManager;
		JsonObject initial = docks.Save();
		Shot(window, "default");
		// the close button's glyph fades to white and back without a tween error
		CaptionButton close = window.FindChildren("*", "", true, false).OfType<CaptionButton>().First(b => b.TooltipText == "Close");
		close.EmitSignal(Control.SignalName.MouseEntered);
		await Wait(0.3);
		Check("close glyph lit", close.GetThemeColor("font_color").IsEqualApprox(close.GetThemeColor("glyph_hot")));
		close.EmitSignal(Control.SignalName.MouseExited);
		await Wait(0.4);
		Check("close glyph back at rest", !close.GetThemeColor("font_color").IsEqualApprox(close.GetThemeColor("glyph_hot")));

		Check("small enough for snap thirds", window.MinSize.X <= 640);

		// the inspector onto the program's left tile: a new pane left of it
		await Drag("inspector", async () => await ToTile(window, "program", DockSide.Left), "compass");
		Check("a tab ghost followed the pointer", ghostShown);
		Check("the dragged tab was dimmed", tabDimmed);
		Check("neither is left after the drop", !window.FindChildren("*", "", true, false).OfType<DockTabGhost>().Any() && !window.FindChildren("*", "", true, false).OfType<TabBar>().Any(t => Enumerable.Range(0, t.TabCount).Any(t.IsTabDisabled)));
		DockTree main = Main();
		DockStack inspector = main.StackOf("inspector");
		Check("inspector split left of program", inspector?.Parent is { Vertical: false } s && s.First == inspector && s.Second.Views.Contains("program"));

		// the timeline onto the end of the media tabs
		await Drag("timeline", async () => { pointer = ToScreen(window, StripEnd(window, "media")); await Frames(3); }, "marker");
		Check("timeline joined the media tabs", Main().StackOf("media")?.ViewIds.SequenceEqual(["media", "timeline"]) == true);

		// the media out past every window: a float of its own
		await Drag("media", async () => { pointer = window.Position + window.Size + new Vector2I(60, 60); await Frames(3); }, null);
		DockFloatWindow floating = Floats().FirstOrDefault();
		Check("media floated", floating is not null && Floats().Count == 1 && !Main().Contains("media"));
		await Wait(0.5);
		if (floating is not null) Shot(floating, "float");

		// a lone pane's tabs sit in the float's bar
		Check("the float's tabs are in its bar", floating is not null && floating.Frame.Bar.FindChildren("*", "TabBar", true, false).Count == 1);
		TabBar barTabs = floating?.Frame.Bar.FindChildren("*", "TabBar", true, false).OfType<TabBar>().FirstOrDefault();
		Check("they take the bar's free space", barTabs is not null && barTabs.Size.X > floating.Frame.Bar.Size.X / 2);

		// a float's only view can't float again, by menu or by dragging out
		Check("its only view can't float", !docks.CanFloat("media"));
		if (floating is not null)
		{
			docks.Float("media");
			pointer = ToScreen(floating, barTabs.GetGlobalTransform() * (barTabs.GetTabRect(0).GetCenter()));
			held = true;
			docks.BeginDrag("media");
			await Frames(2);
			pointer = floating.Position + floating.Size + new Vector2I(60, 60);
			await Frames(3);
			held = false;
			await Frames(3);
			Check("nor by dragging it out", Floats().Count == 1 && Floats()[0] == floating && docks.Save()["floats"]!.AsArray().Count == 1);
		}

		// media picked up in the float lands on the main window's timeline, the ghost following it across
		if (floating is not null)
		{
			Control from = floating.FindChildren("*", "", true, false).OfType<MediaViewer>().First();
			Halide.Scripts.UI.DragDrop.DragDrop.Pointer = () => pointer;
			Halide.Scripts.UI.DragDrop.DragDrop.Held = () => held;
			held = true;
			Halide.Scripts.UI.DragDrop.DragDrop.Begin(new Halide.Scripts.UI.DragDrop.MediaPayload([]), new ColorRect { Size = new Vector2(60, 36) }, new Vector2(30, 18), from);
			pointer = ToScreen(floating, from.GetGlobalRect().GetCenter());
			await Frames(3);
			Check("the ghost starts in the float", Halide.Scripts.UI.DragDrop.DragDrop.Ghost?.GetParent() == floating);

			UIClipsView clips = window.FindChildren("*", "", true, false).OfType<UIClipsView>().First();
			pointer = ToScreen(window, clips.GetGlobalRect().GetCenter());
			await Frames(3);
			Check("the ghost hops to the main window", Halide.Scripts.UI.DragDrop.DragDrop.Ghost?.GetParent() == window);
			Check("the main window's timeline would take it", Halide.Scripts.UI.DragDrop.DragDrop.CanDropAt(Vector2.Zero));
			Shot(window, "cross-window");
			Halide.Scripts.UI.DragDrop.DragDrop.Cancel();
			held = false;
			await Frames(2);
		}

		// the inspector into the float, right of the media
		if (floating is not null)
		{
			await Drag("inspector", async () => await ToTile(floating, "media", DockSide.Right), "float-compass", floating);
			JsonObject saved = docks.Save();
			DockTree inFloat = new(DockNode.FromJson(saved["floats"]![0]!["layout"]));
			Check("the float split for the inspector", inFloat.Root is DockSplit { Vertical: false } fs && fs.First.Views.Contains("media") && fs.Second.Views.Contains("inspector"));
			await Wait(0.3);
			Shot(floating, "float-split");

			// closing the float docks both back where they came from
			floating.EmitSignal(Window.SignalName.CloseRequested);
			await Frames(3);
			main = Main();
			Check("the float closed", Floats().Count == 0);
			Check("media back among its old tab mates", main.StackOf("media")?.ViewIds.Contains("timeline") == true);
			Check("inspector back left of program", main.StackOf("inspector")?.Parent is { Vertical: false } b && b.First == main.StackOf("inspector") && b.Second.Views.Contains("program"));
		}

		// a closed view reopens where it was
		docks.Close("timeline");
		Check("timeline closed", !docks.IsOpen("timeline"));
		docks.Open("timeline");
		Check("timeline reopened with the media", Main().StackOf("timeline")?.ViewIds.Contains("media") == true);

		// saved and restored, the same layout
		string before = docks.Save().ToJsonString();
		docks.Restore(JsonNode.Parse(before)!.AsObject());
		Check("a restore keeps the layout", docks.Save().ToJsonString() == before);

		docks.Reset();
		await Frames(2);
		Check("a reset goes back to the default", docks.Save().ToJsonString() == initial.ToJsonString());
		Shot(window, "reset");

	}

	// picks up `view`'s tab, lets `move` steer the pointer, shows what the target drew, and lets go
	async System.Threading.Tasks.Task Drag(string view, Func<System.Threading.Tasks.Task> move, string shot, Window shown = null)
	{
		pointer = ToScreen(window, PaneOf(window, view)?.GetGlobalRect().GetCenter() ?? Vector2.Zero);
		held = true;
		docks.BeginDrag(view);
		await Frames(2);
		await move();
		ghostShown = (shown ?? window).FindChildren("*", "", true, false).OfType<DockTabGhost>().Any(g => g.Visible);
		tabDimmed = window.FindChildren("*", "", true, false).OfType<TabBar>().Any(t => Enumerable.Range(0, t.TabCount).Any(t.IsTabDisabled));
		if (shot is not null) Shot(shown ?? window, shot);
		held = false;
		await Frames(3);
	}

	// over the pane holding `view`, then onto the compass tile for `side`
	async System.Threading.Tasks.Task ToTile(Window at, string view, DockSide side)
	{
		DockPane pane = PaneOf(at, view);
		pointer = ToScreen(at, pane.GetGlobalRect().GetCenter() + new Vector2(0, 14));
		await Frames(3);
		DockCompassTile tile = at.FindChildren("*", "", true, false).OfType<DockCompassTile>().First(t => t.Side == side && t.IsVisibleInTree());
		pointer = ToScreen(at, tile.GetGlobalRect().GetCenter());
		await Frames(3);
	}

	static DockPane PaneOf(Window at, string view) =>
		at.FindChildren("*", "", true, false).OfType<DockPane>().FirstOrDefault(p => p.Stack?.ViewIds.Contains(view) == true);

	// just inside the right end of the strip holding `view`'s tab
	static Vector2 StripEnd(Window at, string view)
	{
		Rect2 strip = PaneOf(at, view).StripRect;
		return new Vector2(strip.End.X - 60, strip.GetCenter().Y);
	}

	static Vector2I ToScreen(Window at, Vector2 global) => at.Position + (Vector2I)(global * at.ContentScaleFactor).Round();

	DockTree Main() => new(DockNode.FromJson(docks.Save()["main"]));

	List<DockFloatWindow> Floats() => [.. window.GetChildren().OfType<DockFloatWindow>().Where(f => !f.IsQueuedForDeletion())];

	void Check(string what, bool ok)
	{
		Console.Out.Flush();
		GD.Print($"{(ok ? "PASS" : "FAIL")} {what}");
		if (!ok) failures++;
	}

	void Shot(Window at, string name)
	{
		if (shots is null) return;
		at.GetTexture().GetImage().SavePng(Path.Combine(shots, $"dock-{name}.png"));
	}

	async System.Threading.Tasks.Task Wait(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

	async System.Threading.Tasks.Task Frames(int count)
	{
		for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}
}

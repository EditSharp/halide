using Halide.Api;
using Halide.Scripts.UI.Docking;
using Halide.Scripts.UI.DragDrop;
using Godot;
using System.Linq;
using System.Threading.Tasks;

namespace Halide.Tests;

// dragging tabs with a steered pointer (no OS input): the compass, the tab strip marker, floating out, the ghost, cross-window media
[TestFixture]
[Windowed]
public sealed class DockDragTests
{
	ProjectHandle project;
	Vector2I pointer;
	bool held;

	Window Main => project.Window;

	[SetUp]
	public async Task Open()
	{
		project = await TestApp.NewProjectAsync();
		if (Main.Mode != Window.ModeEnum.Windowed)
		{
			Main.Mode = Window.ModeEnum.Windowed;
			await TestApp.Seconds(0.5);
		}
		Rect2I usable = DisplayServer.ScreenGetUsableRect(Main.CurrentScreen);
		Main.Size = new Vector2I(System.Math.Min(1200, usable.Size.X - 96), System.Math.Min(720, usable.Size.Y - 96));
		Main.Position = usable.Position + (usable.Size - Main.Size) / 2;
		DockDrag.Pointer = () => pointer;
		DockDrag.Held = () => held;
		DragDrop.Pointer = () => pointer;
		DragDrop.Held = () => held;
		await TestApp.Seconds(0.5);
	}

	static Vector2I ToScreen(Window at, Vector2 global) => at.Position + (Vector2I)(global * at.ContentScaleFactor).Round();

	static DockPane PaneOf(Window at, string view) =>
		at.FindChildren("*", "", true, false).OfType<DockPane>().FirstOrDefault(p => p.Stack?.ViewIds.Contains(view) == true);

	async Task Pick(string view)
	{
		pointer = ToScreen(Main, PaneOf(Main, view).GetGlobalRect().GetCenter());
		held = true;
		project.Editor.Docks.BeginDrag(view);
		await TestApp.Frames(2);
	}

	async Task Release()
	{
		held = false;
		await TestApp.Frames(3);
	}

	async Task OntoTile(Window at, string view, DockSide side)
	{
		pointer = ToScreen(at, PaneOf(at, view).GetGlobalRect().GetCenter() + new Vector2(0, 14));
		await TestApp.Frames(3);
		DockCompassTile tile = at.FindChildren("*", "", true, false).OfType<DockCompassTile>().First(t => t.Side == side && t.IsVisibleInTree());
		pointer = ToScreen(at, tile.GetGlobalRect().GetCenter());
		await TestApp.Frames(3);
	}

	DockTree Tree => new(DockNode.FromJson(project.Layout.Capture()["main"]));

	[Test]
	public async Task DroppingOnASideTileSplitsThatPane()
	{
		await Pick("inspector");
		await OntoTile(Main, "program", DockSide.Left);
		Assert.True(Main.FindChildren("*", "", true, false).OfType<DockTabGhost>().Any(g => g.Visible), "a tab ghost follows the pointer");
		Assert.True(Main.FindChildren("*", "", true, false).OfType<TabBar>().Any(t => Enumerable.Range(0, t.TabCount).Any(t.IsTabDisabled)), "the dragged tab is dimmed");
		await Release();

		DockStack inspector = Tree.StackOf("inspector");
		Assert.True(inspector.Parent is { Vertical: false } s && s.First == inspector && s.Second.Views.Contains("program"));
		Assert.False(Main.FindChildren("*", "", true, false).OfType<DockTabGhost>().Any(), "the ghost is gone");
	}

	[Test]
	public async Task DroppingOnATabStripJoinsTheTabs()
	{
		await Pick("timeline");
		Rect2 strip = PaneOf(Main, "media").StripRect;
		pointer = ToScreen(Main, new Vector2(strip.End.X - 60, strip.GetCenter().Y));
		await TestApp.Frames(3);
		await Release();
		Assert.Sequence(["media", "timeline"], Tree.StackOf("media").ViewIds);
	}

	[Test]
	public async Task DroppingOutsideEveryWindowFloats()
	{
		await Pick("media");
		pointer = Main.Position + Main.Size + new Vector2I(60, 60);
		await TestApp.Frames(3);
		await Release();
		Assert.True(project.Layout.IsFloating("media"));
	}

	[Test]
	public async Task EscapeOrReleasingOverNothingChangesNothing()
	{
		string before = project.Layout.Capture().ToJsonString();
		await Pick("inspector");
		pointer = ToScreen(Main, PaneOf(Main, "program").GetGlobalRect().GetCenter() + new Vector2(200, 100));
		await TestApp.Frames(3);
		await Release();
		Assert.Equal(before, project.Layout.Capture().ToJsonString());
	}

	[Test]
	public async Task AFloatsTabsSitInItsBarAndItSplits()
	{
		project.Layout.Float("media");
		await TestApp.Seconds(0.5);
		DockFloatWindow floating = Main.GetChildren().OfType<DockFloatWindow>().Single();
		TabBar barTabs = floating.Frame.Bar.FindChildren("*", "TabBar", true, false).OfType<TabBar>().SingleOrDefault();
		Assert.NotNull(barTabs, "a lone pane's tabs are in the bar");
		Assert.True(barTabs.Size.X > floating.Frame.Bar.Size.X / 2, "taking the free space");

		await Pick("inspector");
		await OntoTile(floating, "media", DockSide.Right);
		await Release();
		Assert.True(project.Layout.IsFloating("inspector"), "it joined the float");
		Assert.Count(0, floating.Frame.Bar.FindChildren("*", "TabBar", true, false), "a split float keeps tabs on its panes");
	}

	[Test]
	public async Task MediaDragsCrossIntoAnotherWindow()
	{
		project.Layout.Float("media");
		await TestApp.Seconds(0.5);
		DockFloatWindow floating = Main.GetChildren().OfType<DockFloatWindow>().Single();
		Control from = floating.FindChildren("*", "", true, false).OfType<MediaViewer>().First();

		held = true;
		DragDrop.Begin(new MediaPayload([]), new ColorRect { Size = new Vector2(60, 36) }, new Vector2(30, 18), from);
		pointer = ToScreen(floating, from.GetGlobalRect().GetCenter());
		await TestApp.Frames(3);
		Assert.Equal(floating, DragDrop.Ghost?.GetParent());

		Control clips = Main.FindChildren("*", "", true, false).OfType<UIClipsView>().First();
		pointer = ToScreen(Main, clips.GetGlobalRect().GetCenter());
		await TestApp.Frames(3);
		Assert.Equal(Main, DragDrop.Ghost?.GetParent(), "the ghost hopped windows");
		Assert.True(DragDrop.CanDropAt(Vector2.Zero), "the timeline in the other window would take it");
		DragDrop.Cancel();
	}
}

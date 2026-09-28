using Halide.Api;
using Halide.Scripts.App.Chrome;
using Godot;
using System.Linq;
using System.Threading.Tasks;

namespace Halide.Tests;

// the drawn top bar: what's draggable, what's clickable, and what the windows put in it
[TestFixture]
public sealed class ChromeTests
{
	UITopBar bar;
	Window host;

	[SetUp]
	public async Task Build()
	{
		host = new() { Size = new Vector2I(1000, 200), Unfocusable = true };
		TestApp.Tree.Root.AddChild(host);
		bar = GD.Load<PackedScene>("res://Scenes/Components/TopBar.tscn").Instantiate<UITopBar>();
		host.AddChild(bar);
		bar.Size = new Vector2(1000, 36);
		bar.SetMetrics(36, 46);
		await TestApp.Frames(2);
	}

	[TearDown]
	public void Remove() => host.QueueFree();

	static Vector2 Middle(Control c) => c.GetGlobalTransform() * (c.Size / 2);

	// the bar is drawn at the system scale whatever the interface scale, so its text must be rasterized at the drawn size
	[Test]
	public void TextIsRasterizedAtTheBarsScale()
	{
		Assert.Equal(CanvasItem.OversamplingWithScaleEnum.Enabled, bar.OversamplingWithScale);
		foreach (CanvasItem item in bar.FindChildren("*", "CanvasItem", true, false).OfType<CanvasItem>())
			Assert.NotEqual(CanvasItem.OversamplingWithScaleEnum.Disabled, item.OversamplingWithScale, $"{item.Name} follows the bar");
	}

	[Test]
	public async Task HomesLocalAndCloudTabsSitInItsBar()
	{
		ProjectManager.Singleton.ShowHome();
		await TestApp.Frames(3);
		HomeWindow home = TestApp.Tree.Root.GetChildren().OfType<HomeWindow>().First();
		UITopBar homeBar = home.FindChildren("*", "", true, false).OfType<UITopBar>().First();
		TabBar tabs = Assert.NotNull(homeBar.FindChildren("*", "TabBar", true, false).OfType<TabBar>().FirstOrDefault(), "the tabs are in the bar");
		Assert.Sequence(["Local", "Cloud"], Enumerable.Range(0, tabs.TabCount).Select(tabs.GetTabTitle));
		Assert.Equal(UITopBar.Region.None, homeBar.RegionAt(tabs.GetGlobalTransform() * tabs.GetTabRect(0).GetCenter()), "a tab takes clicks");

		// past the last tab: still the bar's own draggable space, not swallowed by the row holding the tabs
		Vector2 pastTabs = tabs.GetGlobalTransform() * new Vector2(tabs.Size.X - 4, tabs.Size.Y / 2);
		Assert.Equal(UITopBar.Region.Caption, homeBar.RegionAt(pastTabs), "empty space past the tabs still drags the window");
	}

	[Test]
	public void EmptySpaceDragsTheWindow()
	{
		Assert.Equal(UITopBar.Region.Caption, bar.RegionAt(new Vector2(500, 18)));
		Assert.Equal(UITopBar.Region.None, bar.RegionAt(new Vector2(500, 300)), "outside the bar");
	}

	[Test]
	public void ButtonsAreClickableNotCaption()
	{
		Button logo = bar.FindChildren("Logo", "", true, false).OfType<Button>().Single();
		Assert.Equal(UITopBar.Region.None, bar.RegionAt(Middle(logo)));
		Button menu = bar.AddMenu("File");
		bar.Size = new Vector2(1000, 36);
		Assert.Equal(UITopBar.Region.None, bar.RegionAt(Middle(menu)));
	}

	[Test]
	public void MaximizeIsItsOwnRegion()
	{
		bar.ShowsCaptionButtons = true;
		Button maximize = bar.FindChildren("Maximize", "", true, false).OfType<Button>().Single();
		Assert.Equal(UITopBar.Region.Maximize, bar.RegionAt(Middle(maximize)));
	}

	[Test]
	public async Task HeldTabsAreClickableButTheSpacePastThemDrags()
	{
		HBoxContainer strip = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		TabBar tabs = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TabAlignment = TabBar.AlignmentMode.Left };
		tabs.AddTab("Media");
		strip.AddChild(tabs);
		bar.Hold(strip);
		await TestApp.Frames(2);

		Vector2 onTab = tabs.GetGlobalTransform() * tabs.GetTabRect(0).GetCenter();
		Assert.Equal(UITopBar.Region.None, bar.RegionAt(onTab), "the tab is clickable");
		Vector2 pastTabs = tabs.GetGlobalTransform() * new Vector2(tabs.Size.X - 5, tabs.Size.Y / 2);
		Assert.Equal(UITopBar.Region.Caption, bar.RegionAt(pastTabs), "the space past the tabs drags");

		Label title = bar.FindChildren("Title", "", true, false).OfType<Label>().Single();
		Assert.False(title.Visible, "the title makes way");
		bar.Hold(null);
		Assert.True(title.Visible);
	}

	[Test]
	public void AnInertLogoIsntClickable()
	{
		bar.LogoInert = true;
		Button logo = bar.FindChildren("Logo", "", true, false).OfType<Button>().Single();
		Assert.Equal(Control.MouseFilterEnum.Ignore, logo.MouseFilter);
	}

	[Test]
	public void TheLayoutSwitcherShowsItsName()
	{
		bar.LayoutName = "Audio";
		Assert.True(bar.LayoutsAnchor.Visible);
		Assert.Equal("Audio", ((Button)bar.LayoutsAnchor).Text);
		bar.LayoutName = null;
		Assert.False(bar.LayoutsAnchor.Visible);
	}

	[Test]
	public async Task AProjectWindowHasMenusAndItsLayout()
	{
		ProjectHandle project = await TestApp.NewProjectAsync();
		UITopBar projectBar = project.Window.Frame.Bar;
		if (OS.GetName() != "macOS")
		{
			string[] menus = [.. projectBar.FindChildren("*", "Button", true, false).OfType<Button>().Where(b => b.ThemeTypeVariation == "TopBarButton" && b != projectBar.LayoutsAnchor).Select(b => b.Text)];
			Assert.Sequence(["File", "Edit", "View", "Playback"], menus);
		}
		Assert.Equal("Editing", ((Button)projectBar.LayoutsAnchor).Text);
		project.Layout.Apply("Audio");
		Assert.Equal("Audio", ((Button)projectBar.LayoutsAnchor).Text, "follows the active layout");
	}

	[Test]
	public void TheBarHasARealHeight()
	{
		(float height, float captionWidth, float scale) = WindowChrome.Metrics(host);
		Assert.True(height >= 27, $"tall enough for tabs and menus: {height}");
		Assert.True(captionWidth > 0 && scale > 0);
	}
}

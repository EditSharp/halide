using EditSharpGUI.Api;
using EditSharpGUI.Scripts.App.Chrome;
using EditSharpGUI.Scripts.App.Commands;
using EditSharpGUI.Scripts.Input;
using EditSharpGUI.Scripts.UI.ContextMenu;
using Godot;
using System.Linq;
using System.Threading.Tasks;

namespace EditSharpGUI.Tests;

// the bar menus as they open: titles, what's greyed out and ticked, shortcut hints, and what extensions add
[TestFixture]
public sealed class MenuTests
{
	ProjectHandle project;

	[SetUp]
	public async Task Open() => project = await TestApp.BlueprintProjectAsync();

	ContextMenu Build(string name) => BarMenus.Build(name, project.Context);

	static ContextButton Item(ContextMenu menu, string id) => Assert.NotNull(menu.Find<ContextButton>(id), $"'{id}' is in the menu");

	[Test]
	public void EveryMenuBuildsWithTitledItems()
	{
		foreach ((string name, Key _) in BarMenus.Menus)
		{
			ContextMenu menu = Build(name);
			foreach (ContextButton button in menu.All().OfType<ContextButton>())
				Assert.False(string.IsNullOrWhiteSpace(button.Text?.Text), $"{name}: '{button.Id}' has a title");
		}
	}

	// what a bar click does after the view last clicked has gone, such as the Home window being closed
	[Test]
	public async Task MenusOpenAfterTheLastClickedViewIsFreed()
	{
		Control gone = new();
		TestApp.Tree.Root.AddChild(gone);
		InputManager.Singleton.Keyboard.Capture(gone);
		gone.QueueFree();
		await TestApp.Frames(2);

		Assert.Null(InputManager.Singleton.Keyboard.Captor, "a freed captor isn't handed out");
		foreach ((string name, Key _) in BarMenus.Menus) Build(name);
		ContextMenu layouts = new();
		BarMenus.FillLayouts(layouts.Elements, project.Context);
		Assert.True(layouts.Elements.Count > 0, "the layouts menu fills");
	}

	// stands in for a handler that switches between bar menus itself, as the Windows one does
	sealed class BarRecorder : EditSharpGUI.Scripts.UI.ContextMenu.Platform.PlatformHandler
	{
		public System.Collections.Generic.List<EditSharpGUI.Scripts.UI.ContextMenu.Platform.BarMenu> Menus = [];
		public int Index, Shown;
		public override bool SwitchesBarMenus => true;
		public override void HandleMenu(ContextMenu menu, Window owner, Vector2I? at) { }
		public override void HandleBar(System.Collections.Generic.IReadOnlyList<EditSharpGUI.Scripts.UI.ContextMenu.Platform.BarMenu> menus, int index, Window owner)
		{
			Menus = [.. menus];
			Index = index;
			Shown++;
		}
	}

	[Test]
	public async Task EveryBarMenuGoesToTheHandlerSoTheLayoutsOneCanBeReachedToo()
	{
		EditSharpGUI.Scripts.UI.ContextMenu.Platform.PlatformHandler real = ContextMenus.Handler;
		BarRecorder recorder = new();
		ContextMenus.Handler = recorder;
		try
		{
			BarMenus bar = project.Window.GetChildren().OfType<BarMenus>().First();
			bar.Open("Edit");
			await TestApp.Frames(2);

			Assert.Equal(1, recorder.Shown);
			Assert.Count(5, recorder.Menus);
			Assert.Equal(1, recorder.Index, "Edit opens");
			Assert.NotNull(recorder.Menus[4].Menu.Elements.OfType<ContextRadioList>().FirstOrDefault(), "the layouts menu comes last");
			foreach (var menu in recorder.Menus)
			{
				Assert.True(menu.Button.HasArea(), "each button has a place to hover");
				Assert.Equal(menu.Button.End.Y, menu.At.Y, "each drops down from its button");
			}
			Assert.Count(5, recorder.Menus.Select(m => m.Button.Position.X).Distinct());

			bar.Open("View");
			await TestApp.Frames(2);
			Assert.Equal(1, recorder.Shown, "while one is open the handler does the switching");

			recorder.Menus[recorder.Index].Menu.EmitSignal(ContextMenu.SignalName.Closed);
			await TestApp.Frames(2);
			bar.Open(BarMenus.LayoutsMenu);
			await TestApp.Frames(2);
			Assert.Equal(2, recorder.Shown);
			Assert.Equal(4, recorder.Index, "the layout switcher opens its menu through the bar too");
		}
		finally { ContextMenus.Handler = real; }
	}

	[Test]
	public void ItemsShowTheirKeys()
	{
		Assert.Equal("Ctrl+S", Item(Build("File"), Shortcuts.Save).ShortcutHint?.Text);
		Assert.Equal("Space", Item(Build("Playback"), Shortcuts.PlaybackToggle).ShortcutHint?.Text);
	}

	[Test]
	public void UndoNamesWhatItUndoes()
	{
		Assert.False(Item(Build("Edit"), Shortcuts.Undo).Enabled, "nothing to undo yet");
		project.Timeline.AddChannel(video: false);
		ContextButton undo = Item(Build("Edit"), Shortcuts.Undo);
		Assert.True(undo.Enabled);
		Assert.Equal("Undo Add audio channel", undo.Text.Text);
		Assert.False(Item(Build("Edit"), Shortcuts.Redo).Enabled);
	}

	[Test]
	public void ClipItemsNeedASelection()
	{
		ContextMenu edit = Build("Edit");
		Assert.False(Item(edit, Shortcuts.Copy).Enabled);
		Assert.False(Item(edit, Shortcuts.Delete).Enabled);
		Assert.False(Item(edit, Shortcuts.Paste).Enabled, "nothing on the clipboard");

		project.Timeline.Select([project.Timeline.Clips[0]]);
		edit = Build("Edit");
		Assert.True(Item(edit, Shortcuts.Copy).Enabled);
		Assert.True(Item(edit, Shortcuts.Cut).Enabled);
		Assert.True(Item(edit, Shortcuts.Duplicate).Enabled);
		Assert.True(Item(edit, Shortcuts.Delete).Enabled);
	}

	[Test]
	public void ViewItemsTickOpenViews()
	{
		ContextMenu view = Build("View");
		Assert.True(Item(view, AppCommands.ViewCommand("timeline")).Checked);
		Assert.False(Item(view, AppCommands.ViewCommand("source")).Checked);
		Assert.Equal(ContextButton.CheckType.Check, Item(view, AppCommands.ViewCommand("media")).Type);
	}

	[Test]
	public void RunningAViewItemTogglesIt()
	{
		Commands.Run(AppCommands.ViewCommand("inspector"), project.Context);
		Assert.False(project.Layout.IsOpen("inspector"));
		Commands.Run(AppCommands.ViewCommand("inspector"), project.Context);
		Assert.True(project.Layout.IsOpen("inspector"));
	}

	[Test]
	public void TheLayoutsSubmenuListsThemWithTheActiveOneTicked()
	{
		ContextSubmenu layouts = Assert.NotNull(Build("View").Find<ContextSubmenu>("view.layouts"));
		ContextRadioList list = Assert.NotNull(layouts.Elements.OfType<ContextRadioList>().FirstOrDefault(), "the layouts are one radio group");
		Assert.Sequence(["Editing", "Assembly", "Audio"], list.Buttons.Select(b => b.Text.Text));
		Assert.Equal(0, list.SelectedButton);
		Assert.True(list.Buttons[0].Checked);
		Assert.False(list.Buttons[1].Checked);
		Assert.NotNull(layouts.Elements.OfType<ContextButton>().FirstOrDefault(b => b.Id == AppCommands.SaveLayout));
		Assert.False(layouts.Elements.OfType<ContextButton>().First(b => b.Id == AppCommands.DeleteLayout).Enabled, "presets can't be deleted");
	}

	// picked the way the menu handlers pick: through MenuModel, which owns the radio behaviour
	[Test]
	public void PickingALayoutFromTheMenuAppliesItAndUnticksTheRest()
	{
		ContextRadioList list = Build("View").Find<ContextSubmenu>("view.layouts").Elements.OfType<ContextRadioList>().First();
		int audio = list.Buttons.ToList().FindIndex(b => b.Text.Text == "Audio");
		EditSharpGUI.Scripts.UI.ContextMenu.Platform.MenuModel.Activate(new(list.Buttons[audio], list, audio));
		Assert.Equal("Audio", project.Layout.Active);
		Assert.Equal(1, list.Buttons.Count(b => b.Checked), "only one layout is ticked");
		Assert.True(list.Buttons[audio].Checked);
	}

	[Test]
	public void OpenRecentListsProjects()
	{
		ContextSubmenu recent = Assert.NotNull(Build("File").Find<ContextSubmenu>("file.recent"));
		Assert.True(recent.Elements.OfType<ContextButton>().Any(b => b.Text.Text == project.Name));
	}

	[Test]
	public void LoopIsATickedToggle()
	{
		Assert.False(Item(Build("Playback"), Shortcuts.Loop).Checked);
		Commands.Run(Shortcuts.Loop, project.Context);
		Assert.True(Item(Build("Playback"), Shortcuts.Loop).Checked);
		Assert.True(project.Playback.Loop);
	}

	[Test]
	public void ExtensionItemsJoinTheirMenu()
	{
		using Registration command = TestApp.App.Commands.Register(new Command { Id = "test.menuitem", Title = "Test Item", Run = (_, _) => null });
		using Registration atEnd = TestApp.App.Menus.Add("Edit", "test.menuitem");
		ContextMenu edit = Build("Edit");
		Assert.Equal("test.menuitem", edit.Elements.OfType<ContextButton>().Last().Id);
		Assert.True(edit.Elements[^2] is ContextDivider, "set apart from the app's own items");
	}

	[Test]
	public void ExtensionItemsCanFollowAnItem()
	{
		using Registration command = TestApp.App.Commands.Register(new Command { Id = "test.after", Title = "After Copy", Run = (_, _) => null });
		using Registration after = TestApp.App.Menus.Add("Edit", "test.after", after: Shortcuts.Copy);
		ContextMenu edit = Build("Edit");
		int copy = edit.Elements.ToList().FindIndex(e => e.Id == Shortcuts.Copy);
		Assert.Equal("test.after", edit.Elements[copy + 1].Id);
	}

	[Test]
	public async Task ExtensionViewsAreListedAndOpen()
	{
		using Registration view = TestApp.App.Views.Register(new ViewDefinition("test.view", "Test View", _ => new Label { Text = "hi" }, Beside: "inspector"));
		await TestApp.Frames(1);
		ContextButton item = Item(Build("View"), AppCommands.ViewCommand("test.view"));
		Assert.Equal("Test View", item.Text.Text);

		project.Layout.Open("test.view");
		Assert.True(project.Layout.IsOpen("test.view"));
		Assert.True(project.Editor.Docks.Views.Any(v => v.Id == "test.view"));
		view.Dispose();
		await TestApp.Frames(1);
		Assert.False(project.Editor.Docks.Views.Any(v => v.Id == "test.view"), "gone from the window when unregistered");
	}

	[Test]
	public void AViewIdCantBeTakenTwice()
	{
		using Registration view = TestApp.App.Views.Register(new ViewDefinition("test.twice", "Twice", _ => new Label()));
		Assert.Throws<System.ArgumentException>(() => TestApp.App.Views.Register(new ViewDefinition("test.twice", "Again", _ => new Label())));
		Assert.Throws<System.ArgumentException>(() => TestApp.App.Views.Register(new ViewDefinition("timeline", "Mine", _ => new Label())), "nor a built-in id");
	}
}

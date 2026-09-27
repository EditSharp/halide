using EditSharpGUI.Scripts.App.Commands;
using EditSharpGUI.Scripts.App.Layouts;
using EditSharpGUI.Scripts.Input;
using EditSharpGUI.Scripts.UI.ContextMenu;
using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace EditSharpGUI.Scripts.App.Chrome;

// the File, Edit, View and Playback menus in a window's bar: each filled from the commands as it opens,
// the pointer sliding to another menu while one is open, and Alt with a letter opening one
public partial class BarMenus : Node
{
	// the menus in bar order, with the letter that opens each alongside Alt
	public static readonly (string Name, Key Letter)[] Menus = [("File", Key.F), ("Edit", Key.E), ("View", Key.V), ("Playback", Key.P)];

	// the layout switcher's menu, which the pointer reaches the same way as the others
	public const string LayoutsMenu = "Layouts";

	UITopBar bar;
	readonly Dictionary<string, Button> buttons = [];
	string open;
	string next;
	bool altAlone;

	public static BarMenus Attach(Window window, UITopBar bar)
	{
		BarMenus menus = new() { Name = "BarMenus", bar = bar };
		foreach ((string name, Key _) in Menus) menus.buttons[name] = bar.AddMenu(name);
		if (bar.LayoutsAnchor is Button layouts) menus.buttons[LayoutsMenu] = layouts;
		bar.MenuPressed += (name, _) => menus.Open(name);
		bar.LayoutsPressed += () => menus.Open(LayoutsMenu);
		window.AddChild(menus);
		return menus;
	}

	/// <summary>A menu from res://Menus/Bar, filled for the window's context, the way it opens from the bar.</summary>
	public static ContextMenu Build(string name, CommandContext context)
	{
		ContextMenu menu = GD.Load<ContextMenu>($"res://Menus/Bar/{name}.tres").Clone();
		AddExtensionItems(name, menu.Elements);
		Fill(menu.Elements, context);
		return menu;
	}

	public void Open(string name)
	{
		if (!buttons.TryGetValue(name, out Button button) || !button.IsVisibleInTree()) return;
		CommandContext context = CommandContext.Of(GetWindow());

		// the handler moves between the menus itself, so all of them go to it now. a second Open() call
		// while one is already showing only happens from code, not a real click (the native popup captures
		// clicks on the other buttons itself, long before they'd reach a Button.Pressed signal), so it's
		// fine to just ignore it here
		if (ContextMenus.Handler.SwitchesBarMenus)
		{
			if (open is not null) return;

			List<(ContextMenu, Control)> all = [];
			int index = 0;
			foreach ((string each, Button eachButton) in buttons)
			{
				if (!eachButton.IsVisibleInTree()) continue;
				if (each == name) index = all.Count;
				ContextMenu built = MenuFor(each, context);
				built.Closed += () => Callable.From(Closed).CallDeferred();
				all.Add((built, eachButton));
			}

			open = name;
			ContextMenus.ShowBar(all, index);
			return;
		}

		// another is open: it closes first, and this one opens once it has
		if (open is not null)
		{
			if (open == name) return;
			next = name;
			ContextMenus.Handler.Dismiss();
			return;
		}

		ContextMenu menu = MenuFor(name, context);
		open = name;
		menu.Closed += () => Callable.From(Closed).CallDeferred();
		ContextMenus.ShowContextMenuBelow(menu, button);
	}

	void Closed()
	{
		open = null;
		if (next is string then)
		{
			next = null;
			Open(then);
		}
	}

	ContextMenu MenuFor(string name, CommandContext context)
	{
		if (name != LayoutsMenu) return Build(name, context);
		ContextMenu menu = new();
		FillLayouts(menu.Elements, context);
		return menu;
	}

	// while a menu is open, the pointer on another bar menu opens that one instead, as native menu bars do
	// (handlers that switch by themselves don't need this)
	public override void _Process(double delta)
	{
		if (open is null || next is not null || ContextMenus.Handler.SwitchesBarMenus) return;

		Vector2I pointer = OsPointer.Position;
		foreach ((string name, Button button) in buttons)
		{
			if (name == open || !button.IsVisibleInTree()) continue;
			if (ScreenRect(button).HasPoint(pointer))
			{
				Open(name);
				return;
			}
		}
	}

	static Rect2I ScreenRect(Control control)
	{
		Rect2 rect = control.GetGlobalTransform() * new Rect2(Vector2.Zero, control.Size);
		Vector2I from = ContextMenus.ToScreen(control.GetViewport(), rect.Position);
		Vector2I to = ContextMenus.ToScreen(control.GetViewport(), rect.End);
		return new Rect2I(from, to - from);
	}

	// Alt with a menu's letter opens it; Alt tapped alone opens the first
	public override void _Input(InputEvent e)
	{
		if (OS.GetName() == "macOS" || e is not InputEventKey key || key.Echo) return;

		if (key.Keycode == Key.Alt)
		{
			if (key.Pressed) altAlone = true;
			else if (altAlone)
			{
				altAlone = false;
				Open(Menus[0].Name);
				GetViewport().SetInputAsHandled();
			}
			return;
		}

		if (!key.Pressed) return;
		altAlone = false;

		if (!key.AltPressed || key.CtrlPressed || key.ShiftPressed || key.MetaPressed) return;
		foreach ((string name, Key letter) in Menus)
		{
			if (key.Keycode != letter) continue;
			Open(name);
			GetViewport().SetInputAsHandled();
			return;
		}
	}

	// the View menu's extension views after the built-in ones, then items extensions put in this menu
	static void AddExtensionItems(string name, IList<ContextElement> elements)
	{
		EditSharpGUI.Api.EditSharpApp app = EditSharpGUI.Api.EditSharpApp.Instance;

		if (name == "View")
		{
			int at = elements.ToList().FindIndex(e => e.Id == AppCommands.ViewCommand(AppCommands.Views[^1].Id)) + 1;
			foreach (EditSharpGUI.Api.ViewDefinition view in app.Views.FromExtensions)
				elements.Insert(at++, new ContextButton { Id = AppCommands.ViewCommand(view.Id) });
		}

		bool first = true;
		foreach (EditSharpGUI.Api.MenuService.Item item in app.Menus.In(name))
		{
			int after = item.After is null ? -1 : elements.ToList().FindIndex(e => e.Id == item.After);
			if (after >= 0) elements.Insert(after + 1, new ContextButton { Id = item.Command });
			else
			{
				if (first) elements.Add(new ContextDivider());
				elements.Add(new ContextButton { Id = item.Command });
			}
			first = false;
		}
	}

	// ---- filling a menu from the commands ----

	static void Fill(IList<ContextElement> elements, CommandContext context)
	{
		foreach (ContextElement element in elements.ToList())
		{
			switch (element)
			{
				case ContextSubmenu { Id: "file.recent" } recent:
					FillRecent(recent, context);
					break;

				case ContextSubmenu { Id: "view.layouts" } layouts:
					FillLayouts(layouts, context);
					break;

				case ContextSubmenu submenu:
					Fill(submenu.Elements, context);
					break;

				case ContextButton button when Commands.Commands.Get(button.Id) is Command command:
					Bind(button, command, context);
					break;
			}
		}
	}

	static void Bind(ContextButton button, Command command, CommandContext context, JsonObject args = null)
	{
		button.Text = new ContextText(command.TitleIn(context));
		button.Enabled = command.EnabledIn(context);
		button.ShortcutHint = ContextMenus.Hint(command.Id);
		if (command.IsChecked is not null)
		{
			button.Type = ContextButton.CheckType.Check;
			button.Checked = command.IsChecked(context);
		}
		button.Pressed += () => Commands.Commands.Run(command.Id, context, args);
	}

	static void FillRecent(ContextSubmenu recent, CommandContext context)
	{
		recent.Elements.Clear();
		foreach (RecentProject project in RecentProjects.All.Where(p => p.Exists).Take(10))
		{
			ContextButton item = new() { Id = $"recent.{project.Path}", Text = new ContextText(project.Name) };
			string path = project.Path;
			Node asker = context.Source ?? (Node)((SceneTree)Engine.GetMainLoop()).Root;
			item.Pressed += () => _ = ProjectManager.Singleton.OpenProjectAsync(path, asker);
			recent.Elements.Add(item);
		}

		if (recent.Elements.Count == 0) recent.Elements.Add(new ContextText("No recent projects"));
	}

	/// <summary>The layouts, the active one ticked, then managing them: the switcher's menu and View ▸ Layouts alike.</summary>
	public static void FillLayouts(ContextSubmenu layouts, CommandContext context) => FillLayouts(layouts.Elements, context);

	public static void FillLayouts(IList<ContextElement> elements, CommandContext context)
	{
		elements.Clear();
		string active = context.Editor?.Layouts.Active;
		int number = 0;

		// one radio group: picking a layout unticks the others
		ContextRadioList list = new();
		foreach (string name in LayoutStore.Names)
		{
			number++;
			if (name == active) list.SelectedButton = number - 1;
			list.Buttons.Add(new ContextButton
			{
				Id = $"layout.item.{name}",
				Text = new ContextText(name),
				Type = ContextButton.CheckType.Radio,
				Checked = name == active,
				Enabled = context.Editor is not null,
				ShortcutHint = number <= 9 ? ContextMenus.Hint(Shortcuts.Layout(number)) : null,
			});
		}
		list.Selected += button => Commands.Commands.Run(AppCommands.ApplyLayout, context, new JsonObject { ["name"] = button.Text.Text });
		elements.Add(list);

		elements.Add(new ContextDivider());
		foreach (string id in new[] { AppCommands.SaveLayout, AppCommands.RenameLayout, AppCommands.DeleteLayout, Shortcuts.ResetLayout })
		{
			ContextButton item = new() { Id = id };
			Bind(item, Commands.Commands.Get(id), context);
			elements.Add(item);
		}
	}
}

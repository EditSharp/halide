using Halide.Api;
using Halide.Scripts.App.Commands;
using Halide.Scripts.Input;
using Halide.Scripts.UI.ContextMenu;
using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Halide.Tests;

// the command registry and the promises around it: every menu item and shortcut has a command
[TestFixture]
public sealed class CommandRegistryTests
{
	[Test]
	public void EveryCommandHasAnIdAndTitle()
	{
		Assert.True(Commands.All.Count() > 60, "the app's commands are registered");
		foreach (Command c in Commands.All)
		{
			Assert.False(string.IsNullOrWhiteSpace(c.Id), "an id");
			Assert.False(string.IsNullOrWhiteSpace(c.Title), $"{c.Id} has a title");
			Assert.True(c.Id.Contains('.'), $"{c.Id} is namespaced");
		}
	}

	[Test]
	public void EveryShortcutActionIsACommand()
	{
		foreach ((string action, _, _) in Shortcuts.Catalog)
			Assert.NotNull(Commands.Get(action), $"'{action}' runs as a command");
	}

	[Test]
	public void NoTwoActionsShareADefaultKey()
	{
		Dictionary<KeyCombo, string> seen = [];
		foreach ((string action, _, _) in Shortcuts.Catalog)
		{
			foreach (KeyCombo combo in ShortcutMap.DefaultsFor(action))
			{
				if (seen.TryGetValue(combo, out string other)) Assert.Fail($"{combo} is both '{other}' and '{action}'");
				seen[combo] = action;
			}
		}
	}

	[Test]
	public void EveryBarMenuItemIsACommand()
	{
		string[] dynamic = ["file.recent", "view.layouts"];
		foreach ((string name, Key _) in Scripts.App.Chrome.BarMenus.Menus)
		{
			ContextMenu menu = GD.Load<ContextMenu>($"res://Menus/Bar/{name}.tres");
			foreach (ContextElement e in menu.All())
			{
				if (e is ContextSubmenu sub) Assert.Contains(sub.Id, dynamic, $"{name}: a submenu the bar fills");
				else if (e is ContextButton button) Assert.NotNull(Commands.Get(button.Id), $"{name}: '{button.Id}' is a command");
			}
		}
	}

	[Test]
	public void EveryBuiltInViewHasAToggleCommand()
	{
		foreach ((string id, _) in AppCommands.Views) Assert.NotNull(Commands.Get(AppCommands.ViewCommand(id)), id);
	}

	[Test]
	public void RegisteringReplacesAndUnregisterRemoves()
	{
		Command first = new() { Id = "test.one", Title = "One", Run = (_, _) => 1 };
		Command second = new() { Id = "test.one", Title = "One Again", Run = (_, _) => 2 };
		int changes = 0;
		void Count() => changes++;
		Commands.Changed += Count;
		try
		{
			Commands.Register(first);
			Commands.Register(second);
			Assert.Equal(second, Commands.Get("test.one"));
			Commands.Unregister("test.one");
			Assert.Null(Commands.Get("test.one"));
			Commands.Unregister("test.one");
		}
		finally { Commands.Changed -= Count; }
		Assert.Equal(3, changes, "each real change is announced once");
	}

	[Test]
	public void RunReportsMissingDisabledAndFailingCommands()
	{
		Assert.False(Commands.Run("test.nothing"));

		Commands.Register(new Command { Id = "test.off", Title = "Off", CanRun = _ => false, Run = (_, _) => 1 });
		Commands.Register(new Command { Id = "test.throws", Title = "Throws", Run = (_, _) => throw new System.InvalidOperationException("no") });
		Commands.Register(new Command { Id = "test.echo", Title = "Echo", Run = (_, args) => args?["value"]?.DeepClone() });
		try
		{
			Assert.False(Commands.Run("test.off"));
			Assert.False(Commands.Run("test.throws"));
			Assert.True(Commands.TryRun("test.echo", out JsonNode result, args: new JsonObject { ["value"] = 7 }));
			Assert.Equal(7, result.GetValue<int>());
		}
		finally
		{
			Commands.Unregister("test.off");
			Commands.Unregister("test.throws");
			Commands.Unregister("test.echo");
		}
	}

	[Test]
	public void TheServiceThrowsCommandExceptions()
	{
		CommandsService service = EditSharpApp.Instance.Commands;
		Assert.Throws<CommandException>(() => service.Run("test.nothing"));

		using Registration off = service.Register(new Command { Id = "test.off", Title = "Off", CanRun = _ => false, Run = (_, _) => 1 });
		using Registration boom = service.Register(new Command { Id = "test.boom", Title = "Boom", Run = (_, _) => throw new System.InvalidOperationException("kaboom") });
		Assert.False(service.CanRun("test.off"));
		Assert.Throws<CommandException>(() => service.Run("test.off"));
		CommandException e = Assert.Throws<CommandException>(() => service.Run("test.boom"));
		Assert.True(e.Message.Contains("kaboom"), "the cause is in the message");
	}

	[Test]
	public void DisposingARegistrationRemovesOnlyItsOwnCommand()
	{
		CommandsService service = EditSharpApp.Instance.Commands;
		Registration old = service.Register(new Command { Id = "test.shared", Title = "Old", Run = (_, _) => null });
		Command replacement = new() { Id = "test.shared", Title = "New", Run = (_, _) => null };
		using Registration current = service.Register(replacement);
		old.Dispose();
		Assert.Equal(replacement, service.Get("test.shared"), "a replaced command's old registration doesn't remove the new one");
		old.Dispose();
	}

	[Test]
	public void TheCatalogueListsEveryCommand()
	{
		string page = Api.Remote.CommandCatalog.Markdown();
		foreach (Command c in Commands.All) Assert.True(page.Contains($"`{c.Id}`"), $"{c.Id} is in the catalogue");
	}
}

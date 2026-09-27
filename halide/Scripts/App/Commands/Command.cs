using System;
using System.Text.Json.Nodes;

namespace Halide.Scripts.App.Commands;

/// <summary>A named action the menus, shortcuts, extensions and remote callers all run the same way.</summary>
public sealed class Command
{
	/// <summary>The command's name, like <c>project.save</c>; also its shortcut action.</summary>
	public required string Id { get; init; }

	/// <summary>What menus call it.</summary>
	public required string Title { get; init; }

	/// <summary>What it does and the arguments it takes, for the docs and <c>command.list</c>.</summary>
	public string Help { get; init; }

	/// <summary>A title that changes with the context, like "Undo Move"; null uses <see cref="Title"/>.</summary>
	public Func<CommandContext, string> DynamicTitle { get; init; }

	/// <summary>Whether it can run in the context; null means always.</summary>
	public Func<CommandContext, bool> CanRun { get; init; }

	/// <summary>Whether a menu shows it ticked; null means it isn't a toggle.</summary>
	public Func<CommandContext, bool> IsChecked { get; init; }

	/// <summary>Runs it with optional arguments and returns its result, or null.</summary>
	public required Func<CommandContext, JsonObject, JsonNode> Run { get; init; }

	public string TitleIn(CommandContext context) => DynamicTitle?.Invoke(context) ?? Title;

	public bool EnabledIn(CommandContext context) => CanRun?.Invoke(context) ?? true;
}

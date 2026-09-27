using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Halide.Scripts.App.Commands;

/// <summary>Every command the app knows, by id.</summary>
public static class Commands
{
	static readonly Dictionary<string, Command> registry = [];

	/// <summary>A command was added or removed.</summary>
	public static event Action Changed;

	public static IEnumerable<Command> All => registry.Values;

	/// <summary>Adds a command, replacing one with the same id.</summary>
	public static void Register(Command command)
	{
		registry[command.Id] = command;
		Changed?.Invoke();
	}

	public static void Unregister(string id)
	{
		if (registry.Remove(id)) Changed?.Invoke();
	}

	public static Command Get(string id) => registry.TryGetValue(id, out Command command) ? command : null;

	/// <summary>Runs a command in a context, or the current one; false when it doesn't exist or can't run there.</summary>
	public static bool Run(string id, CommandContext context = null, JsonObject args = null) => TryRun(id, out _, context, args);

	/// <summary>Runs a command and hands back its result.</summary>
	public static bool TryRun(string id, out JsonNode result, CommandContext context = null, JsonObject args = null)
	{
		result = null;
		if (Get(id) is not Command command) return false;

		context ??= CommandContext.Current;
		if (!command.EnabledIn(context)) return false;

		try
		{
			result = command.Run(context, args);
			return true;
		}
		catch (Exception e)
		{
			GD.PushError($"Command '{id}' failed: {e.Message}");
			return false;
		}
	}
}

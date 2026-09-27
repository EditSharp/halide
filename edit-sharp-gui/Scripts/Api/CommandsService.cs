using EditSharpGUI.Scripts.App.Commands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace EditSharpGUI.Api;

/// <summary>The named commands: what menus, shortcuts, extensions and remote callers run.</summary>
public sealed class CommandsService
{
	/// <summary>Every command, sorted by id.</summary>
	public IReadOnlyList<Command> All => [.. Commands.All.OrderBy(c => c.Id)];

	public Command Get(string id) => Commands.Get(id);

	/// <summary>Adds a command until the returned registration is disposed.</summary>
	public Registration Register(Command command)
	{
		Commands.Register(command);
		return new Registration(() => { if (Commands.Get(command.Id) == command) Commands.Unregister(command.Id); });
	}

	/// <summary>Whether a command exists and can run for a project, or the focused one.</summary>
	public bool CanRun(string id, ProjectHandle project = null) => Commands.Get(id) is Command command && command.EnabledIn(Context(project));

	/// <summary>Runs a command for a project (the focused one when null) and returns its result.</summary>
	/// <exception cref="CommandException">The command doesn't exist, can't run there, or failed.</exception>
	public JsonNode Run(string id, ProjectHandle project = null, JsonObject args = null)
	{
		if (Commands.Get(id) is not Command command) throw new CommandException($"There's no command '{id}'.");

		CommandContext context = Context(project);
		if (!command.EnabledIn(context)) throw new CommandException($"'{id}' can't run right now.");

		try
		{
			return command.Run(context, args);
		}
		catch (Exception e) when (e is not CommandException)
		{
			throw new CommandException($"'{id}' failed: {e.Message}", e);
		}
	}

	static CommandContext Context(ProjectHandle project) => project?.Context ?? CommandContext.Current;
}

using EditSharpGUI.Scripts.App.Commands;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EditSharpGUI.Api;

/// <summary>The dockable views: the built-in ones and those extensions add, each shown in every project window's View menu.</summary>
public sealed class ViewRegistry
{
	readonly List<ViewDefinition> added = [];

	/// <summary>A view kind was added; open project windows get one straight away.</summary>
	public event Action<ViewDefinition> Added;

	/// <summary>A view kind was removed; project windows close and drop theirs.</summary>
	public event Action<ViewDefinition> Removed;

	/// <summary>The built-in views, by id and title.</summary>
	public IReadOnlyList<(string Id, string Title)> BuiltIn => AppCommands.Views;

	/// <summary>The views extensions added.</summary>
	public IReadOnlyList<ViewDefinition> FromExtensions => added;

	/// <summary>Every view kind's id and title, built-in first.</summary>
	public IReadOnlyList<(string Id, string Title)> All => [.. BuiltIn, .. added.Select(v => (v.Id, v.Title))];

	/// <summary>Adds a view kind until the returned registration is disposed, with a View menu command to open and close it.</summary>
	/// <exception cref="ArgumentException">The id is taken.</exception>
	public Registration Register(ViewDefinition view)
	{
		if (All.Any(v => v.Id == view.Id)) throw new ArgumentException($"There's already a view '{view.Id}'.", nameof(view));

		added.Add(view);
		Registration command = RegisterToggle(view);
		Added?.Invoke(view);

		return new Registration(() =>
		{
			if (!added.Remove(view)) return;
			command.Dispose();
			Removed?.Invoke(view);
		});
	}

	static Registration RegisterToggle(ViewDefinition view)
	{
		string id = AppCommands.ViewCommand(view.Id);
		Commands.Register(new Command
		{
			Id = id,
			Title = view.Title,
			CanRun = ctx => ctx.Docks?.Views.Any(v => v.Id == view.Id) == true,
			IsChecked = ctx => ctx.Docks?.IsOpen(view.Id) == true,
			Run = (ctx, _) =>
			{
				if (ctx.Docks.IsOpen(view.Id)) ctx.Docks.Close(view.Id);
				else ctx.Docks.Open(view.Id);
				return null;
			},
		});
		return new Registration(() => Commands.Unregister(id));
	}
}

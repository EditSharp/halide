using EditSharp.Components.Media;
using EditSharpGUI.Scripts.App.Commands;
using EditSharpGUI.Scripts.Input;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EditSharpGUI.Api.Extensions;

/// <summary>What an extension works through: the app, and ways to add to it that are all taken back when the extension stops.</summary>
/// <remarks>
/// Commands, views, importers and event handlers added here are guarded: an exception from them is logged,
/// the extension is marked failed and disabled, and the app carries on.
/// </remarks>
public sealed class ExtensionContext
{
	readonly List<IDisposable> added = [];
	readonly Action<Exception> fail;

	internal ExtensionContext(ExtensionManifest manifest, string folder, Action<Exception> fail)
	{
		Manifest = manifest;
		Folder = folder;
		this.fail = fail;
	}

	public ExtensionManifest Manifest { get; }

	/// <summary>The extension's folder, for its own files.</summary>
	public string Folder { get; }

	/// <summary>The app: projects, commands, views, menus, settings and importers.</summary>
	public EditSharpApp App => EditSharpApp.Instance;

	/// <summary>Adds a command; its id should start with the extension's id.</summary>
	public Registration AddCommand(Command command) => Track(App.Commands.Register(new Command
	{
		Id = command.Id,
		Title = command.Title,
		DynamicTitle = command.DynamicTitle is null ? null : ctx => Guard(() => command.DynamicTitle(ctx), command.Title),
		CanRun = command.CanRun is null ? null : ctx => Guard(() => command.CanRun(ctx), false),
		IsChecked = command.IsChecked is null ? null : ctx => Guard(() => command.IsChecked(ctx), false),
		Run = (ctx, args) =>
		{
			try { return command.Run(ctx, args); }
			catch (Exception e) when (e is not CommandException)
			{
				fail(e);
				throw;
			}
		},
	}));

	/// <summary>Gives a command a default key, unless the user has bound it themselves.</summary>
	public Registration AddShortcut(string command, KeyCombo combo)
	{
		ShortcutMap map = InputManager.Singleton.Keyboard.Shortcuts;
		bool mine = map.Get(command).Count == 0;
		if (mine) map.Bind(command, combo);
		Shortcuts.Extra.Add((command, Manifest.Name, App.Commands.Get(command)?.Title ?? command));

		return Track(new Registration(() =>
		{
			Shortcuts.Extra.RemoveAll(e => e.Action == command);
			if (mine && map.Get(command).SequenceEqual([combo])) map.Unbind(command);
		}));
	}

	/// <summary>Adds a dockable view to every project window, with a View menu item.</summary>
	public Registration AddView(ViewDefinition view) => Track(App.Views.Register(view with
	{
		Create = project => Guard(() => view.Create(project), (Control)null),
	}));

	/// <summary>Puts a command in a bar menu: File, Edit, View or Playback.</summary>
	public Registration AddMenuItem(string menu, string command, string after = null) => Track(App.Menus.Add(menu, command, after));

	/// <summary>Makes media from files ending in <paramref name="extensions"/>.</summary>
	public Registration AddImporter(IEnumerable<string> extensions, Func<string, IMedia> create) =>
		Track(App.Importers.Register(extensions, path => Guard(() => create(path), (IMedia)null)));

	/// <summary>Adds a page to App Settings showing an object's [Editable] properties, like the built-in pages.</summary>
	public Registration AddSettingsPage(string title, object page) => Track(App.SettingsPages.Register(title, page));

	/// <summary>Runs <paramref name="handler"/> whenever a project opens, including those open already.</summary>
	public Registration OnProjectOpened(Action<ProjectHandle> handler)
	{
		Action<ProjectHandle> guarded = p => Guard(() => handler(p));
		foreach (ProjectHandle p in App.Projects.All) guarded(p);
		App.Projects.Opened += guarded;
		return Track(new Registration(() => App.Projects.Opened -= guarded));
	}

	public Registration OnProjectClosed(Action<ProjectHandle> handler)
	{
		Action<ProjectHandle> guarded = p => Guard(() => handler(p));
		App.Projects.Closed += guarded;
		return Track(new Registration(() => App.Projects.Closed -= guarded));
	}

	/// <summary>Anything else to undo when the extension stops.</summary>
	public T Track<T>(T disposable) where T : IDisposable
	{
		added.Add(disposable);
		return disposable;
	}

	/// <summary>Writes to the app's log, marked with the extension's id.</summary>
	public void Log(string message) => GD.Print($"[{Manifest.Id}] {message}");

	internal void RemoveAll()
	{
		for (int i = added.Count - 1; i >= 0; i--)
		{
			try { added[i].Dispose(); }
			catch (Exception e) { GD.PushWarning($"[{Manifest.Id}] undoing an addition failed: {e.Message}"); }
		}
		added.Clear();
	}

	T Guard<T>(Func<T> work, T fallback)
	{
		try { return work(); }
		catch (Exception e)
		{
			fail(e);
			return fallback;
		}
	}

	void Guard(Action work)
	{
		try { work(); }
		catch (Exception e) { fail(e); }
	}
}

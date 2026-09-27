using Halide.Scripts.App.Layouts;
using Halide.Scripts.Input;
using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

namespace Halide.Scripts.App.Commands;

/// <summary>The commands the app itself provides: everything in the bar menus, and every shortcut.</summary>
public static class AppCommands
{
	/// <summary>The dockable views the View menu lists, by id and title.</summary>
	public static readonly (string Id, string Title)[] Views =
	[
		("media", "Media"),
		("source", "Source"),
		("program", "Program"),
		("inspector", "Inspector"),
		("timeline", "Timeline"),
		("graph", "Graph Editor"),
	];

	public static string ViewCommand(string view) => $"view.{view}";

	public static void RegisterAll()
	{
		// ---- the app ----
		Register(Shortcuts.NewProject, "New Project…", Always, ctx => _ = NewProjectDialog.ShowAsync(Asker(ctx)));
		Register(Shortcuts.OpenProject, "Open Project…", Always, ctx => ProjectManager.Singleton.BrowseToOpen(Asker(ctx)));
		Register(Shortcuts.ShowHome, "Home", Always, _ => ProjectManager.Singleton.ShowHome());
		Register(Shortcuts.ShowSettings, "App Settings…", Always, _ => ProjectManager.Singleton.ShowSettings());
		Register(Shortcuts.Quit, "Quit", Always, _ => ProjectManager.Singleton.QuitAll());

		// ---- a project: through its editor's shortcut handlers, so a key and a menu do the same ----
		Editing(Shortcuts.Save, "Save");
		Editing(Shortcuts.SaveAs, "Save As…");
		Editing(Shortcuts.MediaImport, "Import Media…");
		Editing(Shortcuts.CloseProject, "Close Project");

		Commands.Register(new Command
		{
			Id = Shortcuts.Undo,
			Title = "Undo",
			DynamicTitle = ctx => ctx.Editor?.UndoName is string name ? $"Undo {name}" : "Undo",
			CanRun = ctx => ctx.Editor?.CanRun(Shortcuts.Undo) == true,
			Run = (ctx, _) => Invoke(Shortcuts.Undo, ctx),
		});
		Commands.Register(new Command
		{
			Id = Shortcuts.Redo,
			Title = "Redo",
			DynamicTitle = ctx => ctx.Editor?.RedoName is string name ? $"Redo {name}" : "Redo",
			CanRun = ctx => ctx.Editor?.CanRun(Shortcuts.Redo) == true,
			Run = (ctx, _) => Invoke(Shortcuts.Redo, ctx),
		});

		Editing(Shortcuts.Cut, "Cut");
		Editing(Shortcuts.Copy, "Copy");
		Editing(Shortcuts.Paste, "Paste");
		Editing(Shortcuts.Duplicate, "Duplicate");
		Editing(Shortcuts.Delete, "Delete");
		Editing(Shortcuts.RippleDelete, "Ripple Delete");
		Editing(Shortcuts.RippleDeleteAll, "Ripple Delete on Every Channel");
		Editing(Shortcuts.Split, "Split at Playhead");
		Editing(Shortcuts.SplitAll, "Split Every Channel at Playhead");
		Editing(Shortcuts.SelectAll, "Select All");
		Editing(Shortcuts.Deselect, "Deselect All");
		Editing(Shortcuts.MediaRename, "Rename");

		// ---- view ----
		foreach ((string id, string title) in Views)
		{
			string view = id;
			Commands.Register(new Command
			{
				Id = ViewCommand(view),
				Title = title,
				CanRun = ctx => ctx.Docks?.Views.Any(v => v.Id == view) == true,
				IsChecked = ctx => ctx.Docks?.IsOpen(view) == true,
				Run = (ctx, _) =>
				{
					if (ctx.Docks.IsOpen(view)) ctx.Docks.Close(view);
					else ctx.Docks.Open(view);
					return null;
				},
			});
		}

		Editing(Shortcuts.ResetLayout, "Reset to Saved Layout");
		Commands.Register(new Command
		{
			Id = ApplyLayout,
			Title = "Apply Layout",
			CanRun = ctx => ctx.Editor is not null,
			IsChecked = ctx => ctx.Editor?.Layouts.Active is not null,
			Run = (ctx, args) => ctx.Editor.Layouts.Apply((string)args?["name"]),
		});
		Commands.Register(new Command
		{
			Id = SaveLayout,
			Title = "Save as New Layout…",
			CanRun = ctx => ctx.Editor is not null,
			Run = (ctx, args) => Async(async () =>
			{
				string name = (string)args?["name"] ?? await LayoutDialogs.AskName(Asker(ctx), "Save Layout", "Save", LayoutDialogs.FreeName("My Layout"));
				if (name is not null) ctx.Editor.Layouts.SaveAsNew(name);
			}),
		});
		Commands.Register(new Command
		{
			Id = RenameLayout,
			Title = "Rename Layout…",
			CanRun = ctx => ctx.Editor?.Layouts.CanEdit == true,
			Run = (ctx, args) => Async(async () =>
			{
				string name = (string)args?["name"] ?? await LayoutDialogs.AskName(Asker(ctx), "Rename Layout", "Rename", ctx.Editor.Layouts.Active);
				if (name is not null) ctx.Editor.Layouts.RenameActive(name);
			}),
		});
		Commands.Register(new Command
		{
			Id = DeleteLayout,
			Title = "Delete Layout",
			CanRun = ctx => ctx.Editor?.Layouts.CanEdit == true,
			Run = (ctx, args) => Async(async () =>
			{
				if (args?["confirmed"]?.GetValue<bool>() == true || await LayoutDialogs.ConfirmDelete(Asker(ctx), ctx.Editor.Layouts.Active))
					ctx.Editor.Layouts.DeleteActive();
			}),
		});
		for (int n = 1; n <= 9; n++) Editing(Shortcuts.Layout(n), $"Layout {n}");
		Editing(Shortcuts.FloatFocused, "Float Focused View");
		Commands.Register(new Command
		{
			Id = Shortcuts.Fullscreen,
			Title = "Toggle Fullscreen",
			CanRun = ctx => ctx.Window is not null,
			IsChecked = ctx => ctx.Source?.Mode == Window.ModeEnum.Fullscreen,
			Run = (ctx, _) => Invoke(Shortcuts.Fullscreen, ctx),
		});
		Editing(Shortcuts.ZoomIn, "Zoom In");
		Editing(Shortcuts.ZoomOut, "Zoom Out");
		Editing(Shortcuts.ZoomFit, "Zoom to Fit");

		// ---- playback ----
		Editing(Shortcuts.PlaybackToggle, "Play / Pause");
		Editing(Shortcuts.PlaybackStop, "Stop");
		Editing(Shortcuts.PlaybackForward, "Shuttle Forward");
		Editing(Shortcuts.PlaybackReverse, "Shuttle Reverse");
		Editing(Shortcuts.StepForward, "Step Forward");
		Editing(Shortcuts.StepBack, "Step Back");
		Editing(Shortcuts.GoToStart, "Go to Start");
		Editing(Shortcuts.GoToEnd, "Go to End");
		Commands.Register(new Command
		{
			Id = Shortcuts.Loop,
			Title = "Loop Playback",
			CanRun = ctx => ctx.Editor is not null,
			IsChecked = ctx => ctx.Editor?.Looping == true,
			Run = (ctx, _) => Invoke(Shortcuts.Loop, ctx),
		});
	}

	public const string ApplyLayout = "layout.apply";
	public const string SaveLayout = "layout.saveNew";
	public const string RenameLayout = "layout.rename";
	public const string DeleteLayout = "layout.delete";

	static bool Always(CommandContext _) => true;

	// a command that waits on a dialog finishes later; it returns at once
	static JsonNode Async(Func<System.Threading.Tasks.Task> work)
	{
		_ = work();
		return null;
	}

	static void Register(string id, string title, Func<CommandContext, bool> canRun, Action<CommandContext> run) => Commands.Register(new Command
	{
		Id = id,
		Title = title,
		CanRun = canRun,
		Run = (ctx, _) => { run(ctx); return null; },
	});

	// a project command answered by whichever handler takes its shortcut action, greyed out by the editor's rules
	static void Editing(string id, string title) => Commands.Register(new Command
	{
		Id = id,
		Title = title,
		Help = "Does what its menu item and key do, in the view last clicked (the timeline when none was).",
		CanRun = ctx => ctx.Editor?.CanRun(id) == true,
		Run = (ctx, _) => Invoke(id, ctx),
	});

	static JsonNode Invoke(string action, CommandContext ctx)
	{
		InputManager.Singleton.Keyboard.Invoke(action, ctx.Origin);
		return null;
	}

	// the node a dialog opened by a command belongs to
	static Node Asker(CommandContext ctx) => (Node)ctx.Source ?? ((SceneTree)Engine.GetMainLoop()).Root;
}

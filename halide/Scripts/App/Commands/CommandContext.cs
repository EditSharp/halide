using Halide.Scripts.UI.Docking;
using Godot;
using System.Linq;

namespace Halide.Scripts.App.Commands;

/// <summary>Where a command runs: a project window and its editor, or the app with none open.</summary>
public sealed class CommandContext
{
	/// <summary>The project window, or null outside one.</summary>
	public ProjectWindow Window { get; init; }

	/// <summary>The window the command was asked from: the project's own, one of its floats, or another.</summary>
	public Window Source { get; init; }

	public ProjectSession Session => Window?.Session;

	public Editor Editor => Window?.FindChildren("*", "", true, false).OfType<Editor>().FirstOrDefault();

	public DockManager Docks => Window?.FindChildren("*", "", true, false).OfType<DockManager>().FirstOrDefault();

	/// <summary>The node shortcut handlers are asked from: the source window's focus and captor first, then the editor.</summary>
	public Node Origin => Source is not null && Source != Window ? Source : Editor;

	/// <summary>The context of a window: its project, whether it's a project window or one of its floats.</summary>
	public static CommandContext Of(Window window)
	{
		Window top = window;
		while (top is not null && top is not ProjectWindow && top.GetParent()?.GetWindow() is Window parent && parent != top) top = parent;
		return new CommandContext { Window = top as ProjectWindow, Source = window };
	}

	/// <summary>The context of whichever window has focus, or the most recent project window.</summary>
	public static CommandContext Current
	{
		get
		{
			ProjectWindow focused = ProjectManager.Singleton?.OpenProjects.FirstOrDefault(w => w.HasFocus() || w.GetChildren().OfType<Window>().Any(f => f.HasFocus()));
			ProjectWindow window = focused ?? ProjectManager.Singleton?.OpenProjects.LastOrDefault();
			Window source = window?.GetChildren().OfType<Window>().FirstOrDefault(f => f.HasFocus()) ?? window;
			return new CommandContext { Window = window, Source = source };
		}
	}
}

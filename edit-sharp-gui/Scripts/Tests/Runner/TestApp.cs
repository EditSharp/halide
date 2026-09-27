using EditSharpGUI.Api;
using EditSharpGUI.Scripts.UI.Dialogs;
using EditSharpGUI.Scripts.UI.Docking;
using Godot;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace EditSharpGUI.Tests;

/// <summary>What tests share: the app, projects to test on, waiting, and putting everything back between tests.</summary>
public static class TestApp
{
	public static EditSharpApp App => EditSharpApp.Instance;

	public static SceneTree Tree => (SceneTree)Engine.GetMainLoop();

	/// <summary>The current fixture's own folder: user data, projects and anything else it writes.</summary>
	public static string Sandbox { get; internal set; }

	/// <summary>Whether the run has no windows (Godot's --headless).</summary>
	public static bool Headless => DisplayServer.GetName() == "headless";

	/// <summary>A video with sound shipped for tests.</summary>
	public static string ExampleVideo => Path.Combine(AppContext.BaseDirectory, "Media", "Example", "Video", "video3.mp4");

	static int made;

	/// <summary>A new, empty project in the sandbox, open in a window.</summary>
	public static async Task<ProjectHandle> NewProjectAsync(string name = null)
	{
		ProjectHandle project = await App.Projects.CreateAsync(Sandbox, name ?? $"Test {++made}");
		return Assert.NotNull(project, "the project opened");
	}

	/// <summary>The shared test project (clips, channels, keyframes, the example video), saved in the sandbox and opened.</summary>
	public static async Task<ProjectHandle> BlueprintProjectAsync(string name = null)
	{
		string folder = Path.Combine(Sandbox, name ?? $"Blueprint {++made}");
		Directory.CreateDirectory(folder);
		string path = Path.Combine(folder, Path.GetFileName(folder) + ProjectFile.Extension);
		ProjectFile.Save(Project.FromBlueprint(global::Tests.TestBlueprint), path);
		return Assert.NotNull(await App.Projects.OpenAsync(path), "the blueprint project opened");
	}

	public static async Task Frames(int count = 1)
	{
		for (int i = 0; i < count; i++) await Tree.ToSignal(Tree, SceneTree.SignalName.ProcessFrame);
	}

	public static async Task Seconds(double seconds) => await Tree.ToSignal(Tree.CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

	/// <summary>Waits a frame at a time until <paramref name="condition"/> holds; fails after <paramref name="timeout"/> seconds.</summary>
	public static async Task WaitUntil(Func<bool> condition, string what, double timeout = 10)
	{
		ulong until = Godot.Time.GetTicksMsec() + (ulong)(timeout * 1000);
		while (!condition())
		{
			if (Godot.Time.GetTicksMsec() > until) Assert.Fail($"timed out waiting: {what}");
			await Frames();
		}
	}

	/// <summary>Everything a test may have left: projects and windows closed without asking, test hooks back to normal.</summary>
	public static async Task ResetAsync()
	{
		Dialogs.Answering = TestAnswer;

		foreach (ProjectWindow window in ProjectManager.Singleton.OpenProjects.ToArray())
		{
			if (GodotObject.IsInstanceValid(window)) await window.CloseAsync();
		}

		// Home-tile captures are slow without a GPU; left running they pile up across tests
		await ProjectThumbnails.StopAllAsync();

		foreach (Window window in Tree.Root.GetChildren().OfType<Window>().Where(w => w is HomeWindow or SettingsWindow).ToArray())
			window.QueueFree();

		DockDrag.Pointer = () => Scripts.Input.OsPointer.Position;
		DockDrag.Held = () => Scripts.Input.OsPointer.LeftHeld;
		Scripts.UI.DragDrop.DragDrop.Cancel();
		Scripts.UI.DragDrop.DragDrop.Pointer = () => Scripts.Input.OsPointer.Position;
		Scripts.UI.DragDrop.DragDrop.Held = () => Scripts.Input.OsPointer.LeftHeld;

		// layouts remember rearrangements; each test starts from the shipped ones
		await Frames(2);
		File.Delete(ProjectSettings.GlobalizePath(Scripts.App.Layouts.LayoutStore.FilePath));
		Scripts.App.Layouts.LayoutStore.Reload();
	}

	/// <summary>How tests answer dialogs unless they say otherwise: the destructive button (discard), else cancel.</summary>
	public static string TestAnswer(Dialog dialog) => dialog.Buttons.FirstOrDefault(b => b.Role == DialogButtonRole.Destructive)?.Id ?? dialog.Cancel?.Id;
}

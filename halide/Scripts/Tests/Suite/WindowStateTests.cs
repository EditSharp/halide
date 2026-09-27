using Halide.Api;
using Halide.Scripts.App.Chrome;
using Halide.Scripts.Input;
using Godot;
using System.Threading.Tasks;

namespace Halide.Tests;

// a project window reopening where it was left: its windowed size even when closed fullscreen or maximized, without drifting, and always on a screen
[TestFixture]
[Windowed]
public sealed class WindowStateTests
{
	static readonly Rect2I Windowed = new(200, 200, 1000, 700);

	static async Task<ProjectHandle> Placed()
	{
		ProjectHandle project = await TestApp.NewProjectAsync();
		await TestApp.Seconds(0.5);
		WindowChrome.Place(project.Window, Windowed);
		await TestApp.Seconds(0.8);
		return project;
	}

	static async Task<ProjectHandle> Reopen(ProjectHandle project)
	{
		string path = project.FilePath;
		project.Save();
		await project.CloseAsync();
		ProjectHandle again = await TestApp.App.Projects.OpenAsync(path);
		await TestApp.Seconds(1);
		return again;
	}

	// the OS's answer: Godot's Position can lag behind where the window is
	static Rect2I Rect(Window w) => WindowChrome.ContentRect(w);

	[Test(Timeout = 60)]
	public async Task ClosedFullscreenItReopensWindowedWhereItWasBefore()
	{
		ProjectHandle project = await Placed();
		Halide.Scripts.App.Commands.Commands.Run(Shortcuts.Fullscreen, project.Context);
		await TestApp.Seconds(1);
		Assert.Equal(Window.ModeEnum.Fullscreen, project.Window.Mode);

		ProjectWindow window = (await Reopen(project)).Window;
		Assert.Equal(Window.ModeEnum.Windowed, window.Mode);
		Assert.Equal(Windowed, Rect(window));
	}

	[Test(Timeout = 60)]
	public async Task ClosedMaximizedItReopensMaximizedAndRestoresWhereItWas()
	{
		ProjectHandle project = await Placed();
		project.Window.Mode = Window.ModeEnum.Maximized;
		await TestApp.Seconds(1);

		ProjectWindow window = (await Reopen(project)).Window;
		Assert.Equal(Window.ModeEnum.Maximized, window.Mode);
		Assert.Equal(Windowed, window.RestoredRect, "what it remembered");
		window.Mode = Window.ModeEnum.Windowed;
		await TestApp.Seconds(1);
		Assert.Equal(Windowed, Rect(window));
	}

	[Test(Timeout = 90)]
	public async Task ReopeningDoesntMoveOrGrowTheWindow()
	{
		ProjectHandle project = await Placed();
		for (int i = 0; i < 3; i++) project = await Reopen(project);
		Assert.Equal(Windowed, Rect(project.Window));
	}

	[Test(Timeout = 30)]
	public async Task AnOversizedSaveOpensOnAScreen()
	{
		ProjectWindow window = (await TestApp.NewProjectAsync()).Window;
		await TestApp.Seconds(0.5);
		window.Restore(new Rect2I(-5000, -5000, 20000, 20000), maximized: false);
		await TestApp.Seconds(0.8);

		bool fits = false;
		for (int i = 0; i < DisplayServer.GetScreenCount(); i++)
			fits |= DisplayServer.ScreenGetUsableRect(i).Encloses(Rect(window));
		Assert.True(fits, $"the window {Rect(window)} sits inside a screen's usable area");
	}
}

using EditSharpGUI.Api;
using Godot;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace EditSharpGUI.Tests;

// the Windows caption: what the OS is told is caption, maximize and client, and the bar keeping its height
[TestFixture]
[Windowed]
[OnlyOn("Windows")]
public sealed class WindowsChromeTests
{
	const uint WM_NCHITTEST = 0x84;
	const int HTCLIENT = 1, HTCAPTION = 2, HTMAXBUTTON = 9;

	[DllImport("user32.dll")] static extern nint SendMessageW(nint hwnd, uint msg, nint wParam, nint lParam);

	static nint Handle(Window w) => (nint)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, w.GetWindowId());

	static int HitAt(Window window, Vector2 global)
	{
		Vector2I screen = window.Position + (Vector2I)(window.GetScreenTransform() * global).Round();
		nint lParam = (screen.Y << 16) | (screen.X & 0xFFFF);
		return (int)SendMessageW(Handle(window), WM_NCHITTEST, 0, lParam);
	}

	[Test]
	public async Task TheOsKnowsTheCaptionTheButtonsAndTheContent()
	{
		ProjectHandle project = await TestApp.NewProjectAsync();
		await TestApp.Seconds(0.3);
		UITopBar bar = project.Window.Frame.Bar;
		Button maximize = bar.FindChildren("Maximize", "", true, false).OfType<Button>().Single();
		Button logo = bar.FindChildren("Logo", "", true, false).OfType<Button>().Single();
		Vector2 Middle(Control c) => c.GetGlobalTransform() * (c.Size / 2);

		Assert.Equal(HTMAXBUTTON, HitAt(project.Window, Middle(maximize)), "maximize, for Snap Layouts");
		Assert.Equal(HTCLIENT, HitAt(project.Window, Middle(logo)), "the logo is clickable");
		Assert.Equal(HTCAPTION, HitAt(project.Window, bar.GetGlobalTransform() * new Vector2(bar.Size.X * 0.6f, bar.Size.Y / 2)), "empty bar drags");
		Assert.Equal(HTCLIENT, HitAt(project.Window, (Vector2)project.Window.Size / 2 / project.Window.ContentScaleFactor), "the editor is content");
	}

	[Test]
	public async Task TheBarKeepsItsHeightWhenMaximized()
	{
		ProjectHandle project = await TestApp.NewProjectAsync();
		await TestApp.Seconds(0.3);
		float restored = project.Window.Frame.Bar.Size.Y;
		project.Window.Mode = Window.ModeEnum.Maximized;
		await TestApp.Seconds(0.5);
		Assert.Near(restored, project.Window.Frame.Bar.Size.Y, 0.5, "same height maximized");
		project.Window.Mode = Window.ModeEnum.Windowed;
		await TestApp.Seconds(0.3);
	}

	[Test]
	public async Task TheBarIgnoresTheInterfaceScale()
	{
		AppSettings.Current.InterfaceScale = InterfaceScale.Percent100;
		AppSettings.Current.Apply();
		ProjectHandle project = await TestApp.NewProjectAsync();
		await TestApp.Seconds(0.3);
		float onScreen = project.Window.Frame.Bar.GetGlobalTransform().Scale.Y * project.Window.Frame.Bar.Size.Y * project.Window.ContentScaleFactor;

		AppSettings.Current.InterfaceScale = InterfaceScale.Percent200;
		AppSettings.Current.Apply();
		foreach (Window w in TestApp.Tree.Root.FindChildren("*", "Window", true, false).OfType<Window>()) Screens.Scale(w);
		await TestApp.Seconds(0.5);
		float scaled = project.Window.Frame.Bar.GetGlobalTransform().Scale.Y * project.Window.Frame.Bar.Size.Y * project.Window.ContentScaleFactor;
		Assert.Near(onScreen, scaled, 1.5, "the bar's size on screen didn't change");
	}
}

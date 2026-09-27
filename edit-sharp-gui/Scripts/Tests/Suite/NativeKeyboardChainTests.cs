using EditSharpGUI.Api;
using EditSharpGUI.Scripts.Input;
using Godot;
using System.Linq;
using System.Threading.Tasks;

namespace EditSharpGUI.Tests;

// which view a real click focuses, and how that changes which shortcuts apply -- the real-input
// counterpart to KeyboardChainTests.SelectAllFollowsTheFocusedView (which drives Keyboard.Capture
// directly, bypassing the click that would normally cause it). Undo/redo, zoom and clip-editing
// shortcuts fired through real keys are already covered end to end in NativeTimelineTests; this
// fixture is specifically about a click ITSELF choosing the right view to route shortcuts to
[TestFixture]
[Windowed]
[OnlyOn("Windows")]
public sealed class NativeKeyboardChainTests
{
	ProjectHandle project;

	[SetUp]
	public async Task Open()
	{
		project = await TestApp.BlueprintProjectAsync();
		await TestApp.Seconds(0.5);
	}

	UIClip VisibleClip() => project.Editor.TimelineView.FindChildren("*", "", true, false).OfType<UIClip>()
		.First(u => project.Editor.TimelineView.ViewContains(u.GetGlobalRect().GetCenter()));

	[Test(Timeout = 30)]
	public async Task ClickingAMediaTileFocusesMediaOverTheTimeline()
	{
		UIMediaItem tile = project.Editor.MediaView.FindChildren("*", "", true, false).OfType<UIMediaItem>().First();
		await RealInput.MoveTo(RealInput.ScreenCenterOf(tile));
		RealInput.Click();
		await TestApp.Frames(2);

		Assert.Equal("media", project.Editor.FocusedView);
		Assert.False(project.Editor.CanRun(Shortcuts.Copy), "copy is a timeline action, not available with media focused");

		// clicking back into the timeline hands focus back
		await RealInput.MoveTo(RealInput.ScreenCenterOf(VisibleClip()));
		RealInput.Click();
		await TestApp.Frames(2);

		Assert.Equal("timeline", project.Editor.FocusedView);
	}
}

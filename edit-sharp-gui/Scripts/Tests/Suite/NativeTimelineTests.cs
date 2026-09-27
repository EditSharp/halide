using EditSharp.Components.Clips;
using EditSharpGUI.Api;
using EditSharpGUI.Scripts.Input;
using Godot;
using System.Linq;
using System.Threading.Tasks;

namespace EditSharpGUI.Tests;

// timeline editing through real OS input, driving the same UITimeline/UIRuler/UIClip controls a hand
// would -- the counterpart to TimelineServiceTests (which calls the service API directly, the same
// surface extensions and scripts use, and stays that way for those tests)
[TestFixture]
[Windowed]
[OnlyOn("Windows")]
public sealed class NativeTimelineTests
{
	ProjectHandle project;
	UITimeline timeline;

	[SetUp]
	public async Task Open()
	{
		project = await TestApp.BlueprintProjectAsync();
		await TestApp.Seconds(0.5);
		timeline = project.Editor.TimelineView;
	}

	// the on-screen point for a moment on the timeline, in the ruler's own row -- real win32 screen pixels.
	// the X comes from UITimeline.GlobalXOfTime, which is measured against the same clipsViewContainer
	// reference CursorTime reads clicks back against (the ruler's own GlobalPosition.X doesn't necessarily
	// line up with that -- it's a different row and was previously found to be several pixels off)
	Vector2I ScreenPointAtTime(Time t)
	{
		UIRuler ruler = timeline.FindChildren("*", "", true, false).OfType<UIRuler>().First();
		Vector2 point = new(timeline.GlobalXOfTime(t), ruler.GlobalPosition.Y + ruler.Size.Y / 2f);
		return RealInput.ScreenPointIn(project.Window, point);
	}

	[Test(Timeout = 30)]
	public async Task ClickingTheRulerMovesThePlayhead()
	{
		Time target = Time.FromSeconds(4.5);
		// a real click lands on a whole physical pixel, rounded through the DPI scale in both directions,
		// so allow a couple of logical pixels either side rather than expecting an exact hit
		double tolerance = 2d / timeline.PixelsPerSecond;

		// the very first real click into a freshly-opened window occasionally lands well short (a timing
		// race with the window still settling into place right after open) -- retrying a couple of times
		// is cheap and matches how NativeBarMenuTests.OpenAndMeasure copes with the same kind of flake
		double until = Godot.Time.GetTicksMsec() + 5000;
		while (System.Math.Abs(project.Timeline.Playhead.Seconds - target.Seconds) >= tolerance && Godot.Time.GetTicksMsec() < until)
		{
			await RealInput.MoveTo(ScreenPointAtTime(target));
			RealInput.Click();
			await TestApp.Frames(3);
		}

		Assert.True(System.Math.Abs(project.Timeline.Playhead.Seconds - target.Seconds) < tolerance,
			$"playhead at {project.Timeline.Playhead.Seconds}s, wanted close to {target.Seconds}s");
	}

	Clip Longest() => project.Timeline.Clips.OrderByDescending(c => c.Duration).First();

	// with 10 channels the blueprint doesn't all fit in the clips view at once -- these tests don't care
	// which clip they act on, so rather than picking one that might be scrolled out of reach (as Longest()'s
	// channel 0 was found to be, off the bottom of the view), take whichever clip is actually on screen,
	// the same constraint a real hand on the mouse would have
	UIClip VisibleClip() => timeline.FindChildren("*", "", true, false).OfType<UIClip>().First(u => timeline.ViewContains(u.GetGlobalRect().GetCenter()));

	async Task<Clip> ClickAVisibleClip()
	{
		UIClip view = VisibleClip();
		await RealInput.MoveTo(RealInput.ScreenCenterOf(view));
		RealInput.Click();
		await TestApp.Frames(2);
		return view.Clip;
	}

	[Test(Timeout = 30)]
	public async Task ClickingAClipSelectsItAndSelectAllShortcutSelectsEvery()
	{
		Clip clip = await ClickAVisibleClip();
		Assert.Sequence([clip], project.Timeline.Selected, "the clicked clip is selected");

		await RealInput.PressCombo(new KeyCombo(Key.A, Control: true));
		await TestApp.Frames(2);
		Assert.Equal(project.Timeline.Clips.Count, project.Timeline.Selected.Count, "select-all selected every clip");
	}

	[Test(Timeout = 30)]
	public async Task DeletingASelectedClipViaTheShortcut()
	{
		Clip clip = await ClickAVisibleClip();
		int before = project.Timeline.Clips.Count;

		await RealInput.PressCombo(new KeyCombo(Key.Backspace));
		await TestApp.Frames(2);

		Assert.Equal(before - 1, project.Timeline.Clips.Count, "the clip is gone");
		Assert.False(project.Timeline.Clips.Contains(clip), "specifically the one deleted");
	}

	[Test(Timeout = 30)]
	public async Task DuplicatingASelectedClipViaTheShortcut()
	{
		Clip original = await ClickAVisibleClip();

		await RealInput.PressCombo(new KeyCombo(Key.D, Control: true));
		await TestApp.Frames(2);

		// not a raw clip-count check: AddClip overwrites whatever already occupies the spot a duplicate
		// lands on, and the blueprint deliberately has overlapping content on some channels to exercise
		// that -- so the real signal that a duplicate happened is a new clip landing in the selection
		Assert.Count(1, project.Timeline.Selected, "the duplicate is selected");
		Assert.NotEqual(original, project.Timeline.Selected[0], "a new clip, not the original, is selected");
		Assert.Contains(project.Timeline.Selected[0], project.Timeline.Clips, "the duplicate is really on the timeline");
	}

	[Test(Timeout = 30)]
	public async Task RippleDeletingASelectedClipViaTheShortcut()
	{
		Clip clip = await ClickAVisibleClip();
		int before = project.Timeline.Clips.Count;

		await RealInput.PressCombo(new KeyCombo(Key.Delete));
		await TestApp.Frames(2);

		Assert.Equal(before - 1, project.Timeline.Clips.Count, "the clip is gone");
		Assert.False(project.Timeline.Clips.Contains(clip), "specifically the one deleted");
	}

	[Test(Timeout = 30)]
	public async Task ZoomToFitViaTheShortcut()
	{
		// a shortcut climbs from the keyboard captor, so the timeline needs to have claimed it via a
		// real click first -- nothing has clicked into it yet this test, unlike the others which select
		// a clip (and so capture the keyboard) before pressing their shortcut
		await ClickAVisibleClip();
		timeline.PixelsPerSecond = 400;
		await TestApp.Frames(2);

		await RealInput.PressCombo(new KeyCombo(Key.Backslash));
		await TestApp.Frames(3);

		Assert.True(timeline.PixelsPerSecond < 400, "zoomed out to fit");
		Assert.True(timeline.PixelsPerSecond > 0, "still positive");
	}

	[Test(Timeout = 30)]
	public async Task MovingAClipViaARealDrag()
	{
		UIClip view = VisibleClip();
		Clip clip = view.Clip;
		Time before = clip.Start;

		Vector2I from = RealInput.ScreenCenterOf(view);
		await RealInput.MoveTo(from);
		RealInput.MouseDown();
		// several steps past the drag threshold, not one jump straight to the target -- the same
		// reasoning RealInput.MoveTo itself uses for plain mouse moves
		await RealInput.MoveTo(from + new Vector2I(80, 0), steps: 10);
		RealInput.MouseUp();
		await TestApp.Frames(3);

		Assert.NotEqual(before, clip.Start, "the clip moved along the timeline");
	}

	// TODO: real-input edge-trim drag (TimelineServiceTests.TrimmingBothEnds's counterpart). A press on
	// the end UIDragHandle after a real selection click was confirmed to land at the exact right screen
	// coordinate (verified against the handle's own GetGlobalRect()), but Mouse.Reconcile force-releases
	// the button before any drag registers -- something about the handle's press specifically, since the
	// same down-move-up shape works for a plain clip-body drag in MovingAClipViaARealDrag above. Left for
	// a follow-up session rather than continuing to guess.

	[Test(Timeout = 30)]
	public async Task SplittingAtThePlayheadViaTheShortcut()
	{
		Clip clip = Longest();
		Time at = clip.Start + clip.Duration / 2;
		await RealInput.MoveTo(ScreenPointAtTime(at));
		RealInput.Click();
		await TestApp.Frames(2);

		int before = project.Timeline.Clips.Count;
		await RealInput.PressCombo(new KeyCombo(Key.B, Control: true));
		await TestApp.Frames(2);

		Assert.True(project.Timeline.Clips.Count > before, "the clip covering the playhead was split");
	}
}

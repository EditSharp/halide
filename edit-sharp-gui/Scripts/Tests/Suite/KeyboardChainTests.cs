using EditSharpGUI.Api;
using EditSharpGUI.Scripts.App.Commands;
using EditSharpGUI.Scripts.Input;
using System.Threading.Tasks;

namespace EditSharpGUI.Tests;

// menu commands act through the same handlers as their keys, on the view last clicked
[TestFixture]
public sealed class KeyboardChainTests
{
	ProjectHandle project;
	Keyboard Keys => InputManager.Singleton.Keyboard;

	[SetUp]
	public async Task Open() => project = await TestApp.BlueprintProjectAsync();

	[TearDown]
	public void LetGo() => Keys.Release(Keys.Captor);

	[Test]
	public void UndoAndRedoGoThroughTheEditor()
	{
		project.Timeline.AddChannel(video: true);
		int position = project.History.Position;
		Assert.True(Keys.Invoke(Shortcuts.Undo, project.Editor));
		Assert.Equal(position - 1, project.History.Position);
		Assert.True(Commands.Run(Shortcuts.Redo, project.Context));
		Assert.Equal(position, project.History.Position);
	}

	[Test]
	public void TimelineCommandsReachTheTimeline()
	{
		Keys.Capture(project.Editor.TimelineView);
		Commands.Run(Shortcuts.SelectAll, project.Context);
		Assert.Equal(project.Timeline.Clips.Count, project.Timeline.Selected.Count);
		Commands.Run(Shortcuts.Deselect, project.Context);
		Assert.Count(0, project.Timeline.Selected);
	}

	[Test]
	public void SelectAllFollowsTheFocusedView()
	{
		Keys.Capture(project.Editor.MediaView);
		Assert.Equal("media", project.Editor.FocusedView);
		Assert.False(project.Editor.CanRun(Shortcuts.Copy), "copy is a timeline action");
		Keys.Capture(project.Editor.TimelineView);
		Assert.Equal("timeline", project.Editor.FocusedView);
	}

	[Test]
	public void CopyAndPasteClips()
	{
		Keys.Capture(project.Editor.TimelineView);
		project.Timeline.Select([project.Timeline.Clips[0]]);
		project.Timeline.Playhead = project.Timeline.Timeline.Duration + EditSharp.Time.FromSeconds(10);
		int before = project.Timeline.Clips.Count;
		Assert.True(Commands.Run(Shortcuts.Copy, project.Context));
		Assert.True(project.Editor.CanRun(Shortcuts.Paste), "paste lights up");
		Assert.True(Commands.Run(Shortcuts.Paste, project.Context));
		Assert.Equal(before + 1, project.Timeline.Clips.Count);
	}

	[Test]
	public void DuplicateThroughTheMenu()
	{
		Keys.Capture(project.Editor.TimelineView);
		project.Timeline.Select([project.Timeline.Clips[0]]);
		int before = project.Timeline.Clips.Count;
		Assert.True(Commands.Run(Shortcuts.Duplicate, project.Context));
		Assert.Equal(before + 1, project.Timeline.Clips.Count);
	}

	[Test]
	public void GoToStartAndEnd()
	{
		project.Timeline.Playhead = EditSharp.Time.FromSeconds(3);
		Commands.Run(Shortcuts.GoToEnd, project.Context);
		Assert.Equal(project.Timeline.Timeline.Duration, project.Timeline.Playhead);
		Commands.Run(Shortcuts.GoToStart, project.Context);
		Assert.Equal(EditSharp.Time.Zero, project.Timeline.Playhead);
	}

	[Test]
	public void ZoomCommandsChangeTheScale()
	{
		double start = project.Timeline.PixelsPerSecond;
		Commands.Run(Shortcuts.ZoomIn, project.Context);
		Assert.True(project.Timeline.PixelsPerSecond > start);
		Commands.Run(Shortcuts.ZoomOut, project.Context);
		Commands.Run(Shortcuts.ZoomOut, project.Context);
		Assert.True(project.Timeline.PixelsPerSecond < start);
	}

	[Test]
	public void LayoutShortcutsSwitchLayouts()
	{
		Commands.Run(Shortcuts.Layout(2), project.Context);
		Assert.Equal("Assembly", project.Layout.Active);
		Commands.Run(Shortcuts.ResetLayout, project.Context);
		Assert.Equal("Assembly", project.Layout.Active, "reset keeps the layout, as saved");
	}

	[Test]
	public void FloatFocusedViewFloatsIt()
	{
		Keys.Capture(project.Editor.InspectorView);
		Commands.Run(Shortcuts.FloatFocused, project.Context);
		Assert.True(project.Layout.IsFloating("inspector"));
	}

	[Test]
	public void AnActionNobodyTakesReportsFalse()
	{
		Assert.False(Keys.Invoke("test.nobody.handles.this", project.Editor));
	}
}

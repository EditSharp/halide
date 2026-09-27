using EditSharp;
using EditSharp.Components.Clips;
using EditSharp.Components.Media;
using EditSharpGUI.Api;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EditSharpGUI.Tests;

// timeline edits through the service: each is one undo entry, and undo puts everything back
[TestFixture]
public sealed class TimelineServiceTests
{
	ProjectHandle project;

	[SetUp]
	public async Task Open() => project = await TestApp.BlueprintProjectAsync();

	TimelineService Timeline => project.Timeline;

	string Snapshot() => string.Join(";", Timeline.Clips.Select(c => $"{c.Id}@{c.Channel?.GetType().Name}{c.Channel?.Index}:{c.Start.Ticks}-{c.End.Ticks}"));

	// the edit is one entry named as given; undo restores and redo reapplies
	void Undoable(Action edit, string name = null)
	{
		string before = Snapshot();
		int position = project.History.Position;
		edit();
		string after = Snapshot();
		Assert.NotEqual(before, after, "the edit changed something");
		Assert.Equal(position + 1, project.History.Position, "one history entry");
		if (name is not null) Assert.True(project.History.UndoDescription?.StartsWith(name) == true, $"named '{name}': got '{project.History.UndoDescription}'");

		project.History.Undo();
		Timeline.Reconcile();
		Assert.Equal(before, Snapshot(), "undo restores");
		project.History.Redo();
		Assert.Equal(after, Snapshot(), "redo reapplies");
	}

	Clip Longest() => Timeline.Clips.OrderByDescending(c => c.Duration).First();

	[Test]
	public void TheBlueprintHasClipsOnBothKinds()
	{
		Assert.True(Timeline.Clips.OfType<VideoClip>().Any());
		Assert.True(Timeline.Clips.OfType<AudioClip>().Any());
	}

	[Test]
	public async Task PlacingMediaAddsClipsAsOneEntry()
	{
		IMedia media = project.Media.Import([TestApp.ExampleVideo]).FirstOrDefault() ?? project.Media.Find(TestApp.ExampleVideo);
		await TestApp.WaitUntil(() => media.TryGetNaturalLength(out _), "the video's length is known", 20);
		int channel = Timeline.AddChannel(video: true).Index;

		IReadOnlyList<Clip> made = null;
		Undoable(() => made = Timeline.Place(media, Time.FromSeconds(40), channel));
		Assert.True(made.Count >= 1, "clips were made");
		Assert.True(made.All(c => c.Start == Time.FromSeconds(40)), "at the time asked");
	}

	[Test]
	public void MovingAClip()
	{
		Clip clip = Longest();
		Undoable(() => Timeline.Move(clip, clip.Start + Time.FromSeconds(30)), "Move");
	}

	[Test]
	public void TrimmingBothEnds()
	{
		Clip clip = Longest();
		Undoable(() => Timeline.TrimStart(clip, Time.FromSeconds(1)), "Trim");
		Time end = clip.End;
		Timeline.TrimEnd(clip, Time.FromSeconds(1));
		Assert.Equal(end - Time.FromSeconds(1), clip.End);
	}

	[Test]
	public void SplittingEveryClipAtATime()
	{
		Time at = Longest().Start + Longest().Duration / 2;
		int covering = Timeline.ClipsAt(at).Count;
		int before = Timeline.Clips.Count;
		Undoable(() => Timeline.Split(at), "Split");
		Assert.Equal(before + covering, Timeline.Clips.Count, "every covering clip became two");
	}

	[Test]
	public void SplittingOnlyTheClipsGiven()
	{
		Clip clip = Longest();
		Time at = clip.Start + clip.Duration / 2;
		int before = Timeline.Clips.Count;
		Timeline.Split(at, [clip]);
		Assert.Equal(before + 1, Timeline.Clips.Count);
	}

	[Test]
	public void SplittingOutsideAClipDoesNothing()
	{
		Clip clip = Longest();
		int position = project.History.Position;
		Timeline.Split(clip.End + Time.FromSeconds(500), [clip]);
		Assert.Equal(position, project.History.Position, "no entry for nothing");
	}

	[Test]
	public void DeletingLeavesTheGap()
	{
		Clip clip = Longest();
		List<Clip> after = [.. clip.Channel.Clips.Where(c => c.Start >= clip.End)];
		List<Time> starts = [.. after.Select(c => c.Start)];
		Undoable(() => Timeline.Delete([clip]), "Delete");
		Assert.Sequence(starts, after.Select(c => c.Start), "what followed stays put");
	}

	[Test]
	public void RippleDeletingClosesTheGap()
	{
		Clip clip = Longest();
		List<Clip> after = [.. clip.Channel.Clips.Where(c => c.Start >= clip.End)];
		Time duration = clip.Duration;
		List<Time> expected = [.. after.Select(c => c.Start - duration)];
		Timeline.Delete([clip], ripple: true);
		Assert.Sequence(expected, after.Select(c => c.Start), "what followed moved up");
	}

	[Test]
	public void DuplicatingPutsCopiesAfter()
	{
		Clip clip = Longest();
		IReadOnlyList<Clip> copies = Timeline.Duplicate([clip]);
		Assert.Count(1, copies);
		Assert.Equal(clip.End, copies[0].Start);
		Assert.Equal(clip.Duration, copies[0].Duration);
		Assert.Sequence(copies, Timeline.Selected, "the copies are selected");

		project.History.Undo();
		Timeline.Reconcile();
		Undoable(() => Timeline.Duplicate([clip]));
	}

	[Test]
	public void AddingChannels()
	{
		int videos = Timeline.Timeline.VideoChannels.Count;
		Assert.Equal(videos, Timeline.AddChannel(video: true).Index);
		Assert.Equal(videos + 1, Timeline.Timeline.VideoChannels.Count);
		project.History.Undo();
		Assert.Equal(videos, Timeline.Timeline.VideoChannels.Count);
	}

	[Test]
	public void LinkingClips()
	{
		List<Clip> two = [.. Timeline.Clips.Where(c => c.LinkGroupId is null).Take(2)];
		if (two.Count < 2) Assert.Skip("the blueprint has no two unlinked clips");
		Timeline.Link(two);
		Assert.True(two[0].LinkGroupId is not null, "linked");
		Assert.Equal(two[0].LinkGroupId, two[1].LinkGroupId);
	}

	[Test]
	public void SelectingAndDeselecting()
	{
		Timeline.SelectAll();
		Assert.Equal(Timeline.Clips.Count, Timeline.Selected.Count);
		Timeline.Select([Longest()]);
		Assert.Sequence([Longest()], Timeline.Selected);
		Timeline.Deselect();
		Assert.Count(0, Timeline.Selected);
	}

	[Test]
	public void ThePlayheadMoves()
	{
		Timeline.Playhead = Time.FromSeconds(4.5);
		Assert.Equal(Time.FromSeconds(4.5), Timeline.Playhead);
		Assert.Equal(Time.FromSeconds(4.5), project.Playback.Position);
	}

	[Test]
	public async Task ZoomingToFitShowsTheWholeTimeline()
	{
		Timeline.PixelsPerSecond = 400;
		Timeline.ZoomToFit();
		await TestApp.Frames(2);
		Assert.True(Timeline.PixelsPerSecond < 400, "zoomed out");
		Assert.True(Timeline.PixelsPerSecond > 0, "still positive");
	}

	[Test]
	public void ABatchIsOneEntry()
	{
		int position = project.History.Position;
		Clip clip = Longest();
		using (project.Batch("Rough cut"))
		{
			Timeline.Move(clip, clip.Start + Time.FromSeconds(10));
			Timeline.AddChannel(video: false);
			Timeline.TrimEnd(clip, Time.FromSeconds(1));
		}
		Assert.Equal(position + 1, project.History.Position);
		Assert.Equal("Rough cut", project.History.UndoDescription);
	}

	[Test]
	public void BatchesNestIntoTheOuterEntry()
	{
		int position = project.History.Position;
		using (project.Batch("Outer"))
		{
			using (project.Batch("Inner")) Timeline.AddChannel(video: true);
			Timeline.AddChannel(video: false);
		}
		Assert.Equal(position + 1, project.History.Position);
		Assert.Equal("Outer", project.History.UndoDescription);
	}

	[Test]
	public void ACancelledBatchChangesNothing()
	{
		string before = Snapshot();
		int channels = Timeline.Timeline.Channels.Count;
		int position = project.History.Position;
		using (ProjectBatch batch = project.Batch("Never mind"))
		{
			Timeline.Move(Longest(), Longest().Start + Time.FromSeconds(10));
			Timeline.AddChannel(video: true);
			batch.Cancel();
		}
		Timeline.Reconcile();
		Assert.Equal(before, Snapshot());
		Assert.Equal(channels, Timeline.Timeline.Channels.Count);
		Assert.Equal(position, project.History.Position);
	}

	[Test]
	public void ABatchThatThrowsRollsBack()
	{
		string before = Snapshot();
		Assert.Throws<InvalidOperationException>(() => project.Batch("Doomed", () =>
		{
			Timeline.Move(Longest(), Longest().Start + Time.FromSeconds(10));
			throw new InvalidOperationException("stop");
		}));
		Timeline.Reconcile();
		Assert.Equal(before, Snapshot());
	}

	[Test]
	public void ChangedFiresOnEdits()
	{
		int changes = 0;
		void Count() => changes++;
		Timeline.Changed += Count;
		try { Timeline.AddChannel(video: true); }
		finally { Timeline.Changed -= Count; }
		Assert.True(changes >= 1);
	}

	[Test]
	public void FindingClipsById()
	{
		Clip clip = Longest();
		Assert.Equal(clip, Timeline.Find(clip.Id));
		Assert.Null(Timeline.Find(Guid.NewGuid()));
	}
}

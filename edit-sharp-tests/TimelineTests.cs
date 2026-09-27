using EditSharp.Components;
using EditSharp.Components.Channels;
using EditSharp.Components.Clips;
using EditSharp.History;
using System.Linq;
using Xunit;

namespace EditSharp.Tests;

using History = EditSharp.History.History;

// timeline editing on the model: channels, clips, moves, trims, splits, ripples and links
public class TimelineTests
{
	static Time S(double seconds) => Time.FromSeconds(seconds);

	// a timeline with two video channels and an audio channel, built outside any history
	static Timeline Build()
	{
		using var _ = Transaction.Suppress();
		Timeline timeline = new();
		timeline.AddChannel(new VideoChannel());
		timeline.AddChannel(new VideoChannel());
		timeline.AddChannel(new AudioChannel());
		return timeline;
	}

	static VideoClip Noise(double start, double duration) => VideoClip.CreateNoise(S(start), S(duration), seed: 1);

	[Fact]
	public void ChannelsKeepTheirKindsAndOrder()
	{
		Timeline timeline = Build();
		Assert.Equal(2, timeline.VideoChannels.Count);
		Assert.Single(timeline.AudioChannels);
		Assert.Equal(0, timeline.VideoChannels[0].Index);
		Assert.Equal(1, timeline.VideoChannels[1].Index);
	}

	[Fact]
	public void ClipsLandWhereTheyreAdded()
	{
		using var _ = Transaction.Suppress();
		Timeline timeline = Build();
		VideoClip clip = (VideoClip)timeline.VideoChannels[0].AddClip(Noise(2, 3));
		Assert.Equal(S(2), clip.Start);
		Assert.Equal(S(5), clip.End);
		Assert.Equal(timeline.VideoChannels[0], clip.Channel);
		Assert.Equal(S(5), timeline.Duration);
	}

	[Fact]
	public void MovingKeepsTheLengthAndCanChangeChannel()
	{
		using var _ = Transaction.Suppress();
		Timeline timeline = Build();
		Clip clip = timeline.VideoChannels[0].AddClip(Noise(0, 4));
		clip.Move(S(10), timeline.VideoChannels[1]);
		Assert.Equal(S(10), clip.Start);
		Assert.Equal(S(4), clip.Duration);
		Assert.Equal(timeline.VideoChannels[1], clip.Channel);
		Assert.Empty(timeline.VideoChannels[0].Clips);
	}

	[Fact]
	public void TrimmingShortensFromEitherEnd()
	{
		using var _ = Transaction.Suppress();
		Timeline timeline = Build();
		Clip clip = timeline.VideoChannels[0].AddClip(Noise(0, 10));
		clip.TrimStart(S(2));
		Assert.Equal(S(2), clip.Start);
		Assert.Equal(S(10), clip.End);
		clip.TrimEnd(S(3));
		Assert.Equal(S(7), clip.End);
	}

	[Fact]
	public void SplittingMakesTwoClipsCoveringTheSameSpan()
	{
		using var _ = Transaction.Suppress();
		Timeline timeline = Build();
		Clip clip = timeline.VideoChannels[0].AddClip(Noise(0, 10));
		clip.Split(S(4));
		Clip[] halves = [.. timeline.VideoChannels[0].Clips.OrderBy(c => c.Start)];
		Assert.Equal(2, halves.Length);
		Assert.Equal(S(0), halves[0].Start);
		Assert.Equal(S(4), halves[0].End);
		Assert.Equal(S(4), halves[1].Start);
		Assert.Equal(S(10), halves[1].End);
	}

	[Fact]
	public void DeletingLeavesAGapAndRippleDeletingClosesIt()
	{
		using var _ = Transaction.Suppress();
		Timeline timeline = Build();
		Channel channel = timeline.VideoChannels[0];
		Clip first = channel.AddClip(Noise(0, 2));
		Clip second = channel.AddClip(Noise(2, 2));
		Clip third = channel.AddClip(Noise(4, 2));

		second.Delete();
		Assert.Equal(S(4), third.Start);

		first.RippleDelete();
		Assert.Equal(S(2), third.Start);
	}

	[Fact]
	public void RemovingARangeCutsIntoClips()
	{
		using var _ = Transaction.Suppress();
		Timeline timeline = Build();
		Channel channel = timeline.VideoChannels[0];
		channel.AddClip(Noise(0, 10));
		channel.RemoveRange(S(3), S(5));
		Clip[] left = [.. channel.Clips.OrderBy(c => c.Start)];
		Assert.Equal(2, left.Length);
		Assert.Equal(S(3), left[0].End);
		Assert.Equal(S(5), left[1].Start);
	}

	[Fact]
	public void LinkedClipsShareAGroup()
	{
		using var _ = Transaction.Suppress();
		Timeline timeline = Build();
		Clip a = timeline.VideoChannels[0].AddClip(Noise(0, 2));
		Clip b = timeline.VideoChannels[1].AddClip(Noise(0, 2));
		timeline.Link([a, b]);
		Assert.NotNull(a.LinkGroupId);
		Assert.Equal(a.LinkGroupId, b.LinkGroupId);
	}

	[Fact]
	public void AnEditIsUndoneExactly()
	{
		History history = new();
		Timeline timeline = Build();
		Clip clip;
		using (Transaction.Suppress()) clip = timeline.VideoChannels[0].AddClip(Noise(0, 4));

		using (Transaction.Scope scope = history.Begin("Move"))
		{
			clip.Move(S(8));
			scope.Commit();
		}
		history.Undo();
		Assert.Equal(S(0), clip.Start);
		history.Redo();
		Assert.Equal(S(8), clip.Start);
	}

	[Fact]
	public void DuplicatesAreIndependent()
	{
		using var _ = Transaction.Suppress();
		Timeline timeline = Build();
		Clip clip = timeline.VideoChannels[0].AddClip(Noise(0, 4));
		Clip copy = clip.Duplicate();
		Assert.NotEqual(clip.Id, copy.Id);
		Assert.Equal(clip.Duration, copy.Duration);
		Assert.Null(copy.Channel);
	}
}

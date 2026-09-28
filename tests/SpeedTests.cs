using EditSharp.Components;
using EditSharp.Components.Channels;
using EditSharp.Components.Clips;
using EditSharp.History;
using Xunit;

namespace EditSharp.Tests;

using History = EditSharp.History.History;

// clip speed: linked partners follow, freeze remembers the speed, reverse flips the sign
public class SpeedTests
{
	static Time S(double seconds) => Time.FromSeconds(seconds);

	static (Timeline Timeline, Clip Video, Clip Audio) Linked()
	{
		using var quiet = Transaction.Suppress();
		Timeline timeline = new();
		timeline.AddChannel(new VideoChannel());
		timeline.AddChannel(new AudioChannel());
		Clip video = timeline.VideoChannels[0].AddClip(VideoClip.CreateNoise(S(0), S(4), seed: 1));
		Clip audio = timeline.AudioChannels[0].AddClip(AudioClip.CreateTone(S(0), S(4)));
		timeline.Link([video, audio]);
		return (timeline, video, audio);
	}

	[Fact]
	public void LinkedPartnersShareTheSpeed()
	{
		(_, Clip video, Clip audio) = Linked();
		using var quiet = Transaction.Suppress();
		video.Speed = new Rational(2, 1);
		Assert.Equal(new Rational(2, 1), audio.Speed);
	}

	[Fact]
	public void FreezingRemembersTheSpeed()
	{
		(_, Clip video, _) = Linked();
		using var quiet = Transaction.Suppress();
		video.Speed = new Rational(3, 2);
		video.Frozen = true;
		Assert.True(video.Frozen);
		Assert.Equal(Rational.Zero, video.Speed);
		video.Frozen = false;
		Assert.Equal(new Rational(3, 2), video.Speed);
	}

	[Fact]
	public void FreezeHoldsTheFrameUnderTheMoment()
	{
		(_, Clip video, _) = Linked();
		using var quiet = Transaction.Suppress();
		video.Freeze(S(1));
		Assert.True(video.Frozen);
		Assert.Equal(S(1), video.FreezeAt);
	}

	[Fact]
	public void ReversingFlipsTheSignForPartnersToo()
	{
		(_, Clip video, Clip audio) = Linked();
		using var quiet = Transaction.Suppress();
		video.Reverse();
		Assert.True(video.IsReversed);
		Assert.True(audio.IsReversed);
		video.Reverse();
		Assert.False(video.IsReversed);
	}

	[Fact]
	public void AFrozenClipDoesntReverse()
	{
		(_, Clip video, _) = Linked();
		using var quiet = Transaction.Suppress();
		video.Frozen = true;
		video.Reverse();
		Assert.False(video.IsReversed);
	}

	[Fact]
	public void ASpeedChangeUndoesForBoth()
	{
		(_, Clip video, Clip audio) = Linked();
		History history = new();
		using (Transaction.Scope scope = history.Begin("Speed"))
		{
			video.Speed = new Rational(1, 2);
			scope.Commit();
		}
		history.Undo();
		Assert.Equal(Rational.One, video.Speed);
		Assert.Equal(Rational.One, audio.Speed);
	}
}

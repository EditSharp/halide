using EditSharp.Components;
using EditSharp.Components.Channels;
using EditSharp.Components.Clips;
using EditSharp.History;
using System.Linq;
using Xunit;

namespace EditSharp.Tests;

// saving and loading timelines keeps every clip, time, speed and link exactly
public class DocumentTests
{
	static Time S(double seconds) => Time.FromSeconds(seconds);

	static Timeline Sample()
	{
		using var _ = Transaction.Suppress();
		Timeline timeline = new();
		timeline.AddChannel(new VideoChannel());
		timeline.AddChannel(new AudioChannel());
		Clip video = timeline.VideoChannels[0].AddClip(VideoClip.CreateNoise(S(1), S(4), seed: 7));
		Clip tone = timeline.AudioChannels[0].AddClip(AudioClip.CreateTone(S(1), S(4)));
		video.Name = "Noise";
		video.Color = "#336699";
		timeline.Link([video, tone]);
		timeline.VideoChannels[0].AddClip(VideoClip.CreateNoise(S(6), S(2), seed: 3)).Speed = new Rational(1, 2);
		return timeline;
	}

	static Timeline RoundTrip(Timeline timeline)
	{
		string json = TimelineDocument.Serialize([timeline], []);
		TimelineDocumentContent content = TimelineDocument.Deserialize(json);
		Assert.Empty(content.Warnings);
		return Assert.Single(content.Timelines);
	}

	[Fact]
	public void ChannelsAndClipsSurvive()
	{
		Timeline before = Sample(), after = RoundTrip(before);
		Assert.Equal(before.Id, after.Id);
		Assert.Equal(before.Channels.Count, after.Channels.Count);
		Assert.Equal(before.Channels.SelectMany(c => c.Clips).Count(), after.Channels.SelectMany(c => c.Clips).Count());
		Assert.Equal(before.Duration, after.Duration);
	}

	[Fact]
	public void ClipDetailsSurvive()
	{
		Timeline before = Sample(), after = RoundTrip(before);
		foreach (Clip clip in before.Channels.SelectMany(c => c.Clips))
		{
			Clip loaded = after.Channels.SelectMany(c => c.Clips).Single(x => x.Id == clip.Id);
			Assert.Equal(clip.GetType(), loaded.GetType());
			Assert.Equal(clip.Start, loaded.Start);
			Assert.Equal(clip.Duration, loaded.Duration);
			Assert.Equal(clip.Speed, loaded.Speed);
			Assert.Equal(clip.Name, loaded.Name);
			Assert.Equal(clip.Color, loaded.Color);
			Assert.Equal(clip.LinkGroupId, loaded.LinkGroupId);
			Assert.Equal(clip.Channel!.Index, loaded.Channel!.Index);
		}
	}

	[Fact]
	public void SavingTwiceGivesTheSameText()
	{
		Timeline timeline = Sample();
		string first = TimelineDocument.Serialize([timeline], []);
		string second = TimelineDocument.Serialize([RoundTrip(timeline)], []);
		Assert.Equal(first, second);
	}

	[Fact]
	public void LoadingIsNotAnUndoableEdit()
	{
		EditSharp.History.History history = new();
		RoundTrip(Sample());
		Assert.False(history.CanUndo);
	}

	[Fact]
	public void GarbageIsRejected()
	{
		Assert.ThrowsAny<System.Text.Json.JsonException>(() => TimelineDocument.Deserialize("not json"));
	}

	[Fact]
	public void NewerDocumentsAreRejected()
	{
		string json = TimelineDocument.Serialize([Sample()], []).Replace("\"formatVersion\": ", "\"formatVersion\": 9999");
		Assert.Throws<System.NotSupportedException>(() => TimelineDocument.Deserialize(json));
	}
}

using EditSharpGUI.Api;
using EditSharpGUI.Scripts.UI.Thumbnails;
using Godot;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace EditSharpGUI.Tests;

// what needs a real renderer: waveforms and thumbnails appearing, playback running
[TestFixture]
[Windowed]
public sealed class RenderingTests
{
	[Test(Timeout = 60)]
	public async Task WaveformsAppearAfterLoad()
	{
		ProjectHandle project = await TestApp.BlueprintProjectAsync();
		FieldInfo envelope = typeof(WaveformView).GetField("envelope", BindingFlags.NonPublic | BindingFlags.Instance);
		await TestApp.WaitUntil(() => project.Window.FindChildren("*", "", true, false).OfType<WaveformStrip>().Any(s => s.IsVisibleInTree() && envelope.GetValue(s) is not null), "an audio clip shows its waveform", 40);
	}

	[Test(Timeout = 60)]
	public async Task WaveformsSurviveMovingTheTimeline()
	{
		ProjectHandle project = await TestApp.BlueprintProjectAsync();
		FieldInfo envelope = typeof(WaveformView).GetField("envelope", BindingFlags.NonPublic | BindingFlags.Instance);
		project.Layout.Float("timeline");
		await TestApp.WaitUntil(() => project.Window.FindChildren("*", "", true, false).OfType<WaveformStrip>().Any(s => s.IsVisibleInTree() && envelope.GetValue(s) is not null), "the floated timeline shows waveforms", 40);
	}

	[Test(Timeout = 60)]
	public async Task PlaybackPlaysAndPauses()
	{
		// a generated clip, so the clock doesn't wait on video decoding
		ProjectHandle project = await TestApp.NewProjectAsync();
		EditSharp.Components.Channels.Channel channel = project.Timeline.AddChannel(true);
		project.Batch("Add noise", () => channel.AddClip(EditSharp.Components.Clips.VideoClip.CreateNoise(EditSharp.Time.Zero, EditSharp.Time.FromSeconds(10), seed: 1)));
		project.Playback.Play();
		await TestApp.WaitUntil(() => project.Playback.Playing, "playing", 10);
		// software rendering on CI can take seconds before the first frame starts the clock
		await TestApp.WaitUntil(() => project.Playback.Position > EditSharp.Time.Zero, "time moved", 20);
		project.Playback.Pause();
		await TestApp.WaitUntil(() => !project.Playback.Playing, "paused", 10);
		EditSharp.Time paused = project.Playback.Position;
		await TestApp.Seconds(0.3);
		Assert.Equal(paused, project.Playback.Position);
	}

	[Test(Timeout = 30)]
	public async Task SeekingMovesThePicture()
	{
		ProjectHandle project = await TestApp.BlueprintProjectAsync();
		project.Playback.Seek(EditSharp.Time.FromSeconds(2));
		await TestApp.Seconds(0.3);
		Assert.Equal(EditSharp.Time.FromSeconds(2), project.Playback.Position);
	}
}

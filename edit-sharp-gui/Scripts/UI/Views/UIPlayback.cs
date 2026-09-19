using EditSharp.Playback;
using Godot;
using Godot.NativeInterop;
using System;
using System.Diagnostics;
using System.Linq;

public partial class UIPlayback : Control
{
	[ExportGroup("Video")]

	[Export] TextureRect videoStreamPlayer;
	[Export] ImageTexture videoTexture;

	[ExportGroup("Audio")]

	[Export] AudioStreamPlayer audioStreamPlayer;

	[ExportGroup("Controls")]

	[Export] Button playButton;
	[Export] Label timestamp;
	[Export] Slider slider;


	Playback playback;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		audioStreamPlayer.Play();
		_generatorPlayback = (AudioStreamGeneratorPlayback)audioStreamPlayer.GetStreamPlayback();

		playButton.Pressed += PlayButton_Pressed;

		slider.DragStarted += Slider_DragStarted;
		slider.ValueChanged += Slider_ValueChanged;
		slider.DragEnded += Slider_DragEnded;
	}

	public void SetPlayback(Playback p)
	{
		playback = p;

		playback.VideoFrame += OnVideoFrame;
		playback.AudioSample += OnAudioSample;
		playback.EndReached += OnEndReached;
	}

	// where playback is, as far as anything watching from outside should know -
	// a timeline playhead, say. raised on the main thread only: from _Process
	// while playing, and from a scrub as it is requested. never from the
	// decoder's own thread, so a listener can touch scene nodes directly
	public event EventHandler<TimeSpan> PositionChanged;

	public PlaybackState State => playback?.State ?? PlaybackState.Inactive;

	TimeSpan? reportedPosition;

	// set from the playback thread when the end is hit, picked up here so the
	// last report is the very end rather than the last frame delivered
	bool endReached;

	public override void _Process(double delta)
	{
		if (playback is null) return;

		if (playback.State == PlaybackState.Playing)
		{
			// the clock extrapolates, so it can read a hair past the end
			TimeSpan position = playback.Position;
			TimeSpan duration = playback.Timeline.Duration;

			ReportPosition(position > duration ? duration : position);
		}
		else if (endReached)
		{
			endReached = false;
			ReportPosition(playback.Timeline.Duration);
		}
	}

	void ReportPosition(TimeSpan position)
	{
		if (reportedPosition == position) return;

		reportedPosition = position;
		PositionChanged?.Invoke(this, position);
	}

	// what the play button does, for whoever else wants to do it - a shortcut, say
	public void TogglePlayback() => PlayButton_Pressed();

	void PlayButton_Pressed()
    {
        if (playback.State == PlaybackState.Inactive)
		{
			//setup playback
			InitializeFramebuffer((int)playback.RenderSettings.Resolution.X, (int)playback.RenderSettings.Resolution.Y);

			// start from wherever the position was left - a scrub while stopped
			// moves it. at the very end there is nothing left to play, so go
			// round to the start instead of playing one frame and stopping
			TimeSpan start = playback.Position;
			TimeSpan frame = TimeSpan.FromSeconds(1d / playback.RenderSettings.Framerate);

			if (start + frame >= playback.Timeline.Duration) start = TimeSpan.Zero;

			playback.Play(start);
			SetPlayButtonText("Pause");
		}
		else
		{
			if (playback.State == PlaybackState.Paused || playback.State == PlaybackState.Scrubbing)
			{
				//unpause
				playback.Play();
				SetPlayButtonText("Pause");
			}
			else
			{
				//pause
				playback.Pause();
				SetPlayButtonText("Play");
				SetTimestamp(playback.Position, playback.Timeline.Duration, playback.RenderSettings.Framerate);
			}
		}
    }

	// the slider is one way to scrub. anything outside - a timeline playhead -
	// is another, through BeginScrub/ScrubTo/EndScrub below. same rules either
	// way: pause for the duration, and pick playback back up where the scrub
	// ends if it was running when the scrub began
	bool dragging = false;

	void Slider_DragStarted()
	{
		dragging = true;
		BeginScrub();
	}

	void Slider_ValueChanged(double value)
	{
		if (!dragging) return;

		TimeSpan position = playback.Timeline.Duration * value;

		// the slider is playback's own control, so a scrub from it is
		// playback moving - anything following along should hear about it.
		// a scrub from outside is not reported back: the caller already knows
		// where it asked for, and echoing the clamped value would fight it
		ReportPosition(position);
		ScrubTo(position);
	}

	void Slider_DragEnded(bool valueChanged)
	{
		dragging = false;
		EndScrub();
	}

	bool scrubbing = false;
	bool restartOnScrubEnd = false;

	public void BeginScrub()
	{
		if (playback is null || scrubbing) return;

		scrubbing = true;

		InitializeFramebuffer((int)playback.RenderSettings.Resolution.X, (int)playback.RenderSettings.Resolution.Y);

		if (playback.State == PlaybackState.Playing)
		{
			restartOnScrubEnd = true;
			Debug.WriteLine("scrub started mid-play, playback will be resumed on scrub end");

			playback.Pause();
			SetPlayButtonText("Play");
		}
	}

	// show the frame at position. clamped to the timeline, since playback
	// refuses anything outside it and a playhead can be dragged past the end
	public void ScrubTo(TimeSpan position)
	{
		if (playback is null) return;

		TimeSpan duration = playback.Timeline.Duration;

		if (position < TimeSpan.Zero) position = TimeSpan.Zero;
		if (position > duration) position = duration;

		// so the next report after this scrub is the real change, not a repeat
		reportedPosition = position;

		// keep the slider in step unless the slider is what is doing the scrubbing
		if (!dragging) SetSliderValue(duration > TimeSpan.Zero ? position / duration : 0d);

		try
		{
			_ = playback.ScrubToAsync(position);
		}
		catch (Exception e)
		{
			Debug.WriteLine(e);
		}
	}

	public void EndScrub()
	{
		if (!scrubbing) return;

		scrubbing = false;

		if (restartOnScrubEnd)
		{
			Debug.WriteLine("attempting to restart playback after scrub");
			restartOnScrubEnd = false;
			playback.Play();
			SetPlayButtonText("Pause");
		}
	}

	void InitializeFramebuffer(int width, int height)
	{
		if (_frame is null || _frame.GetWidth() != width || _frame.GetHeight() != height)
		{
			_frame = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
			videoTexture.CallDeferred("set_image", _frame);
		}
	}

	Image _frame = null;
	void OnVideoFrame(object sender, VideoFrameEventArgs e)
    {
		//if frame is the wrong size, skip drawing
		if (e.Length == (int)playback.RenderSettings.Resolution.X * (int)playback.RenderSettings.Resolution.Y * 4)
		{
			_frame.SetData(e.Width, e.Height, false, Image.Format.Rgba8, e.Buffer[..e.Length]);

			DrawImage(_frame);
		}
		
		SetTimestamp(e.Position, playback.Timeline.Duration, playback.RenderSettings.Framerate);
    }

	AudioStreamGeneratorPlayback _generatorPlayback;
    void OnAudioSample(object sender, AudioSampleEventArgs e)
    {
        if (audioStreamPlayer.Stream is AudioStreamGenerator generator)
		{
			generator.MixRate = e.SampleRate;
			_generatorPlayback.PushBuffer(ToVector2Buffer(e.Buffer, e.Length, e.ChannelCount));
		}
    }

	void OnEndReached(object sender, EventArgs e)
	{
		Debug.WriteLine("end reached");
		endReached = true;
		SetPlayButtonText("Play");
		SetTimestamp(playback.Timeline.Duration, playback.Timeline.Duration, playback.RenderSettings.Framerate);
	}

	

	void DrawImage(Image image)
	{
		try
		{
			videoTexture.CallDeferred("update", image);
		}
		catch
		{
			InitializeFramebuffer(image.GetWidth(), image.GetHeight());
			videoTexture.CallDeferred("update", image);
		}
		
	}

	void SetPlayButtonText(string t)
	{
		playButton.SetDeferred("text", t);
	}

	void SetSliderValue(double v)
	{
		slider.SetDeferred("value", v);
	}

	void SetTimestamp(TimeSpan position, TimeSpan duration, int framerate)
	{
		timestamp.SetDeferred("text", CalculateTimestamp(position, duration, framerate));
		if (playback.State == PlaybackState.Playing) SetSliderValue(position / duration);
	}

	static string CalculateTimestamp(TimeSpan position, TimeSpan duration, int framerate)
	{
		string positionString = position.ToString(@"hh\:mm\:ss");
		string positionFrames = ((int)(position.TotalSeconds % 1d * framerate)).ToString();
		positionFrames = positionFrames.Length == 1 ? string.Concat("0", positionFrames) : positionFrames;

		string durationString = duration.ToString(@"hh\:mm\:ss");
		string durationFrames = ((int)(duration.TotalSeconds % 1d * framerate)).ToString();
		durationFrames = durationFrames.Length == 1 ? string.Concat("0", durationFrames) : durationFrames;

		return $"{positionString}.{positionFrames} / {durationString}.{durationFrames}";
	}

	public Vector2[] samples;
	/// <summary>
    /// Converts a raw s16le PCM chunk from AudioSampleEventArgs into a
    /// Vector2[] suitable for AudioStreamGeneratorPlayback.PushBuffer.
    /// Mono input is duplicated to both channels; anything beyond 2
    /// channels only uses the first two.
    /// </summary>
	public Vector2[] ToVector2Buffer(byte[] buffer, int length, int channelCount)
    {
        const float invShort = 1.0f / 32768.0f;

        int bytesPerFrame = 2 * channelCount; // 2 bytes per s16 sample
        int frameCount = length / bytesPerFrame;

		//create a new array for samples if one is not already present
		if (samples is null || samples.Length != frameCount) samples = new Vector2[frameCount];

        int offset = 0;
        for (int i = 0; i < frameCount; i++)
        {
            short leftRaw = (short)(buffer[offset] | (buffer[offset + 1] << 8));
            float left = leftRaw * invShort;

            float right;
            if (channelCount >= 2)
            {
                short rightRaw = (short)(buffer[offset + 2] | (buffer[offset + 3] << 8));
                right = rightRaw * invShort;
            }
            else
            {
                right = left; // mono -> duplicate to both channels
            }

            samples[i] = new Vector2(left, right);
            offset += bytesPerFrame;
        }

        return samples;
    }

    
}

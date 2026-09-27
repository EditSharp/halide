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
		playButton.Pressed += PlayButton_Pressed;

		slider.DragStarted += Slider_DragStarted;
		slider.ValueChanged += Slider_ValueChanged;
		slider.DragEnded += Slider_DragEnded;
	}

	public void SetPlayback(Playback p)
	{
		// the one before is let go of: unhooked, stopped and disposed
		if (playback is not null)
		{
			playback.VideoFrame -= OnVideoFrame;
			playback.AudioSample -= OnAudioSample;
			playback.EndReached -= OnEndReached;
			if (playback.State != PlaybackState.Inactive) playback.Stop();
			playback.Dispose();
			SetPlayButtonText("Play");
		}

		playback = p;
		if (playback is null) return;

		playback.VideoFrame += OnVideoFrame;
		playback.AudioSample += OnAudioSample;
		playback.EndReached += OnEndReached;
	}

	public Playback Playback => playback;

	// the view going for good: the playback goes with it, and a frame still on its
	// way finds nothing listening. a move between panes or windows keeps it
	public override void _Notification(int what)
	{
		if (what == NotificationPredelete) SetPlayback(null);
	}

	// sound stops before the player leaves the tree; after a move _Process starts it again
	public override void _ExitTree()
	{
		_generatorPlayback = null;
		if (IsInstanceValid(audioStreamPlayer)) audioStreamPlayer.Stop();
	}

	// where playback is, as far as anything watching from outside should know -
	// a timeline playhead, say. raised on the main thread only: from _Process
	// while playing, and from a scrub as it is requested. never from the
	// decoder's own thread, so a listener can touch scene nodes directly
	public event EventHandler<Time> PositionChanged;

	public PlaybackState State => playback?.State ?? PlaybackState.Inactive;

	Time? reportedPosition;

	// set from the playback thread when the end is hit, picked up here so the
	// last report is the very end rather than the last frame delivered
	bool endReached;

	public override void _Process(double delta)
	{
		SyncAudio();
		if (playback is null) return;

		if (playback.State == PlaybackState.Playing)
		{
			// the clock extrapolates, so it can read a hair past the end
			Time position = playback.Position;
			Time duration = playback.Timeline.Duration;

			ReportPosition(position > duration ? duration : position);
		}
		else if (endReached)
		{
			endReached = false;
			ReportPosition(Speed < 0f ? Time.Zero : playback.Timeline.Duration);
		}
	}

	void ReportPosition(Time position)
	{
		if (reportedPosition == position) return;

		reportedPosition = position;
		PositionChanged?.Invoke(this, position);
	}

	// what the play button does, for whoever else wants to do it - a shortcut, say
	public void TogglePlayback() => PlayButton_Pressed();

	// playback goes round to the other end instead of stopping there
	public bool Loop { get; set; }

	// pause while playing at any speed; otherwise play forward at normal speed
	void PlayButton_Pressed()
	{
		if (playback is null) return;

		if (playback.State == PlaybackState.Playing) Pause();
		else PlayAt(1f);
	}

	float Speed => playback?.Speed ?? 1f;

	void Pause()
	{
		playback.Pause();
		SetPlayButtonText("Play");
		SetTimestamp(playback.Position, playback.Timeline.Duration, playback.RenderSettings.Framerate);
	}

	// play at a speed, carrying on in place when already playing, resuming when
	// paused, starting a session when there is none
	void PlayAt(float speed)
	{
		playback.Speed = speed;
		if (playback.State == PlaybackState.Playing) return;

		if (playback.State == PlaybackState.Inactive)
		{
			InitializeFramebuffer((int)playback.RenderSettings.Resolution.X, (int)playback.RenderSettings.Resolution.Y);

			// start from wherever the position was left - a scrub while stopped
			// moves it. at the far end there is nothing left to play, so go
			// round to the other end instead of playing one frame and stopping
			Time start = playback.Position;
			Time frame = Time.FrameLength(playback.RenderSettings.Framerate);
			Time duration = playback.Timeline.Duration;

			if (speed > 0f && start + frame >= duration) start = Time.Zero;
			if (speed < 0f && start - frame <= Time.Zero) start = duration;

			playback.Play(start);
		}
		else playback.Play();
		SyncAudio();

		SetPlayButtonText("Pause");
	}

	// ---- shuttle: J, K and L ----

	// J and L double from 1x up to this
	const float FastestSpeed = 128f;

	// K with J or L starts at half speed and halves down to this
	const float SlowestSpeed = 1f / 16f;

	// the speed playing that way, or 0 when paused or going the other way
	float SpeedToward(int direction)
		=> playback.State == PlaybackState.Playing && Math.Sign(playback.Speed) == direction ? Math.Abs(playback.Speed) : 0f;

	// L (direction 1) or J (-1): twice as fast when already going that way at
	// 1x or more, otherwise 1x that way
	public void Shuttle(int direction)
	{
		if (playback is null) return;

		float current = SpeedToward(direction);
		PlayAt(direction * (current >= 1f ? Math.Min(current * 2f, FastestSpeed) : 1f));
	}

	// K with L or J: half as fast when already creeping that way below 1x,
	// otherwise half speed that way
	public void SlowShuttle(int direction)
	{
		if (playback is null) return;

		float current = SpeedToward(direction);
		PlayAt(direction * (current > 0f && current < 1f ? Math.Max(current / 2f, SlowestSpeed) : 0.5f));
	}

	// K
	public void ShuttleStop()
	{
		if (playback is null) return;
		if (playback.State == PlaybackState.Playing) Pause();
	}

	// one frame either way from where playback is, pausing first
	public void StepFrame(int direction)
	{
		if (playback is null) return;

		if (playback.State == PlaybackState.Playing) Pause();

		Rational fps = playback.RenderSettings.Framerate;
		long frame = playback.Position.ToFrame(fps, Rounding.Nearest) + direction;
		Time target = Time.Clamp(Time.FromFrame(frame, fps), Time.Zero, playback.Timeline.Duration);

		InitializeFramebuffer((int)playback.RenderSettings.Resolution.X, (int)playback.RenderSettings.Resolution.Y);

		// the step is playback's own move, so whatever follows along hears about it
		ReportPosition(target);
		ScrubTo(target);
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

		Time position = playback.Timeline.Duration.Scale(value);

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
	public void ScrubTo(Time position)
	{
		if (playback is null) return;

		Time duration = playback.Timeline.Duration;

		if (position < Time.Zero) position = Time.Zero;
		if (position > duration) position = duration;

		// so the next report after this scrub is the real change, not a repeat
		reportedPosition = position;

		// keep the slider in step unless the slider is what is doing the scrubbing
		if (!dragging) SetSliderValue(duration > Time.Zero ? position / duration : 0d);

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
			SyncAudio();
			SetPlayButtonText("Pause");
		}
	}

	// the timeline changed under a still picture: show the frame at the
	// current position again. many calls in one frame render once, at its end;
	// playing or scrubbing already shows the latest
	bool refreshQueued;

	public void RefreshFrame()
	{
		if (playback is null || refreshQueued) return;

		refreshQueued = true;
		Callable.From(RefreshNow).CallDeferred();
	}

	void RefreshNow()
	{
		refreshQueued = false;
		if (playback is null || scrubbing || playback.State == PlaybackState.Playing) return;

		InitializeFramebuffer((int)playback.RenderSettings.Resolution.X, (int)playback.RenderSettings.Resolution.Y);

		Time position = playback.Position;
		Time duration = playback.Timeline.Duration;
		if (position > duration) position = duration;
		if (position < Time.Zero) position = Time.Zero;

		try
		{
			_ = playback.ScrubToAsync(position);
		}
		catch (Exception e)
		{
			Debug.WriteLine(e);
		}
	}

	void InitializeFramebuffer(int width, int height)
	{
		if (_frame is null || _frame.GetWidth() != width || _frame.GetHeight() != height)
		{
			_frame = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
			videoTexture.SetImage(_frame);
		}
	}

	Image _frame = null;

	// playback threads: the pixels are copied here and Godot objects are only touched on the main thread
	void OnVideoFrame(object sender, VideoFrameEventArgs e)
	{
		Playback source = playback;
		if (source is null) return;

		bool fits = e.Length == (int)source.RenderSettings.Resolution.X * (int)source.RenderSettings.Resolution.Y * 4;
		byte[] pixels = fits ? e.Buffer[..e.Length] : null;
		int width = e.Width, height = e.Height;
		Time position = e.Position;

		Callable.From(() =>
		{
			if (!IsInstanceValid(this) || playback != source) return;
			if (pixels is not null) ShowFrame(pixels, width, height);
			SetTimestamp(position, source.Timeline.Duration, source.RenderSettings.Framerate);
		}).CallDeferred();
	}

	// main thread
	void ShowFrame(byte[] pixels, int width, int height)
	{
		if (_frame is null || _frame.GetWidth() != width || _frame.GetHeight() != height)
		{
			_frame = Image.CreateFromData(width, height, false, Image.Format.Rgba8, pixels);
			videoTexture.SetImage(_frame);
			return;
		}

		_frame.SetData(width, height, false, Image.Format.Rgba8, pixels);
		videoTexture.Update(_frame);
	}

	AudioStreamGeneratorPlayback _generatorPlayback;
	int _mixRate;

	// the player runs only while playback does: a generator left playing as its window closes crashes the engine
	void SyncAudio()
	{
		bool playing = playback is not null && playback.State == PlaybackState.Playing;
		if (playing == audioStreamPlayer.Playing) return;

		if (playing)
		{
			audioStreamPlayer.Play();
			_generatorPlayback = (AudioStreamGeneratorPlayback)audioStreamPlayer.GetStreamPlayback();
		}
		else
		{
			_generatorPlayback = null;
			audioStreamPlayer.Stop();
		}
	}

	// playback threads: pushing to the generator is thread-safe, changing its rate is left to the main thread
	void OnAudioSample(object sender, AudioSampleEventArgs e)
	{
		AudioStreamGeneratorPlayback generator = _generatorPlayback;
		if (generator is null) return;

		if (e.SampleRate != _mixRate)
		{
			_mixRate = e.SampleRate;
			int rate = e.SampleRate;
			Callable.From(() => { if (IsInstanceValid(this) && audioStreamPlayer.Stream is AudioStreamGenerator stream) stream.MixRate = rate; }).CallDeferred();
		}

		generator.PushBuffer(ToVector2Buffer(e.Buffer, e.Length, e.ChannelCount));
	}

	void OnEndReached(object sender, EventArgs e)
	{
		Debug.WriteLine("end reached");
		endReached = true;
		SetPlayButtonText("Play");
		SetTimestamp(Speed < 0f ? Time.Zero : playback.Timeline.Duration, playback.Timeline.Duration, playback.RenderSettings.Framerate);

		// round again at the same speed; playing from the far end starts at the other
		if (Loop)
		{
			float speed = Speed;
			Callable.From(() => { if (Loop && playback is not null) PlayAt(speed); }).CallDeferred();
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

	void SetTimestamp(Time position, Time duration, Rational framerate)
	{
		// the shuttle speed, while playing at anything but normal speed
		float speed = Speed;
		string shuttle = playback.State == PlaybackState.Playing && speed != 1f ? $"  {Rational.Approximate(speed, 1024)}x" : "";

		timestamp.SetDeferred("text", CalculateTimestamp(position, duration, framerate) + shuttle);
		if (playback.State == PlaybackState.Playing) SetSliderValue(position / duration);
	}

	static string CalculateTimestamp(Time position, Time duration, Rational framerate)
		=> $"{Timecode(position, framerate)} / {Timecode(duration, framerate)}";

	// hh:mm:ss.ff, the frame counted within its second
	static string Timecode(Time t, Rational framerate)
	{
		long seconds = Math.Max(0, t.Ticks) / Time.TicksPerSecond;
		long frame = new Time(Math.Max(0, t.Ticks) % Time.TicksPerSecond).ToFrame(framerate);
		return $"{seconds / 3600:00}:{seconds / 60 % 60:00}:{seconds % 60:00}.{frame:00}";
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

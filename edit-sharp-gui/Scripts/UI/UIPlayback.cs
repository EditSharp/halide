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

		playback = new()
		{
			Timeline = ProjectManager.Singleton.currentProject.Timeline,
			RenderSettings = ProjectManager.Singleton.currentProject.RenderSettings with { Resolution = new(1280, 720), Framerate = 60 }
		};

		playButton.Pressed += PlayButton_Pressed;

		slider.DragStarted += Slider_DragStarted;
		slider.ValueChanged += Slider_ValueChanged;
		slider.DragEnded += Slider_DragEnded;

		playback.VideoFrame += OnVideoFrame;
		playback.AudioSample += OnAudioSample;
		playback.EndReached += OnEndReached;
	}

	void PlayButton_Pressed()
    {
        if (playback.IsPlaying)
		{
			if (playback.IsPaused)
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
		else
		{
			//setup playback
			_frame = Image.CreateEmpty((int)playback.RenderSettings.Resolution.X, (int)playback.RenderSettings.Resolution.Y, false, Image.Format.Rgba8);
			DrawImage(_frame);

			//start playback
			playback.Play(TimeSpan.Zero);
			SetPlayButtonText("Pause");
		}
    }

	void Slider_DragStarted()
	{
		playback.Pause();
		SetPlayButtonText("Play");
	}

	void Slider_ValueChanged(double value)
	{
		if (!playback.IsPaused && playback.IsPlaying) return;

		Debug.WriteLine($"scrubbing to {CalculateTimestamp(playback.Timeline.Duration * value, playback.Timeline.Duration, playback.RenderSettings.Framerate)}");
		playback.ScrubToAsync(playback.Timeline.Duration * value);
	}

	void Slider_DragEnded(bool valueChanged)
	{
		if (valueChanged) playback.ScrubToAsync(playback.Timeline.Duration * slider.Value);
	}

	Image _frame = Image.CreateEmpty(1920, 1080, false, Image.Format.Rgba8);
	void OnVideoFrame(object sender, VideoFrameEventArgs e)
    {
		//Debug.WriteLine($"video frame received: {e.Width}x{e.Height} {e.Width * e.Height * 4} bytes");
		_frame.SetData(e.Width, e.Height, false, Image.Format.Rgba8, e.Buffer[..e.Length]);

		DrawImage(_frame, true);
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
		SetPlayButtonText("Play");
		SetTimestamp(playback.Timeline.Duration, playback.Timeline.Duration, playback.RenderSettings.Framerate);
	}

	

	void DrawImage(Image image, bool sameResolution = false)
	{
		videoTexture.CallDeferred(sameResolution ? "update" : "set_image", image);
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
		SetSliderValue(position / duration);
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

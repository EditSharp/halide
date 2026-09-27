using EditSharp.Components;
using EditSharp.Playback;
using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

// Home tile pictures in <folder>/Thumbnails: poster.jpg and a ten-frame strip, 768x432 each
public static class ProjectThumbnails
{
	public const int Width = 768;
	public const int Height = 432;
	public const int FrameCount = 10;

	const string FolderName = "Thumbnails";
	const string PosterFile = "poster.jpg";
	const string FramesFile = "frames.jpg";

	// how long a frame waits for sources still opening before it's taken as it is
	static readonly TimeSpan Patience = TimeSpan.FromSeconds(4);

	// renders and writes the pictures in the background; a failure keeps the old ones
	public static async Task CaptureAsync(Project project, Timeline timeline, Time playhead, string projectFolder, CancellationToken ct = default)
	{
		string folder = Path.Combine(projectFolder, FolderName);

		try
		{
			using Playback playback = new()
			{
				Timeline = timeline,
				RenderSettings = project.RenderSettings with { Resolution = Fit(project.RenderSettings.Resolution) },
			};

			Image poster = await RenderAsync(playback, playhead, ct);

			Image strip = Image.CreateEmpty(Width * FrameCount, Height, false, Image.Format.Rgb8);
			Time duration = timeline.Duration;

			for (int i = 0; i < FrameCount; i++)
			{
				Time at = duration > Time.Zero ? duration.Scale((i + 0.5) / FrameCount) : Time.Zero;
				Image frame = await RenderAsync(playback, at, ct);
				strip.BlitRect(frame, new Rect2I(0, 0, Width, Height), new Vector2I(i * Width, 0));
			}

			Directory.CreateDirectory(folder);
			poster.SaveJpg(Path.Combine(folder, PosterFile), 0.85f);
			strip.SaveJpg(Path.Combine(folder, FramesFile), 0.85f);
		}
		catch (Exception e) when (e is not OperationCanceledException)
		{
			GD.PushWarning($"Could not capture the project's pictures: {e.Message}");
		}
	}

	// the frame at a time, fitted on black
	static async Task<Image> RenderAsync(Playback playback, Time at, CancellationToken ct)
	{
		DateTime until = DateTime.UtcNow + Patience;
		ClipFrame frame = await playback.RenderFrameAsync(at, ct);

		while (!frame.Complete && DateTime.UtcNow < until)
		{
			await Task.Delay(250, ct);
			frame = await playback.RenderFrameAsync(at, ct);
		}

		Image rendered = Image.CreateFromData(frame.Width, frame.Height, false, Image.Format.Rgba8, frame.Pixels);
		rendered.Convert(Image.Format.Rgb8);

		Image canvas = Image.CreateEmpty(Width, Height, false, Image.Format.Rgb8);
		canvas.Fill(Colors.Black);
		canvas.BlitRect(rendered, new Rect2I(0, 0, frame.Width, frame.Height), new Vector2I((Width - frame.Width) / 2, (Height - frame.Height) / 2));
		return canvas;
	}

	// the project's frame scaled to fit the picture, even-sized for the encoder
	static System.Numerics.Vector2 Fit(System.Numerics.Vector2 resolution)
	{
		float scale = MathF.Min(Width / MathF.Max(1f, resolution.X), Height / MathF.Max(1f, resolution.Y));
		return new(MathF.Max(2f, MathF.Round(resolution.X * scale / 2f) * 2f), MathF.Max(2f, MathF.Round(resolution.Y * scale / 2f) * 2f));
	}

	// the poster and the ten frames, or nulls and none when they haven't been captured
	public static (Texture2D Poster, Texture2D[] Frames) Load(string projectFolder)
	{
		string folder = Path.Combine(projectFolder, FolderName);
		Texture2D poster = LoadTexture(Path.Combine(folder, PosterFile));

		Texture2D[] frames = [];
		if (LoadTexture(Path.Combine(folder, FramesFile)) is { } strip && strip.GetWidth() >= FrameCount)
		{
			int width = strip.GetWidth() / FrameCount;
			List<Texture2D> slices = [];
			for (int i = 0; i < FrameCount; i++)
				slices.Add(new AtlasTexture { Atlas = strip, Region = new Rect2(i * width, 0, width, strip.GetHeight()) });
			frames = [.. slices];
		}

		return (poster, frames);
	}

	static Texture2D LoadTexture(string path)
	{
		if (!File.Exists(path)) return null;

		Image image = new();
		return image.Load(path) == Error.Ok ? ImageTexture.CreateFromImage(image) : null;
	}
}

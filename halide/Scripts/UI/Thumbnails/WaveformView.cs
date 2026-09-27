using EditSharp.Audio.Analysis;
using Halide.Scripts.UI.Theming;
using Godot;
using System;

namespace Halide.Scripts.UI.Thumbnails;

// a clip's spectral frames as the texture the waveform shader rasterizes:
// per frame eight RGBA texels of band energies and a ninth holding the
// peak, stacked down the texture, frames tiled in rows of RowWidth
public sealed class EnvelopeTexture
{
	public const int RowWidth = 4096;
	public const int TexelsPerFrame = 9;

	public ImageTexture Texture { get; }
	public int FrameCount { get; }
	public double FrameSeconds { get; }

	// the content time of frame 0, and the in-point it was measured against
	public Time ContentStart { get; }
	public Time Anchor { get; }

	public EnvelopeTexture(SpectralEnvelope envelope, Time anchor)
	{
		FrameCount = envelope.Count;
		FrameSeconds = envelope.FrameSeconds;
		ContentStart = envelope.ContentStart;
		Anchor = anchor;

		int width = Math.Max(1, Math.Min(FrameCount, RowWidth));
		int rows = Math.Max(1, (FrameCount + RowWidth - 1) / RowWidth);
		int height = rows * TexelsPerFrame;

		float[] texels = new float[width * height * 4];

		for (int f = 0; f < FrameCount; f++)
		{
			int row = f / RowWidth;
			int col = f - row * RowWidth;
			ReadOnlySpan<float> bands = envelope.BandsOf(f);

			for (int k = 0; k < 8; k++)
			{
				int at = ((row * TexelsPerFrame + k) * width + col) * 4;
				bands.Slice(k * 4, 4).CopyTo(texels.AsSpan(at, 4));
			}

			texels[((row * TexelsPerFrame + 8) * width + col) * 4] = envelope.Peak[f];
		}

		byte[] data = new byte[texels.Length * 4];
		Buffer.BlockCopy(texels, 0, data, 0, data.Length);

		Image image = Image.CreateFromData(width, height, false, Image.Format.Rgbaf, data);
		Texture = ImageTexture.CreateFromImage(image);
	}

	// a media's whole analysis, unprocessed
	public static EnvelopeTexture Of(AudioAnalysis analysis)
		=> new(new SpectralEnvelope(Time.Zero, AudioAnalysis.FrameSeconds, analysis.Bands, analysis.Peak), Time.Zero);
}

// a control the waveform shader paints: hand it an envelope, tell it which
// content time its left edge stands for and how many content seconds a
// pixel spans, and it draws. it takes its size from its parent and rounds
// its corners to the panel it sits in
public partial class WaveformView : Control
{
	static Shader shader;

	ShaderMaterial material;
	EnvelopeTexture envelope;

	// what the left edge and a pixel stand for, in content seconds
	double contentAtLeft;
	double pixelSeconds = 0.01;

	Color peakColor = Colors.White;
	Color rmsColor = Colors.White;

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;

		shader ??= GD.Load<Shader>("res://Shaders/Waveform.gdshader");
		material = new ShaderMaterial { Shader = shader };
		Material = material;

		Apply();
		UpdateMask();
	}

	public void SetEnvelope(EnvelopeTexture texture)
	{
		envelope = texture;
		Apply();
	}

	public void SetWindow(double contentAtLeft, double pixelSeconds)
	{
		if (this.contentAtLeft == contentAtLeft && this.pixelSeconds == pixelSeconds) return;

		this.contentAtLeft = contentAtLeft;
		this.pixelSeconds = pixelSeconds;
		Apply();
	}

	public void SetColors(Color peak, Color rms)
	{
		peakColor = peak;
		rmsColor = rms;
		Apply();
	}

	// the envelope's frame 0 in the content time of a clip whose in-point
	// is `anchor` now: the envelope was measured against its own
	double EffectiveStart(Time anchor) => envelope is null ? 0d : (envelope.ContentStart + (envelope.Anchor - anchor)).Seconds;

	Time anchor;

	public void SetAnchor(Time anchor)
	{
		if (this.anchor == anchor) return;
		this.anchor = anchor;
		Apply();
	}

	void Apply()
	{
		if (material is null) return;

		material.SetShaderParameter("spectrum", envelope?.Texture);
		material.SetShaderParameter("frame_count", envelope?.FrameCount ?? 0);
		material.SetShaderParameter("row_width", EnvelopeTexture.RowWidth);
		material.SetShaderParameter("frame_seconds", (float)(envelope?.FrameSeconds ?? AudioAnalysis.FrameSeconds));
		material.SetShaderParameter("content_start", (float)EffectiveStart(anchor));
		material.SetShaderParameter("content_at_left", (float)contentAtLeft);
		material.SetShaderParameter("pixel_seconds", (float)pixelSeconds);
		material.SetShaderParameter("peak_color", peakColor);
		material.SetShaderParameter("rms_color", rmsColor);
		Visible = envelope is not null;
		QueueRedraw();
	}

	// the shader needs something drawn to paint over: the whole rect
	public override void _Draw()
	{
		if (envelope is null) return;
		DrawRect(new Rect2(Vector2.Zero, Size), Colors.White);
	}

	// the corners follow the panel this sits in
	void UpdateMask()
	{
		if (material is null) return;

		float radius = 0f;

		if (GetParent() is Control panel)
		{
			radius = panel.GetThemeStylebox("panel") switch
			{
				ThemedStyleBox themed => themed.MinCornerRadius,
				StyleBoxFlat box => Mathf.Min(Mathf.Min(box.CornerRadiusTopLeft, box.CornerRadiusTopRight), Mathf.Min(box.CornerRadiusBottomLeft, box.CornerRadiusBottomRight)),
				_ => 0
			};
		}

		material.SetShaderParameter("size", Size);
		material.SetShaderParameter("radius", radius);
		QueueRedraw();
	}

	public override void _Notification(int what)
	{
		if (what == NotificationResized || what == NotificationThemeChanged) UpdateMask();
	}
}

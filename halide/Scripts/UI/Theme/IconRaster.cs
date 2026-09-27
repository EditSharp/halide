using Godot;
using System;
using System.Collections.Generic;

namespace Halide.Scripts.UI.Theming;

// small white icons rasterised into textures, for a glyph or a knob that
// was given no image: a diamond, arrows, a reset arc, chain rings, a ring
// and a pointer. white, so the theme tints them like any icon. each is
// made once per size and shared
public static class IconRaster
{
	public enum Shape { Diamond, DiamondOutline, ArrowLeft, ArrowRight, ArrowDown, Reset, ResetTrack, LinkOff, LinkOn, Ring, Pointer, Dot, Check, Bullet, Chevron, Film, Picture, Speaker, Timeline, Plus, WindowMinimize, WindowMaximize, WindowRestore, WindowClose }

	static readonly Dictionary<(Shape, int), ImageTexture> cache = [];

	public static ImageTexture Get(Shape shape, int size = 18)
	{
		if (cache.TryGetValue((shape, size), out ImageTexture made)) return made;

		made = Render(shape, size);
		cache[(shape, size)] = made;
		return made;
	}

	// coverage by supersampling: each pixel takes four by four samples of a
	// signed distance test, so edges come out smooth at any size
	static ImageTexture Render(Shape shape, int size)
	{
		Image image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
		const int samples = 4;
		float centre = size / 2f;

		for (int y = 0; y < size; y++)
		{
			for (int x = 0; x < size; x++)
			{
				int inside = 0;

				for (int sy = 0; sy < samples; sy++)
				{
					for (int sx = 0; sx < samples; sx++)
					{
						float px = (x + (sx + 0.5f) / samples - centre) / centre;
						float py = (y + (sy + 0.5f) / samples - centre) / centre;
						if (Inside(shape, px, py)) inside++;
					}
				}

				float coverage = inside / (float)(samples * samples);
				if (coverage > 0f) image.SetPixel(x, y, new Color(1f, 1f, 1f, coverage));
			}
		}

		return ImageTexture.CreateFromImage(image);
	}

	// x and y run from -1 to 1 across the icon
	static bool Inside(Shape shape, float x, float y)
	{
		switch (shape)
		{
			case Shape.Diamond:
				return Math.Abs(x) + Math.Abs(y) <= 0.6f;

			case Shape.DiamondOutline:
			{
				float d = Math.Abs(x) + Math.Abs(y);
				return d <= 0.6f && d >= 0.36f;
			}

			case Shape.ArrowLeft:
				return x >= -0.45f && x <= 0.35f && Math.Abs(y) <= (x + 0.45f) * 0.6f;

			case Shape.ArrowRight:
				return x <= 0.45f && x >= -0.35f && Math.Abs(y) <= (0.45f - x) * 0.6f;

			case Shape.ArrowDown:
				return y <= 0.4f && y >= -0.3f && Math.Abs(x) <= (0.4f - y) * 0.6f;

			// an arc from the top round to the left, with a head at the top
			case Shape.Reset:
			{
				float r = MathF.Sqrt(x * x + y * y);
				float angle = MathF.Atan2(y, x);
				bool arc = r >= 0.42f && r <= 0.66f && !(angle > -1.35f && angle < -0.25f);
				bool head = x >= 0.02f && x <= 0.62f && y >= -0.75f && y <= -0.2f && Math.Abs(y + 0.48f) <= (0.62f - x) * 0.55f;
				return arc || head;
			}

			// a hollow diamond struck through
			case Shape.ResetTrack:
			{
				float d = Math.Abs(x) + Math.Abs(y);
				bool outline = d <= 0.62f && d >= 0.4f;
				bool strike = Math.Abs(x + y) <= 0.11f && Math.Abs(x) <= 0.62f;
				return outline || strike;
			}

			case Shape.LinkOff:
			case Shape.LinkOn:
			{
				float gap = shape == Shape.LinkOn ? 0.22f : 0.4f;
				return Ring(x + gap, y, 0.34f, 0.14f) || Ring(x - gap, y, 0.34f, 0.14f);
			}

			case Shape.Ring:
				return Ring(x, y, 0.92f, 0.1f);

			case Shape.Pointer:
				return x >= 0.05f && x <= 0.9f && Math.Abs(y) <= 0.09f;

			case Shape.Dot:
				return x * x + y * y <= 0.02f;

			// menu marks: a check, a radio bullet and a submenu chevron
			case Shape.Check:
				return Segment(x, y, -0.55f, 0.05f, -0.17f, 0.43f, 0.11f) || Segment(x, y, -0.17f, 0.43f, 0.58f, -0.42f, 0.11f);

			case Shape.Bullet:
				return x * x + y * y <= 0.09f;

			case Shape.Chevron:
				return Segment(x, y, -0.18f, -0.48f, 0.26f, 0f, 0.085f) || Segment(x, y, 0.26f, 0f, -0.18f, 0.48f, 0.085f);

			// media kinds: a film frame, a picture, a speaker, timeline bars, a plus
			case Shape.Film:
			{
				bool frame = Math.Abs(x) <= 0.8f && Math.Abs(y) <= 0.6f;
				bool inner = Math.Abs(x) <= 0.5f && Math.Abs(y) <= 0.42f;
				bool hole = Math.Abs(x) >= 0.58f && Math.Abs(x) <= 0.72f && (Math.Abs(y) <= 0.1f || (Math.Abs(y) >= 0.3f && Math.Abs(y) <= 0.5f));
				return frame && !inner && !hole;
			}

			case Shape.Picture:
			{
				bool frame = Math.Abs(x) <= 0.8f && Math.Abs(y) <= 0.62f;
				bool inner = Math.Abs(x) <= 0.66f && Math.Abs(y) <= 0.48f;
				bool hill = y >= -0.1f && y <= 0.48f && Math.Abs(x) <= 0.66f && y >= Math.Abs(x + 0.15f) * 1.1f - 0.42f;
				bool sun = (x + 0.35f) * (x + 0.35f) + (y + 0.25f) * (y + 0.25f) <= 0.02f;
				return (frame && !inner) || hill || sun;
			}

			case Shape.Speaker:
			{
				bool body = x >= -0.75f && x <= -0.35f && Math.Abs(y) <= 0.25f;
				bool cone = x >= -0.35f && x <= 0.1f && Math.Abs(y) <= 0.25f + (x + 0.35f) * 1.1f;
				bool wave1 = Ring(x - 0.05f, y, 0.42f, 0.1f) && x > 0.2f;
				bool wave2 = Ring(x - 0.05f, y, 0.72f, 0.1f) && x > 0.25f;
				return body || cone || wave1 || wave2;
			}

			case Shape.Timeline:
			{
				bool top = y >= -0.62f && y <= -0.28f && x >= -0.8f && x <= 0.2f;
				bool middle = Math.Abs(y) <= 0.17f && x >= -0.4f && x <= 0.8f;
				bool bottom = y >= 0.28f && y <= 0.62f && x >= -0.8f && x <= 0.5f;
				return top || middle || bottom;
			}

			case Shape.Plus:
				return (Math.Abs(x) <= 0.12f && Math.Abs(y) <= 0.6f) || (Math.Abs(y) <= 0.12f && Math.Abs(x) <= 0.6f);

			// the caption buttons: thin strokes, like the OS's own
			case Shape.WindowMinimize:
				return Math.Abs(y) <= 0.06f && Math.Abs(x) <= 0.55f;

			case Shape.WindowMaximize:
				return Outline(x, y, -0.55f, -0.55f, 0.55f, 0.55f, 0.11f);

			case Shape.WindowRestore:
				return Outline(x, y, -0.55f, -0.33f, 0.33f, 0.55f, 0.11f)
					|| (Outline(x, y, -0.33f, -0.55f, 0.55f, 0.33f, 0.11f) && !(x < 0.33f && y > -0.33f));

			case Shape.WindowClose:
				return Segment(x, y, -0.55f, -0.55f, 0.55f, 0.55f, 0.07f) || Segment(x, y, -0.55f, 0.55f, 0.55f, -0.55f, 0.07f);

			default:
				return false;
		}
	}

	// on the border of the box from (l, t) to (r, b), `thickness` wide inward
	static bool Outline(float x, float y, float l, float t, float r, float b, float thickness)
	{
		bool within = x >= l && x <= r && y >= t && y <= b;
		bool inner = x >= l + thickness && x <= r - thickness && y >= t + thickness && y <= b - thickness;
		return within && !inner;
	}

	// within thickness of the segment from a to b, with round ends
	static bool Segment(float x, float y, float ax, float ay, float bx, float by, float thickness)
	{
		float dx = bx - ax, dy = by - ay;
		float t = Math.Clamp(((x - ax) * dx + (y - ay) * dy) / (dx * dx + dy * dy), 0f, 1f);
		float px = ax + t * dx - x, py = ay + t * dy - y;
		return px * px + py * py <= thickness * thickness;
	}

	static bool Ring(float x, float y, float radius, float thickness)
	{
		float r = MathF.Sqrt(x * x + y * y);
		return r <= radius && r >= radius - thickness;
	}
}

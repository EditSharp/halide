using EditSharpGUI.Scripts.Input;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class UIRuler : Control, IDragCancellable
{
	[ExportGroup("Mark Options")]

	[Export] float markWidth = 0.25f;
	[Export] int markMaxWidth = 10;
	[Export] VerticalAlignment markAlignment = VerticalAlignment.Bottom;
	[Export] float ratio = 0.5f;
	[Export] float cornerRounding = 0.5f;

	// the mark colours come from the theme, so a preset or accent change
	// redraws them - see EditSharpTheme's "Ruler" colours
	Color markColor => GetThemeColor("mark", "Ruler");
	Color submarkColor => GetThemeColor("submark", "Ruler");

	double lastPixelsPerSecond;
	int lastFramerate;

	public override void _Notification(int what)
	{
		if (what == NotificationThemeChanged && lastPixelsPerSecond > 0d) Update(lastPixelsPerSecond, lastFramerate);
	}

	[ExportGroup("Controls")]

	[Export] TextureRect marksRect;
	[Export] ImageTexture marksTexture;

	// pressing the ruler is how the playhead gets summoned, and holding it is
	// how the playhead gets dragged from there. the ruler only reports the
	// press and the release - the timeline owns the playhead and runs the drag
	public event EventHandler Pressed;
	public event EventHandler Released;

	public override void _GuiInput(InputEvent _)
	{
		MouseButtonState left = InputManager.Singleton.Mouse.LeftButton;

		switch (left.Action)
		{
			case MouseAction.Press:
			case MouseAction.DoubleClick:
				if (!left.Capture(this)) break;
				Pressed?.Invoke(this, EventArgs.Empty);
				break;

			case MouseAction.Click:
			case MouseAction.DragEnd:
				if (!left.HasCapture(this)) break;
				Released?.Invoke(this, EventArgs.Empty);
				break;
		}
	}

	// the press will never get a release of its own, so let go now
	public void CancelDrag(MouseButtonState button) => Released?.Invoke(this, EventArgs.Empty);


	public void Update(double pixelsPerSecond, int framerate)
	{
		lastPixelsPerSecond = pixelsPerSecond;
		lastFramerate = framerate;

		if (UpdateMarks(new((float)pixelsPerSecond, marksRect.Size.Y), GetScaledMarksOptions(pixelsPerSecond, framerate)))
		{
			marksTexture.SetImage(marksImage);
		}
	}

	MarksOptions GetScaledMarksOptions(double pixelsPerSecond, int framerate)
	{
		// Normal scale
		// zoomed in enough to see individual frames
		if (pixelsPerSecond > framerate * 3f)
		{
			GD.Print("normal scale");
			return new()
			{
				MarkCount = framerate,
				MarkWidth = markWidth,
				MarkMaxWidth = markMaxWidth,
				MarkAlignment = markAlignment,
				MarkColor = markColor,
				SubmarkColor = submarkColor,
				Ratio = ratio,
				CornerRounding = cornerRounding
			};
		}
		// Small scale
		// zoomed out too far to see individual frames, but still pretty close up
		else if (pixelsPerSecond > framerate * 1.5f)
		{
			// find the smallest number that framerate is divisible by
			// if the user is a freak and has a framerate of 7 or something, just use 1
			int smallestDivisor = 1;
			for (int i = 2; i < 100; i++)
			{
				if (framerate % i == 0)
				{
					smallestDivisor = i;
					break;
				}
			}

			GD.Print($"small scale: {framerate / smallestDivisor}");

			return new()
			{
				MarkCount = framerate / smallestDivisor,
				MarkWidth = markWidth,
				MarkMaxWidth = markMaxWidth,
				MarkAlignment = markAlignment,
				MarkColor = markColor,
				SubmarkColor = submarkColor,
				Ratio = ratio,
				CornerRounding = cornerRounding
			};
		}
		// Very small scale
		// zoomed out to see well over a minute on screen
		else if (pixelsPerSecond > framerate / 2f)
		{
			// find all numbers the framerate is divisible by 
			// except for the framerate itself obviously
			List<int> divisors = [];
			for (int i = 2; i < framerate; i++)
			{
				if (framerate % i == 0)
				{
					divisors.Add(i);
				}
			}

			int divisor = framerate;
			if (divisors.Count > 2)
			{
				// set divisor to median
				divisor = divisors[(divisors.Count - 1) / 2];
			}
			else if (divisors.Count > 0)
			{
				divisor = divisors.Last();
			}

			GD.Print($"very small scale: {framerate / divisor}");

			return new()
			{
				MarkCount = framerate / divisor,
				MarkWidth = markWidth,
				MarkMaxWidth = markMaxWidth,
				MarkAlignment = markAlignment,
				MarkColor = markColor,
				SubmarkColor = submarkColor,
				Ratio = ratio,
				CornerRounding = cornerRounding
			};
		}
		// Miniscule scale
		// pixels per minute scale
		else if (pixelsPerSecond > framerate / 3f)
		{
			// find all numbers the framerate is divisible by 
			// except for the framerate itself obviously
			List<int> divisors = [];
			for (int i = 2; i < framerate; i++)
			{
				if (framerate % i == 0)
				{
					divisors.Add(i);
				}
			}

			int divisor = framerate;
			if (divisors.Count > 2)
			{
				// set divisor to second largest found
				divisor = divisors[^2];
			}
			else if (divisors.Count > 0)
			{
				divisor = divisors.Last();
			}

			GD.Print($"miniscule scale: {framerate / divisor}");

			return new()
			{
				MarkCount = framerate / divisor,
				MarkWidth = markWidth,
				MarkMaxWidth = markMaxWidth,
				MarkAlignment = markAlignment,
				MarkColor = markColor,
				SubmarkColor = submarkColor,
				Ratio = ratio,
				CornerRounding = cornerRounding
			};
		}
		// Micro scale
		// pixels per hour type scale
		else
		{
			GD.Print("micro scale");

			return new()
			{
				MarkCount = 1,
				MarkWidth = 1,
				MarkMaxWidth = markMaxWidth,
				MarkAlignment = markAlignment,
				MarkColor = submarkColor,
				CornerRounding = cornerRounding
			};
		}
	}

	Image marksImage = Image.CreateEmpty(512, 512, false, Image.Format.Rgba8);
	Vector2? lastUsedResolution = null;
	MarksOptions? lastUsedOptions = null;


	//returns true if marks had to be regenerated
	bool UpdateMarks(Vector2 resolution, MarksOptions options)
	{
		if (lastUsedResolution.HasValue && lastUsedOptions.HasValue)
		{
			if (resolution.IsEqualApprox(lastUsedResolution.Value) && options.Equals(lastUsedOptions.Value)) return false;
			else
			{
				marksImage.SetData(
					(int)resolution.X, 
					(int)resolution.Y, 
					false, 
					Image.Format.Rgba8, 
					CreateMarksImage(resolution, options)
				);

				return true;
			}
		}
		else
		{
			marksImage.SetData(
				(int)resolution.X, 
				(int)resolution.Y, 
				false, 
				Image.Format.Rgba8, 
				CreateMarksImage(resolution, options)
			);
			
			return true;
		}
	}

    static byte[] CreateMarksImage(Vector2 resolution, MarksOptions options)
	{
		int width = (int)resolution.X;
		int height = (int)resolution.Y;

		byte[] image = new byte[width * height * 4]; // RGBA8, all zero = fully transparent

		if (options.MarkCount <= 0)
			return image;

		float slotWidth = (float)width / options.MarkCount;
		float barWidth = Mathf.Min(slotWidth * Mathf.Clamp(options.MarkWidth, 0f, 1f), options.MarkMaxWidth);

		for (int i = 0; i < options.MarkCount; i++)
		{
			float slotStart = slotWidth * i;

			Color color;
			float markHeight;

			if (i == 0)
			{
				color = options.MarkColor;
				markHeight = height;
			}
			else
			{
				color = options.SubmarkColor;
				markHeight = height * options.Ratio;
			}

			float x = slotStart; // left-aligned within the slot
			float y = options.MarkAlignment switch
			{
				VerticalAlignment.Top => 0f,
				VerticalAlignment.Center => (height - markHeight) / 2f,
				VerticalAlignment.Bottom => height - markHeight,
				_ => height - markHeight,
			};

			float shortSide = Mathf.Min(barWidth, markHeight);
			float cornerRadius = (shortSide / 2f) * Mathf.Clamp(options.CornerRounding, 0f, 1f);

			DrawRoundedRect(image, width, height, x, y, barWidth, markHeight, cornerRadius, color);
		}

		return image;
	}

	static void DrawRoundedRect(
    byte[] image, int imgWidth, int imgHeight,
    float x, float y, float w, float h, float cornerRadius, Color color)
	{
		// Bounding box in pixel space, with a 1px margin for anti-aliasing.
		int minX = Mathf.Max(0, (int)Mathf.Floor(x - 1));
		int minY = Mathf.Max(0, (int)Mathf.Floor(y - 1));
		int maxX = Mathf.Min(imgWidth - 1, (int)Mathf.Ceil(x + w + 1));
		int maxY = Mathf.Min(imgHeight - 1, (int)Mathf.Ceil(y + h + 1));

		Vector2 halfSize = new Vector2(w / 2f, h / 2f);
		Vector2 center = new Vector2(x + halfSize.X, y + halfSize.Y);
		float r = Mathf.Min(cornerRadius, Mathf.Min(halfSize.X, halfSize.Y));

		byte colR = (byte)Mathf.Clamp(color.R * 255f, 0f, 255f);
		byte colG = (byte)Mathf.Clamp(color.G * 255f, 0f, 255f);
		byte colB = (byte)Mathf.Clamp(color.B * 255f, 0f, 255f);
		float colA = Mathf.Clamp(color.A, 0f, 1f);

		for (int py = minY; py <= maxY; py++)
		{
			for (int px = minX; px <= maxX; px++)
			{
				Vector2 p = new Vector2(px + 0.5f, py + 0.5f) - center;
				float dist = SdRoundedBox(p, halfSize, r);

				// Coverage: 1 inside, 0 outside, smooth ~1px transition at the edge.
				float coverage = Mathf.Clamp(0.5f - dist, 0f, 1f);
				if (coverage <= 0f)
					continue;

				float pixelAlpha = colA * coverage;
				int idx = (py * imgWidth + px) * 4;

				// Straight alpha-over blend (background assumed transparent/black).
				byte dstR = image[idx + 0];
				byte dstG = image[idx + 1];
				byte dstB = image[idx + 2];
				byte dstA = image[idx + 3];
				float dstAlphaF = dstA / 255f;

				float outA = pixelAlpha + dstAlphaF * (1f - pixelAlpha);
				if (outA <= 0f)
					continue;

				float outR = (colR * pixelAlpha + dstR * dstAlphaF * (1f - pixelAlpha)) / outA;
				float outG = (colG * pixelAlpha + dstG * dstAlphaF * (1f - pixelAlpha)) / outA;
				float outB = (colB * pixelAlpha + dstB * dstAlphaF * (1f - pixelAlpha)) / outA;

				image[idx + 0] = (byte)Mathf.Clamp(outR, 0f, 255f);
				image[idx + 1] = (byte)Mathf.Clamp(outG, 0f, 255f);
				image[idx + 2] = (byte)Mathf.Clamp(outB, 0f, 255f);
				image[idx + 3] = (byte)Mathf.Clamp(outA * 255f, 0f, 255f);
			}
		}
	}

	// Signed distance to a rounded box, centered at origin.
	// p: point relative to box center. b: half-size of the box (excluding the corner radius). r: corner radius.
	static float SdRoundedBox(Vector2 p, Vector2 b, float r)
	{
		Vector2 q = new Vector2(Mathf.Abs(p.X), Mathf.Abs(p.Y)) - b + new Vector2(r, r);
		float outsideDist = new Vector2(Mathf.Max(q.X, 0f), Mathf.Max(q.Y, 0f)).Length();
		float insideDist = Mathf.Min(Mathf.Max(q.X, q.Y), 0f);
		return outsideDist + insideDist - r;
	}

	struct MarksOptions
	{
		// how many marks to draw
		public int MarkCount = 1;
		// how much of the divided up space for a mark should the mark actually draw to
		// 0f = no width, leaving a blank image, 1f = full width, leaving no gaps between marks
		// (marks align left)
		public float MarkWidth = 0.25f;
		// the maximum width of a mark in pixels
		public int MarkMaxWidth = 10;
		// how submarks should align themselves
		public VerticalAlignment MarkAlignment = VerticalAlignment.Bottom;
		// the color to use for the first mark
		public Color MarkColor = Colors.White;
		// the color to use for all subsequent marks
		public Color SubmarkColor = Colors.LightSlateGray;
		// how tall submarks should be in comparison to the first mark 
		// 0f = no height, 1f = as tall as the first mark
		public float Ratio = 0.5f;
		// how rounded the corners of the marks should be
		// 0f = no rounding, 1f = fully rounded
		public float CornerRounding = 0.5f;

        public MarksOptions()
        {
        }
    }
}

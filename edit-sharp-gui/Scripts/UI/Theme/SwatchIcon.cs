using System.Collections.Generic;
using Godot;

namespace EditSharpGUI.Scripts.UI.Theming;

// a filled rounded square in a colour, for a colour pick in a menu
public static class SwatchIcon
{
	static readonly Dictionary<(Color, int), ImageTexture> cache = [];

	public static ImageTexture Get(Color colour, int size = 16)
	{
		if (cache.TryGetValue((colour, size), out ImageTexture made)) return made;

		Image image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
		float radius = size * 0.2f;
		float inset = size * 0.1f;
		const int samples = 4;

		for (int y = 0; y < size; y++)
		{
			for (int x = 0; x < size; x++)
			{
				int inside = 0;

				for (int sy = 0; sy < samples; sy++)
				{
					for (int sx = 0; sx < samples; sx++)
					{
						float px = x + (sx + 0.5f) / samples;
						float py = y + (sy + 0.5f) / samples;
						if (Inside(px, py, inset, size - inset, radius)) inside++;
					}
				}

				float coverage = inside / (float)(samples * samples);
				image.SetPixel(x, y, new Color(colour.R, colour.G, colour.B, colour.A * coverage));
			}
		}

		made = ImageTexture.CreateFromImage(image);
		cache[(colour, size)] = made;
		return made;
	}

	static bool Inside(float x, float y, float min, float max, float radius)
	{
		if (x < min || x > max || y < min || y > max) return false;

		float cx = Mathf.Clamp(x, min + radius, max - radius);
		float cy = Mathf.Clamp(y, min + radius, max - radius);
		float dx = x - cx, dy = y - cy;

		return dx * dx + dy * dy <= radius * radius;
	}
}

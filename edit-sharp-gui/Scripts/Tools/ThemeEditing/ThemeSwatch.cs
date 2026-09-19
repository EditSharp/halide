using EditSharpGUI.Scripts.UI.Theming;
using Godot;

namespace EditSharpGUI.Scripts.Tools.ThemeEditing;

// one item of the theme drawn large with its name over it: a stylebox
// with sample text in its font colour, a bare colour, or a definition.
// redraws whenever the theme changes, so it follows every edit
public partial class ThemeSwatch : Control
{
	public EditSharpTheme Theme;
	public string ThemeType;
	public string Item;

	// a definition swatch shows the palette entry itself
	public ThemeDefinition? Definition;
	public bool IsColor;

	const float LabelHeight = 18f;

	public override void _Ready()
	{
		CustomMinimumSize = new(200f, 66f);
		MouseFilter = MouseFilterEnum.Ignore;
		if (Theme is not null) Theme.Changed += QueueRedraw;
	}

	public override void _ExitTree()
	{
		if (Theme is not null) Theme.Changed -= QueueRedraw;
	}

	public override void _Draw()
	{
		if (Theme is null) return;

		Font font = GetThemeDefaultFont();
		int fontSize = GetThemeDefaultFontSize();
		Color dim = Theme.Resolve(ThemeDefinition.FontColor2);

		string title = Definition is ThemeDefinition d ? d.ToString() : Item;
		DrawString(font, new Vector2(0f, font.GetAscent(fontSize)), title, HorizontalAlignment.Left, Size.X, fontSize, dim);

		Rect2 rect = new(0f, LabelHeight, Size.X, Size.Y - LabelHeight);

		if (Definition is ThemeDefinition definition)
		{
			DrawCheckered(rect);
			DrawRect(rect, Theme.Resolve(definition));
			DrawRect(rect, dim, false, 1f);
			return;
		}

		if (IsColor)
		{
			DrawCheckered(rect);
			DrawRect(rect, Theme.HasColor(Item, ThemeType) ? Theme.GetColor(Item, ThemeType) : Colors.Magenta);
			DrawRect(rect, dim, false, 1f);
			return;
		}

		StyleBox box = Theme.HasStylebox(Item, ThemeType) ? Theme.GetStylebox(Item, ThemeType) : null;

		if (box is null || box is StyleBoxEmpty)
		{
			DrawRect(rect, dim, false, 1f);
			DrawString(font, new Vector2(0f, rect.Position.Y + rect.Size.Y / 2f + font.GetAscent(fontSize) / 2f - 2f), "empty", HorizontalAlignment.Center, Size.X, fontSize, dim);
			return;
		}

		DrawCheckered(rect);
		DrawStyleBox(box, rect);

		// sample text in the font colour that goes with this item, if the
		// type has one - a button's hover colour for its hover box
		Font sampleFont = Theme.HasFont("font", ThemeType) ? Theme.GetFont("font", ThemeType) : font;
		int sampleSize = Theme.HasFontSize("font_size", ThemeType) ? Theme.GetFontSize("font_size", ThemeType) : fontSize;
		Color text = FontColorFor(Item);

		float baseline = rect.Position.Y + (rect.Size.Y + sampleFont.GetAscent(sampleSize) - sampleFont.GetDescent(sampleSize)) / 2f;
		DrawString(sampleFont, new Vector2(0f, baseline), "Sample", HorizontalAlignment.Center, Size.X, sampleSize, text);
	}

	Color FontColorFor(string item)
	{
		string[] candidates = item switch
		{
			"hover" => ["font_hover_color", "font_color"],
			"pressed" or "hover_pressed" => ["font_pressed_color", "font_color"],
			"disabled" => ["font_disabled_color", "font_color"],
			"focus" => ["font_focus_color", "font_color"],
			_ => ["font_color"]
		};

		foreach (string candidate in candidates)
		{
			if (Theme.HasColor(candidate, ThemeType)) return Theme.GetColor(candidate, ThemeType);
		}

		return Theme.Resolve(ThemeDefinition.FontColor1);
	}

	// so a transparent or translucent colour reads as such
	void DrawCheckered(Rect2 rect)
	{
		const float cell = 8f;
		Color a = new(0.35f, 0.35f, 0.35f);
		Color b = new(0.25f, 0.25f, 0.25f);

		for (float y = 0f; y < rect.Size.Y; y += cell)
		{
			for (float x = 0f; x < rect.Size.X; x += cell)
			{
				bool odd = ((int)(x / cell) + (int)(y / cell)) % 2 == 1;
				Vector2 size = new(Mathf.Min(cell, rect.Size.X - x), Mathf.Min(cell, rect.Size.Y - y));
				DrawRect(new Rect2(rect.Position + new Vector2(x, y), size), odd ? a : b);
			}
		}
	}
}

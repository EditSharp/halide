using Godot;

// a minimize, maximize or close button whose fill fades in and out like the OS's; the fill is a child drawn behind it
[GlobalClass]
public partial class CaptionButton : Button
{
	[Export] Panel fill;

	const double FadeIn = 0.083, FadeOut = 0.167;

	bool hovered, pressed, hot;
	Tween fade;

	public override void _Ready()
	{
		MouseEntered += () => { hovered = true; Fade(); };
		MouseExited += () => { hovered = false; Fade(); };
		ButtonDown += () => { pressed = true; Fade(); };
		ButtonUp += () => { pressed = false; Fade(); };
		fill.Modulate = Colors.Transparent;
	}

	// the OS says the pointer is on this button (maximize, whose events go to the OS for Snap Layouts)
	public void SetHot(bool on)
	{
		if (hot == on) return;
		hot = on;
		Fade();
	}

	public void SetHeld(bool on)
	{
		if (pressed == on) return;
		pressed = on;
		Fade();
	}

	void Fade()
	{
		bool lit = hovered || hot || pressed;
		float strength = !lit ? 0f : pressed ? GetThemeConstant("pressed_fill") / 100f : 1f;

		fade?.Kill();
		fade = CreateTween().SetParallel();
		fade.TweenProperty(fill, "modulate", new Color(1f, 1f, 1f, strength), lit ? FadeIn : FadeOut);

		// a glyph with a hot colour (close's white) fades to it with the fill
		if (HasThemeColor("glyph_hot"))
		{
			Color rest = ThemeDB.GetProjectTheme()?.GetColor("font_color", ThemeTypeVariation) ?? GetThemeColor("font_color");
			Color glyph = rest.Lerp(GetThemeColor("glyph_hot"), lit ? 1f : 0f);

			foreach (string item in new[] { "font_color", "font_hover_color", "font_pressed_color" })
			{
				// a tween needs a value to start from
				if (!HasThemeColorOverride(item)) AddThemeColorOverride(item, rest);
				fade.TweenProperty(this, $"theme_override_colors/{item}", glyph, lit ? FadeIn : FadeOut);
			}
		}
	}
}

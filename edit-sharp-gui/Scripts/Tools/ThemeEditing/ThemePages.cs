using EditSharp.Editing;
using EditSharpGUI.Scripts.UI.Theming;
using Godot;
using SkiaSharp;
using System;
using System.Linq;
using Vector2 = System.Numerics.Vector2;

namespace EditSharpGUI.Scripts.Tools.ThemeEditing;

// the pages the theme tool shows in its inspector: plain objects whose
// editable properties read and write the theme directly. every colour is
// a definition pick - which definition, which shade, how much alpha -
// never a colour; the one place a real colour is chosen is the
// definitions page. a page made with no theme behind it stands for the
// defaults, so the inspector's reset has something to go back to

// ---- the definitions: the palette, the preset, the shades, the sizes ----

public sealed class DefinitionsPage(EditSharpTheme theme)
{
	static readonly EditSharpTheme Defaults = new();

	readonly EditSharpTheme t = theme ?? Defaults;

	public DefinitionsPage() : this(null) { }

	[Editable("Preset", Order = 0)]
	public EditSharpTheme.ColorPreset Preset { get => t.Preset; set => t.Preset = value; }

	[Editable("Background 1", Order = 10, Group = "Base colors")] public SKColor BackgroundColor1 { get => To(t.BackgroundColor1); set => t.BackgroundColor1 = From(value); }
	[Editable("Background 2", Order = 11, Group = "Base colors")] public SKColor BackgroundColor2 { get => To(t.BackgroundColor2); set => t.BackgroundColor2 = From(value); }
	[Editable("Popup background", Order = 12, Group = "Base colors")] public SKColor PopupBackgroundColor { get => To(t.PopupBackgroundColor); set => t.PopupBackgroundColor = From(value); }
	[Editable("Accent", Order = 13, Group = "Base colors")] public SKColor AccentColor { get => To(t.AccentColor); set => t.AccentColor = From(value); }
	[Editable("Font 1", Order = 14, Group = "Base colors")] public SKColor FontColor1 { get => To(t.FontColor1); set => t.FontColor1 = From(value); }
	[Editable("Font 2", Order = 15, Group = "Base colors")] public SKColor FontColor2 { get => To(t.FontColor2); set => t.FontColor2 = From(value); }
	[Editable("Text field", Order = 16, Group = "Base colors")] public SKColor TextFieldColor { get => To(t.TextFieldColor); set => t.TextFieldColor = From(value); }
	[Editable("Button", Order = 17, Group = "Base colors")] public SKColor ButtonColor { get => To(t.ButtonColor); set => t.ButtonColor = From(value); }
	[Editable("Hyperlink", Order = 18, Group = "Base colors")] public SKColor HyperlinkColor { get => To(t.HyperlinkColor); set => t.HyperlinkColor = From(value); }
	[Editable("Stroke", Order = 19, Group = "Base colors")] public SKColor StrokeColor { get => To(t.StrokeColor); set => t.StrokeColor = From(value); }
	[Editable("White", Order = 20, Group = "Base colors")] public SKColor WhiteColor { get => To(t.WhiteColor); set => t.WhiteColor = From(value); }
	[Editable("Black", Order = 21, Group = "Base colors")] public SKColor BlackColor { get => To(t.BlackColor); set => t.BlackColor = From(value); }

	[Editable("Video clip", Order = 30, Group = "Clip colors")] public SKColor VideoClipColor { get => To(t.VideoClipColor); set => t.VideoClipColor = From(value); }
	[Editable("Audio clip", Order = 31, Group = "Clip colors")] public SKColor AudioClipColor { get => To(t.AudioClipColor); set => t.AudioClipColor = From(value); }
	[Editable("Text clip", Order = 32, Group = "Clip colors")] public SKColor TextClipColor { get => To(t.TextClipColor); set => t.TextClipColor = From(value); }
	[Editable("Generator video clip", Order = 33, Group = "Clip colors")] public SKColor GeneratorVideoClipColor { get => To(t.GeneratorVideoClipColor); set => t.GeneratorVideoClipColor = From(value); }
	[Editable("Generator audio clip", Order = 34, Group = "Clip colors")] public SKColor GeneratorAudioClipColor { get => To(t.GeneratorAudioClipColor); set => t.GeneratorAudioClipColor = From(value); }

	[Editable("Effect", Order = 40, Group = "Node colors")] public SKColor EffectNodeColor { get => To(t.EffectNodeColor); set => t.EffectNodeColor = From(value); }
	[Editable("Mask", Order = 41, Group = "Node colors")] public SKColor MaskNodeColor { get => To(t.MaskNodeColor); set => t.MaskNodeColor = From(value); }
	[Editable("Math", Order = 42, Group = "Node colors")] public SKColor MathNodeColor { get => To(t.MathNodeColor); set => t.MathNodeColor = From(value); }
	[Editable("Keying", Order = 43, Group = "Node colors")] public SKColor KeyingNodeColor { get => To(t.KeyingNodeColor); set => t.KeyingNodeColor = From(value); }
	[Editable("Source", Order = 44, Group = "Node colors")] public SKColor SourceNodeColor { get => To(t.SourceNodeColor); set => t.SourceNodeColor = From(value); }
	[Editable("Audio", Order = 45, Group = "Node colors")] public SKColor AudioNodeColor { get => To(t.AudioNodeColor); set => t.AudioNodeColor = From(value); }
	[Editable("Composite", Order = 46, Group = "Node colors")] public SKColor CompositeNodeColor { get => To(t.CompositeNodeColor); set => t.CompositeNodeColor = From(value); }

	[Editable("Hover shift", Order = 50, Group = "Shades", Min = 0, Max = 0.5, Step = 0.01)] public float HoverShift { get => t.HoverShift; set => t.HoverShift = value; }
	[Editable("Pressed shift", Order = 51, Group = "Shades", Min = 0, Max = 0.5, Step = 0.01)] public float PressedShift { get => t.PressedShift; set => t.PressedShift = value; }
	[Editable("Disabled alpha", Order = 52, Group = "Shades", Min = 0, Max = 1, Step = 0.01)] public float DisabledAlpha { get => t.DisabledAlpha; set => t.DisabledAlpha = value; }

	[Editable("Small", Order = 60, Group = "Font sizes", Min = 6, Max = 48, Step = 1)] public int SmallFontSize { get => t.SmallFontSize; set => t.SmallFontSize = value; }
	[Editable("Normal", Order = 61, Group = "Font sizes", Min = 6, Max = 48, Step = 1)] public int FontSize { get => t.FontSize; set => t.FontSize = value; }
	[Editable("Title", Order = 62, Group = "Font sizes", Min = 6, Max = 48, Step = 1)] public int TitleFontSize { get => t.TitleFontSize; set => t.TitleFontSize = value; }

	static SKColor To(Color c) => new((byte)Math.Round(c.R * 255f), (byte)Math.Round(c.G * 255f), (byte)Math.Round(c.B * 255f), (byte)Math.Round(c.A * 255f));
	static Color From(SKColor c) => new(c.Red / 255f, c.Green / 255f, c.Blue / 255f, c.Alpha / 255f);
}

// ---- one stylebox item of a type ----

public enum StyleKind { Empty, Plain, Themed }

public sealed class StyleBoxPage
{
	static readonly ThemedStyleBox Defaults = new();

	readonly EditSharpTheme theme;
	readonly string type;
	readonly string item;

	// the item was swapped for another kind of stylebox: the rows change
	public event Action StructureChanged;

	public StyleBoxPage() { }

	public StyleBoxPage(EditSharpTheme theme, string type, string item)
	{
		this.theme = theme;
		this.type = type;
		this.item = item;
	}

	StyleBox Current => theme is null ? Defaults : theme.HasStylebox(item, type) ? theme.GetStylebox(item, type) : null;
	ThemedStyleBox Themed => Current as ThemedStyleBox ?? Defaults;
	StyleBoxFlat Flat => Current as StyleBoxFlat ?? Defaults;

	[Editable("Kind", Order = 0)]
	public StyleKind Kind
	{
		get => Current switch { ThemedStyleBox => StyleKind.Themed, StyleBoxFlat => StyleKind.Plain, _ => StyleKind.Empty };
		set
		{
			if (theme is null || value == Kind) return;

			StyleBox replacement;

			switch (value)
			{
				case StyleKind.Empty:
					replacement = new StyleBoxEmpty();
					break;

				case StyleKind.Themed:
				{
					ThemedStyleBox box = EditSharpTheme.NewThemedStyleBox();
					if (Current is StyleBoxFlat old) { box.CopyShapeFrom(old); box.Background = ThemeDefinition.None; }
					else box.Background = ThemeDefinition.Transparent;
					replacement = box;
					break;
				}

				default:
				{
					StyleBoxFlat box = new();
					if (Current is StyleBoxFlat old) new ThemedStyleBox().CopyShapeFrom(old);
					if (Current is StyleBoxFlat source) { box.BgColor = source.BgColor; box.BorderColor = source.BorderColor; }
					replacement = box;
					break;
				}
			}

			theme.SetStylebox(item, type, replacement);
			theme.ApplyDefinitions();
			StructureChanged?.Invoke();
		}
	}

	// ---- picks ----

	[Editable("Background", Order = 1, Group = "Background")]
	[VisibleWhen(nameof(Kind), StyleKind.Themed)]
	public ThemeDefinition Background { get => Themed.Background; set { Themed.Background = value; Apply(); } }

	[Editable("Shade", Order = 2, Group = "Background")]
	[VisibleWhen(nameof(Kind), StyleKind.Themed)]
	public ThemeShade BackgroundShade { get => Themed.BackgroundShade; set { Themed.BackgroundShade = value; Apply(); } }

	[Editable("Alpha", Order = 3, Group = "Background", Min = 0, Max = 1, Step = 0.01)]
	[VisibleWhen(nameof(Kind), StyleKind.Themed)]
	public float BackgroundAlpha { get => Themed.BackgroundAlpha; set { Themed.BackgroundAlpha = value; Apply(); } }

	[Editable("Border", Order = 4, Group = "Border")]
	[VisibleWhen(nameof(Kind), StyleKind.Themed)]
	public ThemeDefinition Border { get => Themed.Border; set { Themed.Border = value; Apply(); } }

	[Editable("Shade", Order = 5, Group = "Border")]
	[VisibleWhen(nameof(Kind), StyleKind.Themed)]
	public ThemeShade BorderShade { get => Themed.BorderShade; set { Themed.BorderShade = value; Apply(); } }

	[Editable("Alpha", Order = 6, Group = "Border", Min = 0, Max = 1, Step = 0.01)]
	[VisibleWhen(nameof(Kind), StyleKind.Themed)]
	public float BorderAlpha { get => Themed.BorderAlpha; set { Themed.BorderAlpha = value; Apply(); } }

	// ---- shape: anything flat ----

	[Editable("Draw centre", Order = 10)]
	[VisibleWhen(nameof(Kind), StyleKind.Themed, StyleKind.Plain)]
	public bool DrawCenter { get => Flat.DrawCenter; set => Flat.DrawCenter = value; }

	[Editable("Blend border", Order = 11, Group = "Border")]
	[VisibleWhen(nameof(Kind), StyleKind.Themed, StyleKind.Plain)]
	public bool BorderBlend { get => Flat.BorderBlend; set => Flat.BorderBlend = value; }

	[Editable("Left", Order = 12, Group = "Border", Min = 0, Max = 32, Step = 1)]
	[VisibleWhen(nameof(Kind), StyleKind.Themed, StyleKind.Plain)]
	public int BorderLeft { get => Flat.BorderWidthLeft; set => Flat.BorderWidthLeft = value; }

	[Editable("Top", Order = 13, Group = "Border", Min = 0, Max = 32, Step = 1)]
	[VisibleWhen(nameof(Kind), StyleKind.Themed, StyleKind.Plain)]
	public int BorderTop { get => Flat.BorderWidthTop; set => Flat.BorderWidthTop = value; }

	[Editable("Right", Order = 14, Group = "Border", Min = 0, Max = 32, Step = 1)]
	[VisibleWhen(nameof(Kind), StyleKind.Themed, StyleKind.Plain)]
	public int BorderRight { get => Flat.BorderWidthRight; set => Flat.BorderWidthRight = value; }

	[Editable("Bottom", Order = 15, Group = "Border", Min = 0, Max = 32, Step = 1)]
	[VisibleWhen(nameof(Kind), StyleKind.Themed, StyleKind.Plain)]
	public int BorderBottom { get => Flat.BorderWidthBottom; set => Flat.BorderWidthBottom = value; }

	[Editable("Top left", Order = 20, Group = "Corner radius", Min = 0, Max = 64, Step = 1)]
	[VisibleWhen(nameof(Kind), StyleKind.Themed, StyleKind.Plain)]
	public int CornerTopLeft { get => Flat.CornerRadiusTopLeft; set => Flat.CornerRadiusTopLeft = value; }

	[Editable("Top right", Order = 21, Group = "Corner radius", Min = 0, Max = 64, Step = 1)]
	[VisibleWhen(nameof(Kind), StyleKind.Themed, StyleKind.Plain)]
	public int CornerTopRight { get => Flat.CornerRadiusTopRight; set => Flat.CornerRadiusTopRight = value; }

	[Editable("Bottom right", Order = 22, Group = "Corner radius", Min = 0, Max = 64, Step = 1)]
	[VisibleWhen(nameof(Kind), StyleKind.Themed, StyleKind.Plain)]
	public int CornerBottomRight { get => Flat.CornerRadiusBottomRight; set => Flat.CornerRadiusBottomRight = value; }

	[Editable("Bottom left", Order = 23, Group = "Corner radius", Min = 0, Max = 64, Step = 1)]
	[VisibleWhen(nameof(Kind), StyleKind.Themed, StyleKind.Plain)]
	public int CornerBottomLeft { get => Flat.CornerRadiusBottomLeft; set => Flat.CornerRadiusBottomLeft = value; }

	[Editable("Detail", Order = 24, Group = "Corner radius", Min = 1, Max = 20, Step = 1)]
	[VisibleWhen(nameof(Kind), StyleKind.Themed, StyleKind.Plain)]
	public int CornerDetail { get => Flat.CornerDetail; set => Flat.CornerDetail = value; }

	[Editable("Left", Order = 30, Group = "Content margins", Min = -1, Max = 64, Step = 1)]
	[VisibleWhen(nameof(Kind), StyleKind.Themed, StyleKind.Plain)]
	public float MarginLeft { get => Flat.ContentMarginLeft; set => Flat.ContentMarginLeft = value; }

	[Editable("Top", Order = 31, Group = "Content margins", Min = -1, Max = 64, Step = 1)]
	[VisibleWhen(nameof(Kind), StyleKind.Themed, StyleKind.Plain)]
	public float MarginTop { get => Flat.ContentMarginTop; set => Flat.ContentMarginTop = value; }

	[Editable("Right", Order = 32, Group = "Content margins", Min = -1, Max = 64, Step = 1)]
	[VisibleWhen(nameof(Kind), StyleKind.Themed, StyleKind.Plain)]
	public float MarginRight { get => Flat.ContentMarginRight; set => Flat.ContentMarginRight = value; }

	[Editable("Bottom", Order = 33, Group = "Content margins", Min = -1, Max = 64, Step = 1)]
	[VisibleWhen(nameof(Kind), StyleKind.Themed, StyleKind.Plain)]
	public float MarginBottom { get => Flat.ContentMarginBottom; set => Flat.ContentMarginBottom = value; }

	[Editable("Left", Order = 40, Group = "Expand margins", Min = 0, Max = 64, Step = 1)]
	[VisibleWhen(nameof(Kind), StyleKind.Themed, StyleKind.Plain)]
	public float ExpandLeft { get => Flat.ExpandMarginLeft; set => Flat.ExpandMarginLeft = value; }

	[Editable("Top", Order = 41, Group = "Expand margins", Min = 0, Max = 64, Step = 1)]
	[VisibleWhen(nameof(Kind), StyleKind.Themed, StyleKind.Plain)]
	public float ExpandTop { get => Flat.ExpandMarginTop; set => Flat.ExpandMarginTop = value; }

	[Editable("Right", Order = 42, Group = "Expand margins", Min = 0, Max = 64, Step = 1)]
	[VisibleWhen(nameof(Kind), StyleKind.Themed, StyleKind.Plain)]
	public float ExpandRight { get => Flat.ExpandMarginRight; set => Flat.ExpandMarginRight = value; }

	[Editable("Bottom", Order = 43, Group = "Expand margins", Min = 0, Max = 64, Step = 1)]
	[VisibleWhen(nameof(Kind), StyleKind.Themed, StyleKind.Plain)]
	public float ExpandBottom { get => Flat.ExpandMarginBottom; set => Flat.ExpandMarginBottom = value; }

	[Editable("Shadow", Order = 50, Group = "Shadow")]
	[VisibleWhen(nameof(Kind), StyleKind.Themed)]
	public ThemeDefinition Shadow { get => Themed.Shadow; set { Themed.Shadow = value; Apply(); } }

	[Editable("Shade", Order = 51, Group = "Shadow")]
	[VisibleWhen(nameof(Kind), StyleKind.Themed)]
	public ThemeShade ShadowShade { get => Themed.ShadowShade; set { Themed.ShadowShade = value; Apply(); } }

	[Editable("Alpha", Order = 52, Group = "Shadow", Min = 0, Max = 1, Step = 0.01)]
	[VisibleWhen(nameof(Kind), StyleKind.Themed)]
	public float ShadowAlpha { get => Themed.ShadowAlpha; set { Themed.ShadowAlpha = value; Apply(); } }

	[Editable("Size", Order = 53, Group = "Shadow", Min = 0, Max = 32, Step = 1)]
	[VisibleWhen(nameof(Kind), StyleKind.Themed, StyleKind.Plain)]
	public int ShadowSize { get => Flat.ShadowSize; set => Flat.ShadowSize = value; }

	[Editable("Offset", Order = 54, Group = "Shadow", Step = 1)]
	[VisibleWhen(nameof(Kind), StyleKind.Themed, StyleKind.Plain)]
	public Vector2 ShadowOffset { get => new(Flat.ShadowOffset.X, Flat.ShadowOffset.Y); set => Flat.ShadowOffset = new Godot.Vector2(value.X, value.Y); }

	[Editable("Skew", Order = 60, Step = 0.01)]
	[VisibleWhen(nameof(Kind), StyleKind.Themed, StyleKind.Plain)]
	public Vector2 Skew { get => new(Flat.Skew.X, Flat.Skew.Y); set => Flat.Skew = new Godot.Vector2(value.X, value.Y); }

	[Editable("Anti-aliasing", Order = 61, Group = "Anti-aliasing")]
	[VisibleWhen(nameof(Kind), StyleKind.Themed, StyleKind.Plain)]
	public bool AntiAliasing { get => Flat.AntiAliasing; set => Flat.AntiAliasing = value; }

	[Editable("Size", Order = 62, Group = "Anti-aliasing", Min = 0.01, Max = 4, Step = 0.01)]
	[VisibleWhen(nameof(Kind), StyleKind.Themed, StyleKind.Plain)]
	public float AntiAliasingSize { get => Flat.AntiAliasingSize; set => Flat.AntiAliasingSize = value; }

	void Apply() => theme?.ApplyDefinitions();
}

// ---- one bare colour item of a type: its binding ----

public sealed class ColorItemPage
{
	readonly EditSharpTheme theme;
	readonly string type;
	readonly string item;

	public ColorItemPage() { }

	public ColorItemPage(EditSharpTheme theme, string type, string item)
	{
		this.theme = theme;
		this.type = type;
		this.item = item;
	}

	ColorBinding Existing => theme?.ColorBindings.FirstOrDefault(b => b is not null && b.ThemeType == type && b.Item == item);

	ColorBinding Binding()
	{
		if (Existing is ColorBinding found) return found;

		ColorBinding made = EditSharpTheme.NewColorBinding();
		made.ThemeType = type;
		made.Item = item;
		made.Definition = ThemeDefinition.None;
		theme.ColorBindings.Add(made);
		return made;
	}

	[Editable("Definition", Order = 0)]
	public ThemeDefinition Definition { get => Existing?.Definition ?? ThemeDefinition.None; set { if (theme is null) return; Binding().Definition = value; theme.ApplyDefinitions(); } }

	[Editable("Shade", Order = 1)]
	public ThemeShade Shade { get => Existing?.Shade ?? ThemeShade.None; set { if (theme is null) return; Binding().Shade = value; theme.ApplyDefinitions(); } }

	[Editable("Alpha", Order = 2, Min = 0, Max = 1, Step = 0.01)]
	public float Alpha { get => Existing?.Alpha ?? 1f; set { if (theme is null) return; Binding().Alpha = value; theme.ApplyDefinitions(); } }
}

// ---- a type's font and size, by role ----

public sealed class FontPage
{
	readonly EditSharpTheme theme;
	readonly string type;

	public FontPage() { }

	public FontPage(EditSharpTheme theme, string type)
	{
		this.theme = theme;
		this.type = type;
	}

	FontBinding Existing => theme?.FontBindings.FirstOrDefault(b => b is not null && b.ThemeType == type);

	FontBinding Binding()
	{
		if (Existing is FontBinding found) return found;

		FontBinding made = EditSharpTheme.NewFontBinding();
		made.ThemeType = type;
		theme.FontBindings.Add(made);
		return made;
	}

	[Editable("Font", Order = 0)]
	public ThemeFontRole Font { get => Existing?.Font ?? ThemeFontRole.None; set { if (theme is null) return; Binding().Font = value; theme.ApplyDefinitions(); } }

	[Editable("Size", Order = 1)]
	public ThemeFontSizeRole Size { get => Existing?.Size ?? ThemeFontSizeRole.None; set { if (theme is null) return; Binding().Size = value; theme.ApplyDefinitions(); } }
}

// ---- one constant of a type ----

public sealed class ConstantPage
{
	readonly EditSharpTheme theme;
	readonly string type;
	readonly string item;

	public ConstantPage() { }

	public ConstantPage(EditSharpTheme theme, string type, string item)
	{
		this.theme = theme;
		this.type = type;
		this.item = item;
	}

	[Editable("Value", Order = 0, Min = -64, Max = 256, Step = 1)]
	public int Value { get => theme?.GetConstant(item, type) ?? 0; set => theme?.SetConstant(item, type, value); }
}

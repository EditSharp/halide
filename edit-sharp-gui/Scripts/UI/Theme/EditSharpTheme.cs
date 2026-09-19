using Godot;
using Godot.Collections;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace EditSharpGUI.Scripts.UI.Theming;

// the app's theme: a handful of colour definitions, and items that say
// which definition they take their colours from
//
// the definitions - background, accent, font colours, the clip and node
// colours - are the exported properties at the top. every stylebox in the
// theme is a ThemedStyleBox that names its background and border
// definitions and is otherwise an ordinary stylebox: its corners, borders
// and margins are edited in godot's theme editor and never touched here.
// plain colour items (font colours, the named Clip and Node colours) and
// fonts pick their definitions through the binding lists below, since a
// bare colour cannot carry a pick of its own
//
// changing a definition, a preset, or an item's pick rewrites colours and
// nothing else. FillMissingItems adds any item the theme lacks, with the
// app's default shape and picks, and leaves everything that exists alone.
// main_theme.tres holds the whole result, so it is the source of truth
// for how things look; this file only says what a missing item starts as
[Tool, GlobalClass]
public partial class EditSharpTheme : Theme
{
	public enum ColorPreset { Dark, Light, Custom }

	public EditSharpTheme()
	{
		// an item edited in the theme editor - a stylebox's pick, say - is a
		// change to this theme, and its colours follow
		Changed += OnChanged;
	}

	// ---- definitions: the base set MuseScore uses ----

	Color _backgroundColor1 = new(0.16f, 0.16f, 0.16f);
	Color _backgroundColor2 = new(0.104f, 0.104f, 0.104f);
	Color _popupBackgroundColor = new(0.13f, 0.13f, 0.13f);
	Color _accentColor = new(0.692f, 0.219f, 0.130f);
	Color _fontColor1 = new(0.92f, 0.92f, 0.92f);
	Color _fontColor2 = new(0.537f, 0.537f, 0.537f);
	Color _textFieldColor = new(0.12f, 0.12f, 0.12f);
	Color _buttonColor = new(0.22f, 0.22f, 0.22f);
	Color _hyperlinkColor = new(0.35f, 0.6f, 0.9f);
	Color _strokeColor = new(0.078f, 0.078f, 0.078f);
	Color _whiteColor = new(1f, 1f, 1f);
	Color _blackColor = new(0f, 0f, 0f);

	[ExportGroup("Colors")]
	[Export] public Color BackgroundColor1 { get => _backgroundColor1; set => Define(ref _backgroundColor1, value, true); }
	[Export] public Color BackgroundColor2 { get => _backgroundColor2; set => Define(ref _backgroundColor2, value, true); }
	[Export] public Color PopupBackgroundColor { get => _popupBackgroundColor; set => Define(ref _popupBackgroundColor, value, true); }
	[Export] public Color AccentColor { get => _accentColor; set => Define(ref _accentColor, value, false); }
	[Export] public Color FontColor1 { get => _fontColor1; set => Define(ref _fontColor1, value, true); }
	[Export] public Color FontColor2 { get => _fontColor2; set => Define(ref _fontColor2, value, true); }
	[Export] public Color TextFieldColor { get => _textFieldColor; set => Define(ref _textFieldColor, value, true); }
	[Export] public Color ButtonColor { get => _buttonColor; set => Define(ref _buttonColor, value, true); }
	[Export] public Color HyperlinkColor { get => _hyperlinkColor; set => Define(ref _hyperlinkColor, value, true); }
	[Export] public Color StrokeColor { get => _strokeColor; set => Define(ref _strokeColor, value, true); }
	[Export] public Color WhiteColor { get => _whiteColor; set => Define(ref _whiteColor, value, false); }
	[Export] public Color BlackColor { get => _blackColor; set => Define(ref _blackColor, value, false); }

	// ---- definitions: ours ----

	Color _videoClipColor = new(0.102f, 0.373f, 0.706f);
	Color _audioClipColor = new(0.267f, 0.561f, 0.392f);
	Color _textClipColor = new(0.55f, 0.35f, 0.7f);
	Color _generatorVideoClipColor = new(0.7f, 0.45f, 0.15f);
	Color _generatorAudioClipColor = new(0.2f, 0.55f, 0.55f);

	[ExportGroup("Clip Colors")]
	[Export] public Color VideoClipColor { get => _videoClipColor; set => Define(ref _videoClipColor, value, false); }
	[Export] public Color AudioClipColor { get => _audioClipColor; set => Define(ref _audioClipColor, value, false); }
	[Export] public Color TextClipColor { get => _textClipColor; set => Define(ref _textClipColor, value, false); }
	[Export] public Color GeneratorVideoClipColor { get => _generatorVideoClipColor; set => Define(ref _generatorVideoClipColor, value, false); }
	[Export] public Color GeneratorAudioClipColor { get => _generatorAudioClipColor; set => Define(ref _generatorAudioClipColor, value, false); }

	Color _effectNodeColor = new(0.35f, 0.5f, 0.8f);
	Color _maskNodeColor = new(0.6f, 0.6f, 0.3f);
	Color _mathNodeColor = new(0.5f, 0.5f, 0.5f);
	Color _keyingNodeColor = new(0.3f, 0.7f, 0.4f);
	Color _sourceNodeColor = new(0.75f, 0.4f, 0.3f);
	Color _audioNodeColor = new(0.3f, 0.6f, 0.45f);
	Color _compositeNodeColor = new(0.6f, 0.4f, 0.7f);

	[ExportGroup("Node Colors")]
	[Export] public Color EffectNodeColor { get => _effectNodeColor; set => Define(ref _effectNodeColor, value, false); }
	[Export] public Color MaskNodeColor { get => _maskNodeColor; set => Define(ref _maskNodeColor, value, false); }
	[Export] public Color MathNodeColor { get => _mathNodeColor; set => Define(ref _mathNodeColor, value, false); }
	[Export] public Color KeyingNodeColor { get => _keyingNodeColor; set => Define(ref _keyingNodeColor, value, false); }
	[Export] public Color SourceNodeColor { get => _sourceNodeColor; set => Define(ref _sourceNodeColor, value, false); }
	[Export] public Color AudioNodeColor { get => _audioNodeColor; set => Define(ref _audioNodeColor, value, false); }
	[Export] public Color CompositeNodeColor { get => _compositeNodeColor; set => Define(ref _compositeNodeColor, value, false); }

	// ---- shades: how a definition becomes its hover, pressed and disabled forms ----

	float _hoverShift = 0.08f;
	float _pressedShift = 0.12f;
	float _disabledAlpha = 0.5f;

	// nudged towards white on a dark theme and towards black on a light
	// one, so a hover always stands out from its background
	[ExportGroup("Shades")]
	[Export(PropertyHint.Range, "0,0.5,0.01")] public float HoverShift { get => _hoverShift; set => Define(ref _hoverShift, value, false); }
	[Export(PropertyHint.Range, "0,0.5,0.01")] public float PressedShift { get => _pressedShift; set => Define(ref _pressedShift, value, false); }
	[Export(PropertyHint.Range, "0,1,0.01")] public float DisabledAlpha { get => _disabledAlpha; set => Define(ref _disabledAlpha, value, false); }

	// ---- fonts ----

	Font _uiFont;
	Font _monoFont;
	int _smallFontSize = 12;
	int _fontSize = 14;
	int _titleFontSize = 16;

	[ExportGroup("Fonts")]
	[Export] public Font UIFont { get => _uiFont; set => Define(ref _uiFont, value, false); }
	[Export] public Font MonoFont { get => _monoFont; set => Define(ref _monoFont, value, false); }
	[Export] public int SmallFontSize { get => _smallFontSize; set => Define(ref _smallFontSize, value, false); }
	[Export] public int FontSize { get => _fontSize; set => Define(ref _fontSize, value, false); }
	[Export] public int TitleFontSize { get => _titleFontSize; set => Define(ref _titleFontSize, value, false); }

	// ---- preset ----

	ColorPreset _preset = ColorPreset.Dark;

	// a preset overwrites the base colours. the accent, the clip and node
	// colours are left alone - they read the same on either background.
	// editing a base colour afterwards makes it Custom
	[ExportGroup("Preset")]
	[Export] public ColorPreset Preset
	{
		get => _preset;
		set
		{
			if (value != ColorPreset.Custom) ApplyPreset(value);
			_preset = value;
		}
	}

	// ---- bindings: where bare colour items and fonts get their picks ----

	[ExportGroup("Bindings")]
	[Export] public Array<ColorBinding> ColorBindings { get; set; } = new();
	[Export] public Array<FontBinding> FontBindings { get; set; } = new();

	// ---- tools: checkboxes that act when ticked ----

	// adds every item the app expects and this theme lacks, with its
	// default shape and picks. nothing that exists is changed
	[ExportGroup("Tools")]
	[Export] public bool FillMissingItems { get => false; set { if (value) Regenerate(); } }

	// rewrites every bound colour and font from the definitions
	[Export] public bool ReapplyDefinitions { get => false; set { if (value) ApplyDefinitions(); } }

	// ---- resolving ----

	bool DarkUi => _backgroundColor1.Luminance < 0.5f;

	public Color Resolve(ThemeDefinition definition, ThemeShade shade = ThemeShade.None, float alpha = 1f)
	{
		Color c = Base(definition);

		c = shade switch
		{
			ThemeShade.Hover => DarkUi ? c.Lightened(_hoverShift) : c.Darkened(_hoverShift),
			ThemeShade.Pressed => DarkUi ? c.Lightened(_pressedShift) : c.Darkened(_pressedShift),
			ThemeShade.Disabled => new Color(c.R, c.G, c.B, c.A * _disabledAlpha),
			_ => c
		};

		return alpha >= 1f ? c : new Color(c.R, c.G, c.B, c.A * alpha);
	}

	Color Base(ThemeDefinition definition) => definition switch
	{
		ThemeDefinition.Transparent => Colors.Transparent,
		ThemeDefinition.BackgroundColor1 => _backgroundColor1,
		ThemeDefinition.BackgroundColor2 => _backgroundColor2,
		ThemeDefinition.PopupBackgroundColor => _popupBackgroundColor,
		ThemeDefinition.AccentColor => _accentColor,
		ThemeDefinition.FontColor1 => _fontColor1,
		ThemeDefinition.FontColor2 => _fontColor2,
		ThemeDefinition.TextFieldColor => _textFieldColor,
		ThemeDefinition.ButtonColor => _buttonColor,
		ThemeDefinition.HyperlinkColor => _hyperlinkColor,
		ThemeDefinition.StrokeColor => _strokeColor,
		ThemeDefinition.WhiteColor => _whiteColor,
		ThemeDefinition.BlackColor => _blackColor,
		ThemeDefinition.VideoClipColor => _videoClipColor,
		ThemeDefinition.AudioClipColor => _audioClipColor,
		ThemeDefinition.TextClipColor => _textClipColor,
		ThemeDefinition.GeneratorVideoClipColor => _generatorVideoClipColor,
		ThemeDefinition.GeneratorAudioClipColor => _generatorAudioClipColor,
		ThemeDefinition.EffectNodeColor => _effectNodeColor,
		ThemeDefinition.MaskNodeColor => _maskNodeColor,
		ThemeDefinition.MathNodeColor => _mathNodeColor,
		ThemeDefinition.KeyingNodeColor => _keyingNodeColor,
		ThemeDefinition.SourceNodeColor => _sourceNodeColor,
		ThemeDefinition.AudioNodeColor => _audioNodeColor,
		ThemeDefinition.CompositeNodeColor => _compositeNodeColor,
		_ => Colors.Magenta
	};

	public Font ResolveFont(ThemeFontRole role) => role switch
	{
		ThemeFontRole.UI => _uiFont,
		ThemeFontRole.Mono => _monoFont,
		_ => null
	};

	public int ResolveFontSize(ThemeFontSizeRole role) => role switch
	{
		ThemeFontSizeRole.Small => _smallFontSize,
		ThemeFontSizeRole.Title => _titleFontSize,
		_ => _fontSize
	};

	// ---- applying ----

	// true while a preset is being written, so the twelve assignments do
	// not each re-apply and do not each flip the preset to Custom
	bool applyingPreset;
	bool applying;
	bool applyQueued;

	void Define<T>(ref T field, T value, bool isBaseColor)
	{
		field = value;

		if (applyingPreset) return;
		if (isBaseColor) _preset = ColorPreset.Custom;

		ApplyDefinitions();
	}

	public void ApplyPreset(ColorPreset preset)
	{
		if (preset == ColorPreset.Custom) return;

		applyingPreset = true;

		if (preset == ColorPreset.Dark)
		{
			BackgroundColor1 = new(0.16f, 0.16f, 0.16f);
			BackgroundColor2 = new(0.104f, 0.104f, 0.104f);
			PopupBackgroundColor = new(0.13f, 0.13f, 0.13f);
			FontColor1 = new(0.92f, 0.92f, 0.92f);
			FontColor2 = new(0.537f, 0.537f, 0.537f);
			TextFieldColor = new(0.12f, 0.12f, 0.12f);
			ButtonColor = new(0.22f, 0.22f, 0.22f);
			HyperlinkColor = new(0.35f, 0.6f, 0.9f);
			StrokeColor = new(0.078f, 0.078f, 0.078f);
		}
		else
		{
			BackgroundColor1 = new(0.94f, 0.94f, 0.94f);
			BackgroundColor2 = new(0.88f, 0.88f, 0.88f);
			PopupBackgroundColor = new(0.97f, 0.97f, 0.97f);
			FontColor1 = new(0.1f, 0.1f, 0.1f);
			FontColor2 = new(0.4f, 0.4f, 0.4f);
			TextFieldColor = new(1f, 1f, 1f);
			ButtonColor = new(0.82f, 0.82f, 0.82f);
			HyperlinkColor = new(0.1f, 0.4f, 0.8f);
			StrokeColor = new(0.72f, 0.72f, 0.72f);
		}

		applyingPreset = false;
		_preset = preset;
		ApplyDefinitions();
	}

	// an item changed - from the theme editor, or from this very method.
	// re-apply once, next frame, so a burst of edits costs one pass and a
	// pass never re-enters itself
	void OnChanged()
	{
		if (applying || applyQueued) return;

		applyQueued = true;
		Callable.From(() => { applyQueued = false; ApplyDefinitions(); }).CallDeferred();
	}

	// writes every colour and font that comes from a definition: the
	// themed styleboxes' backgrounds and borders, the bound colour items,
	// the bound fonts. shapes, constants and anything unbound are untouched
	public void ApplyDefinitions()
	{
		if (applying) return;
		applying = true;

		try
		{
			if (_uiFont is not null && DefaultFont != _uiFont) DefaultFont = _uiFont;
			if (DefaultFontSize != _fontSize) DefaultFontSize = _fontSize;

			foreach (string type in GetTypeList())
			{
				foreach (string item in GetStyleboxList(type))
				{
					if (GetStylebox(item, type) is ThemedStyleBox themed) themed.Apply(this);
				}
			}

			foreach (ColorBinding binding in ColorBindings) binding?.Apply(this);
			foreach (FontBinding binding in FontBindings) binding?.Apply(this);
		}
		finally
		{
			applying = false;
		}
	}

	// fills in what is missing, then colours everything
	public void Regenerate()
	{
		EnsureDefaults();
		ApplyDefinitions();
	}

	// ---- the defaults: what a missing item starts as ----

	// only ever adds. an item that exists keeps its shape and its picks
	void Box(string item, string type, StyleBox box)
	{
		if (!HasStylebox(item, type)) SetStylebox(item, type, box);
	}

	void Colour(string type, string item, ThemeDefinition definition, ThemeShade shade = ThemeShade.None, float alpha = 1f)
	{
		if (ColorBindings.Any(b => b is not null && b.ThemeType == type && b.Item == item)) return;

		ColorBinding binding = FromScript<ColorBinding>(ref colorBindingScript, "res://Scripts/UI/Theme/ColorBinding.cs");
		binding.ThemeType = type;
		binding.Item = item;
		binding.Definition = definition;
		binding.Shade = shade;
		binding.Alpha = alpha;
		ColorBindings.Add(binding);
	}

	void Fonts(string type, ThemeFontRole font, ThemeFontSizeRole size)
	{
		if (FontBindings.Any(b => b is not null && b.ThemeType == type)) return;

		FontBinding binding = FromScript<FontBinding>(ref fontBindingScript, "res://Scripts/UI/Theme/FontBinding.cs");
		binding.ThemeType = type;
		binding.Font = font;
		binding.Size = size;
		FontBindings.Add(binding);
	}

	// ---- making scripted resources the saver can reference ----

	// an object made with `new` carries a script with no file behind it, and
	// saving the theme would write that script inline, where nothing can
	// load it back. made from the script resource at its path, the saved
	// item points at the file instead
	static CSharpScript themedStyleBoxScript, colorBindingScript, fontBindingScript;

	static T FromScript<T>(ref CSharpScript script, string path) where T : GodotObject, new()
	{
		script ??= GD.Load<CSharpScript>(path);

		if (script is null) return new T();

		return script.New().AsGodotObject() as T ?? new T();
	}

	public static ThemedStyleBox NewThemedStyleBox() => FromScript<ThemedStyleBox>(ref themedStyleBoxScript, "res://Scripts/UI/Theme/ThemedStyleBox.cs");
	public static ColorBinding NewColorBinding() => FromScript<ColorBinding>(ref colorBindingScript, "res://Scripts/UI/Theme/ColorBinding.cs");
	public static FontBinding NewFontBinding() => FromScript<FontBinding>(ref fontBindingScript, "res://Scripts/UI/Theme/FontBinding.cs");

	// ---- taking another theme's contents: reverting to the file ----

	// loads the file afresh and becomes it - items, definitions, bindings -
	// so every control already using this theme follows. false when the
	// file could not be read as one of these
	public bool ReloadFrom(string path)
	{
		if (ResourceLoader.Load(path, "", ResourceLoader.CacheMode.Ignore) is not EditSharpTheme fresh) return false;

		CopyFrom(fresh);
		return true;
	}

	public void CopyFrom(EditSharpTheme other)
	{
		applying = true;
		applyingPreset = true;

		try
		{
			Clear();
			MergeWith(other);

			BackgroundColor1 = other.BackgroundColor1;
			BackgroundColor2 = other.BackgroundColor2;
			PopupBackgroundColor = other.PopupBackgroundColor;
			AccentColor = other.AccentColor;
			FontColor1 = other.FontColor1;
			FontColor2 = other.FontColor2;
			TextFieldColor = other.TextFieldColor;
			ButtonColor = other.ButtonColor;
			HyperlinkColor = other.HyperlinkColor;
			StrokeColor = other.StrokeColor;
			WhiteColor = other.WhiteColor;
			BlackColor = other.BlackColor;
			VideoClipColor = other.VideoClipColor;
			AudioClipColor = other.AudioClipColor;
			TextClipColor = other.TextClipColor;
			GeneratorVideoClipColor = other.GeneratorVideoClipColor;
			GeneratorAudioClipColor = other.GeneratorAudioClipColor;
			EffectNodeColor = other.EffectNodeColor;
			MaskNodeColor = other.MaskNodeColor;
			MathNodeColor = other.MathNodeColor;
			KeyingNodeColor = other.KeyingNodeColor;
			SourceNodeColor = other.SourceNodeColor;
			AudioNodeColor = other.AudioNodeColor;
			CompositeNodeColor = other.CompositeNodeColor;
			HoverShift = other.HoverShift;
			PressedShift = other.PressedShift;
			DisabledAlpha = other.DisabledAlpha;
			UIFont = other.UIFont;
			MonoFont = other.MonoFont;
			SmallFontSize = other.SmallFontSize;
			FontSize = other.FontSize;
			TitleFontSize = other.TitleFontSize;
			_preset = other._preset;

			ColorBindings = new Array<ColorBinding>(other.ColorBindings);
			FontBindings = new Array<FontBinding>(other.FontBindings);
		}
		finally
		{
			applyingPreset = false;
			applying = false;
		}

		ApplyDefinitions();
	}

	void Constant(string item, string type, int value)
	{
		if (!HasConstant(item, type)) SetConstant(item, type, value);
	}

	void Variation(string name, string baseType)
	{
		if (GetTypeVariationBase(name) != baseType) SetTypeVariation(name, baseType);
	}

	static ThemedStyleBox Flat(
		ThemeDefinition background, ThemeShade shade = ThemeShade.None, float alpha = 1f,
		int radius = 0, ThemeDefinition border = ThemeDefinition.None, int borderWidth = 0, float margin = -1f,
		ThemeShade borderShade = ThemeShade.None, float borderAlpha = 1f)
	{
		ThemedStyleBox box = NewThemedStyleBox();
		box.Background = background;
		box.BackgroundShade = shade;
		box.BackgroundAlpha = alpha;
		box.Border = border;
		box.BorderShade = borderShade;
		box.BorderAlpha = borderAlpha;

		if (radius > 0) box.SetCornerRadiusAll(radius);

		if (border != ThemeDefinition.None && borderWidth > 0)
		{
			box.SetBorderWidthAll(borderWidth);
			box.BorderBlend = true;
		}

		if (margin >= 0f) box.SetContentMarginAll(margin);

		return box;
	}

	static ThemedStyleBox Outline(ThemeDefinition border, float alpha, int width, int radius)
	{
		ThemedStyleBox box = NewThemedStyleBox();
		box.DrawCenter = false;
		box.Background = ThemeDefinition.None;
		box.Border = border;
		box.BorderAlpha = alpha;
		box.BorderBlend = true;
		box.SetBorderWidthAll(width);
		if (radius > 0) box.SetCornerRadiusAll(radius);
		return box;
	}

	// a panel with only some sides stroked, the way a divider between two
	// areas is: bottom and right, say
	static ThemedStyleBox Divided(ThemeDefinition background, ThemeDefinition stroke, int top, int right, int bottom, int left, float margin = -1f, float strokeAlpha = 1f)
	{
		ThemedStyleBox box = NewThemedStyleBox();
		box.Background = background;
		box.Border = stroke;
		box.BorderAlpha = strokeAlpha;
		box.BorderBlend = true;
		box.BorderWidthTop = top;
		box.BorderWidthRight = right;
		box.BorderWidthBottom = bottom;
		box.BorderWidthLeft = left;

		if (margin >= 0f) box.SetContentMarginAll(margin);

		return box;
	}

	public void EnsureDefaults()
	{
		applying = true;

		try
		{
			const ThemeDefinition bg1 = ThemeDefinition.BackgroundColor1;
			const ThemeDefinition bg2 = ThemeDefinition.BackgroundColor2;
			const ThemeDefinition popup = ThemeDefinition.PopupBackgroundColor;
			const ThemeDefinition accent = ThemeDefinition.AccentColor;
			const ThemeDefinition font = ThemeDefinition.FontColor1;
			const ThemeDefinition dim = ThemeDefinition.FontColor2;
			const ThemeDefinition field = ThemeDefinition.TextFieldColor;
			const ThemeDefinition button = ThemeDefinition.ButtonColor;
			const ThemeDefinition stroke = ThemeDefinition.StrokeColor;
			const ThemeDefinition white = ThemeDefinition.WhiteColor;
			const ThemeDefinition black = ThemeDefinition.BlackColor;
			const ThemeShade hover = ThemeShade.Hover;
			const ThemeShade pressed = ThemeShade.Pressed;
			const ThemeShade disabled = ThemeShade.Disabled;

			// ---- panels ----
			Box("panel", "Panel", Flat(bg1));
			Box("panel", "PanelContainer", Flat(bg2));
			Box("panel", "ScrollContainer", new StyleBoxEmpty());
			Box("panel", "PopupPanel", Flat(popup, radius: 4, border: stroke, borderWidth: 1, margin: 6f));
			Box("panel", "TooltipPanel", Flat(popup, radius: 3, border: stroke, borderWidth: 1, margin: 4f));
			Colour("TooltipLabel", "font_color", font);

			// ---- labels ----
			Colour("Label", "font_color", font);
			Colour("Label", "font_shadow_color", ThemeDefinition.Transparent);
			Colour("Label", "font_outline_color", ThemeDefinition.Transparent);

			// ---- buttons, and everything that is a button ----
			foreach (string type in new[] { "Button", "OptionButton", "CheckBox", "CheckButton", "MenuButton", "ColorPickerButton", "LinkButton" })
			{
				bool boxless = type is "CheckBox" or "CheckButton";

				Box("normal", type, boxless ? new StyleBoxEmpty() : Flat(button, radius: 3, margin: 4f));
				Box("hover", type, boxless ? new StyleBoxEmpty() : Flat(button, hover, radius: 3, margin: 4f));
				Box("pressed", type, boxless ? new StyleBoxEmpty() : Flat(button, pressed, radius: 3, margin: 4f));
				Box("hover_pressed", type, boxless ? new StyleBoxEmpty() : Flat(button, pressed, radius: 3, margin: 4f));
				Box("disabled", type, boxless ? new StyleBoxEmpty() : Flat(button, disabled, radius: 3, margin: 4f));
				Box("focus", type, Outline(accent, 0.6f, 1, 3));

				ThemeDefinition text = type == "LinkButton" ? ThemeDefinition.HyperlinkColor : font;
				Colour(type, "font_color", text);
				Colour(type, "font_hover_color", text, type == "LinkButton" ? hover : ThemeShade.None);
				Colour(type, "font_pressed_color", text, type == "LinkButton" ? pressed : ThemeShade.None);
				Colour(type, "font_hover_pressed_color", text);
				Colour(type, "font_focus_color", text);
				Colour(type, "font_disabled_color", text, disabled);
				Colour(type, "icon_normal_color", font);
				Colour(type, "icon_hover_color", font);
				Colour(type, "icon_pressed_color", font);
				Colour(type, "icon_disabled_color", font, disabled);
			}

			// ---- text fields ----
			foreach (string type in new[] { "LineEdit", "TextEdit", "CodeEdit" })
			{
				Box("normal", type, Flat(field, radius: 3, border: stroke, borderWidth: 1, margin: 4f));
				Box("focus", type, Flat(field, radius: 3, border: accent, borderWidth: 1, margin: 4f));
				Box("read_only", type, Flat(field, disabled, radius: 3, border: stroke, borderWidth: 1, margin: 4f));

				Colour(type, "font_color", font);
				Colour(type, "font_selected_color", font);
				Colour(type, "font_uneditable_color", dim);
				Colour(type, "font_readonly_color", dim);
				Colour(type, "font_placeholder_color", dim);
				Colour(type, "caret_color", font);
				Colour(type, "selection_color", accent, alpha: 0.45f);
			}

			Colour("TextEdit", "background_color", field);
			Colour("LineEdit", "clear_button_color", dim);
			Colour("LineEdit", "clear_button_color_pressed", font);

			// ---- sliders and scroll bars ----
			// a slider's track sits on a toolbar, so it is a button's colour,
			// not a text field's - the latter vanishes into the toolbar
			foreach (string type in new[] { "HSlider", "VSlider" })
			{
				Box("slider", type, Flat(button, radius: 2, margin: 2f));
				Box("grabber_area", type, Flat(accent, radius: 2, margin: 2f));
				Box("grabber_area_highlight", type, Flat(accent, hover, radius: 2, margin: 2f));
			}

			// the bar is as wide as its track's margins make it
			foreach (string type in new[] { "HScrollBar", "VScrollBar" })
			{
				Box("scroll", type, Flat(black, alpha: 0.15f, radius: 3, margin: 4f));
				Box("scroll_focus", type, Flat(black, alpha: 0.15f, radius: 3, margin: 4f));
				Box("grabber", type, Flat(font, alpha: 0.25f, radius: 3, margin: 2f));
				Box("grabber_highlight", type, Flat(font, alpha: 0.4f, radius: 3, margin: 2f));
				Box("grabber_pressed", type, Flat(font, alpha: 0.55f, radius: 3, margin: 2f));
			}

			// ---- popups and menus ----
			Box("panel", "PopupMenu", Flat(popup, radius: 4, border: stroke, borderWidth: 1, margin: 4f));
			Box("hover", "PopupMenu", Flat(popup, hover, radius: 2));
			Box("separator", "PopupMenu", Flat(stroke));
			Colour("PopupMenu", "font_color", font);
			Colour("PopupMenu", "font_hover_color", font);
			Colour("PopupMenu", "font_disabled_color", font, disabled);
			Colour("PopupMenu", "font_accelerator_color", dim);
			Colour("PopupMenu", "font_separator_color", dim);

			// ---- split containers ----
			foreach (string type in new[] { "SplitContainer", "HSplitContainer", "VSplitContainer" })
				Box("split_bar_background", type, Flat(stroke));

			// ---- layout variations: box separations ----
			Variation("TightHBox", "HBoxContainer");
			Constant("separation", "TightHBox", 0);
			Variation("TightVBox", "VBoxContainer");
			Constant("separation", "TightVBox", 0);
			Variation("SpacedHBox", "HBoxContainer");
			Constant("separation", "SpacedHBox", 20);
			Variation("ClipHBox", "HBoxContainer");
			Constant("separation", "ClipHBox", 3);
			Variation("ClipVBox", "VBoxContainer");
			Constant("separation", "ClipVBox", 3);

			// ---- view chrome ----
			Variation("ViewBackground", "Panel");
			Box("panel", "ViewBackground", Flat(bg1));
			Variation("ToolbarBackground", "Panel");
			Box("panel", "ToolbarBackground", Flat(bg2));
			Variation("RulerBackground", "Panel");
			Box("panel", "RulerBackground", Flat(bg2));
			Variation("TimelineSpacer", "Panel");
			Box("panel", "TimelineSpacer", Divided(bg2, stroke, 0, 2, 2, 0));
			Variation("ChannelHeader", "PanelContainer");
			Box("panel", "ChannelHeader", Divided(bg2, stroke, 1, 2, 1, 0, 0f));
			Variation("ChannelName", "LineEdit");
			Fonts("ChannelName", ThemeFontRole.None, ThemeFontSizeRole.Title);

			// ---- playhead ----
			Variation("PlayheadHead", "Panel");
			Box("panel", "PlayheadHead", Divided(accent, black, 1, 1, 0, 1, strokeAlpha: 0.35f));
			Variation("PlayheadBody", "Panel");
			Box("panel", "PlayheadBody", Divided(accent, black, 0, 1, 1, 1, strokeAlpha: 0.35f));

			// ---- clips ----
			Variation("ClipShell", "PanelContainer");
			Box("panel", "ClipShell", new StyleBoxEmpty());
			Variation("ClipContent", "Panel");
			Box("panel", "ClipContent", Flat(ThemeDefinition.VideoClipColor, radius: 5));
			Variation("ClipThumbnail", "Panel");
			Box("panel", "ClipThumbnail", Flat(black, alpha: 0.35f, radius: 2, margin: 0f));
			Variation("ClipInnerShadow", "Panel");
			Box("panel", "ClipInnerShadow", Outline(black, 0.35f, 3, 5));
			Variation("ClipOutline", "Panel");
			Box("panel", "ClipOutline", Outline(accent, 1f, 3, 5));

			Variation("ClipName", "Label");
			Fonts("ClipName", ThemeFontRole.UI, ThemeFontSizeRole.Title);
			Colour("ClipName", "font_color", white);

			Variation("ClipSpeed", "Label");
			Fonts("ClipSpeed", ThemeFontRole.Mono, ThemeFontSizeRole.Small);
			Colour("ClipSpeed", "font_color", white);

			Variation("ClipButton", "Button");
			Box("normal", "ClipButton", Flat(black, alpha: 0.35f, radius: 2, margin: 0f));
			Box("hover", "ClipButton", Flat(black, alpha: 0.5f, radius: 2, margin: 0f));
			Box("pressed", "ClipButton", Flat(black, alpha: 0.6f, radius: 2, margin: 0f));
			Box("hover_pressed", "ClipButton", Flat(black, alpha: 0.6f, radius: 2, margin: 0f));
			Box("focus", "ClipButton", new StyleBoxEmpty());
			Fonts("ClipButton", ThemeFontRole.None, ThemeFontSizeRole.Small);

			// ---- playback ----
			Variation("Timestamp", "Label");
			Fonts("Timestamp", ThemeFontRole.Mono, ThemeFontSizeRole.None);

			// ---- inspector ----
			Variation("InspectorSections", "VBoxContainer");
			Constant("separation", "InspectorSections", 4);
			Variation("InspectorRows", "VBoxContainer");
			Constant("separation", "InspectorRows", 2);
			Variation("InspectorRow", "HBoxContainer");
			Constant("separation", "InspectorRow", 6);
			Variation("InspectorEditor", "HBoxContainer");
			Constant("separation", "InspectorEditor", 4);
			Variation("InspectorEditorSlot", "MarginContainer");
			Constant("margin_left", "InspectorEditorSlot", 0);
			Constant("margin_right", "InspectorEditorSlot", 0);
			Constant("margin_top", "InspectorEditorSlot", 0);
			Constant("margin_bottom", "InspectorEditorSlot", 0);

			Variation("InspectorIndent", "MarginContainer");
			Constant("margin_left", "InspectorIndent", 14);
			Constant("margin_top", "InspectorIndent", 2);
			Constant("margin_bottom", "InspectorIndent", 2);
			Variation("InspectorNoIndent", "MarginContainer");
			Constant("margin_left", "InspectorNoIndent", 4);
			Constant("margin_right", "InspectorNoIndent", 4);
			Constant("margin_top", "InspectorNoIndent", 2);
			Constant("margin_bottom", "InspectorNoIndent", 4);

			// section headers are buttons the width of the panel; the fold
			// arrow is drawn in the left margin
			static ThemedStyleBox SectionBox(ThemeDefinition background, ThemeShade shade, float alpha)
			{
				ThemedStyleBox box = Flat(background, shade, alpha, radius: 3, margin: 4f);
				box.ContentMarginLeft = 20f;
				return box;
			}

			Variation("InspectorSection", "Button");
			Box("normal", "InspectorSection", SectionBox(bg1, hover, 1f));
			Box("hover", "InspectorSection", SectionBox(bg1, pressed, 1f));
			Box("pressed", "InspectorSection", SectionBox(bg1, hover, 1f));
			Box("hover_pressed", "InspectorSection", SectionBox(bg1, pressed, 1f));
			Box("focus", "InspectorSection", new StyleBoxEmpty());
			Fonts("InspectorSection", ThemeFontRole.None, ThemeFontSizeRole.Title);

			Variation("InspectorSubsection", "Button");
			Box("normal", "InspectorSubsection", SectionBox(font, ThemeShade.None, 0.05f));
			Box("hover", "InspectorSubsection", SectionBox(font, ThemeShade.None, 0.1f));
			Box("pressed", "InspectorSubsection", SectionBox(font, ThemeShade.None, 0.05f));
			Box("hover_pressed", "InspectorSubsection", SectionBox(font, ThemeShade.None, 0.1f));
			Box("focus", "InspectorSubsection", new StyleBoxEmpty());

			Variation("InspectorLabel", "Label");
			Colour("InspectorLabel", "font_color", dim);
			Variation("InspectorValue", "Label");
			Colour("InspectorValue", "font_color", font);
			Variation("InspectorAxis", "Label");
			Colour("InspectorAxis", "font_color", dim);
			Fonts("InspectorAxis", ThemeFontRole.None, ThemeFontSizeRole.Small);

			Variation("InspectorField", "LineEdit");
			Box("normal", "InspectorField", Flat(field, radius: 3, border: stroke, borderWidth: 1, margin: 3f));
			Box("focus", "InspectorField", Flat(field, radius: 3, border: accent, borderWidth: 1, margin: 3f));
			Box("read_only", "InspectorField", Flat(field, disabled, radius: 3, border: stroke, borderWidth: 1, margin: 3f));
			Variation("InspectorMultiline", "TextEdit");

			// small flat buttons: add, remove, browse, the frames toggle, link
			Variation("InspectorButton", "Button");
			Box("normal", "InspectorButton", Flat(font, alpha: 0.06f, radius: 3, margin: 2f));
			Box("hover", "InspectorButton", Flat(font, alpha: 0.14f, radius: 3, margin: 2f));
			Box("pressed", "InspectorButton", Flat(accent, alpha: 0.5f, radius: 3, margin: 2f));
			Box("hover_pressed", "InspectorButton", Flat(accent, alpha: 0.6f, radius: 3, margin: 2f));
			Box("disabled", "InspectorButton", Flat(font, alpha: 0.03f, radius: 3, margin: 2f));
			Box("focus", "InspectorButton", new StyleBoxEmpty());
			Fonts("InspectorButton", ThemeFontRole.None, ThemeFontSizeRole.Small);

			// the glyph buttons - keyframe column, resets - draw themselves;
			// the button is only a hover
			Variation("InspectorKeyButton", "Button");
			Box("normal", "InspectorKeyButton", new StyleBoxEmpty());
			Box("hover", "InspectorKeyButton", Flat(font, alpha: 0.1f, radius: 3, margin: 0f));
			Box("pressed", "InspectorKeyButton", Flat(font, alpha: 0.18f, radius: 3, margin: 0f));
			Box("hover_pressed", "InspectorKeyButton", Flat(font, alpha: 0.18f, radius: 3, margin: 0f));
			Box("disabled", "InspectorKeyButton", new StyleBoxEmpty());
			Box("focus", "InspectorKeyButton", new StyleBoxEmpty());

			// the glyph icons take the dim colour, and the accent while active
			foreach (string icon in new[] { "icon_normal_color", "icon_hover_color", "icon_pressed_color", "icon_hover_pressed_color", "icon_focus_color" })
				Colour("InspectorKeyButton", icon, dim);
			Colour("InspectorKeyButton", "icon_disabled_color", dim, disabled);

			Variation("InspectorKeyButtonActive", "Button");
			Box("normal", "InspectorKeyButtonActive", new StyleBoxEmpty());
			Box("hover", "InspectorKeyButtonActive", Flat(font, alpha: 0.1f, radius: 3, margin: 0f));
			Box("pressed", "InspectorKeyButtonActive", Flat(font, alpha: 0.18f, radius: 3, margin: 0f));
			Box("hover_pressed", "InspectorKeyButtonActive", Flat(font, alpha: 0.18f, radius: 3, margin: 0f));
			Box("disabled", "InspectorKeyButtonActive", new StyleBoxEmpty());
			Box("focus", "InspectorKeyButtonActive", new StyleBoxEmpty());
			foreach (string icon in new[] { "icon_normal_color", "icon_hover_color", "icon_pressed_color", "icon_hover_pressed_color", "icon_focus_color" })
				Colour("InspectorKeyButtonActive", icon, accent);
			Colour("InspectorKeyButtonActive", "icon_disabled_color", accent, disabled);

			Variation("InspectorSwatch", "Button");
			Box("normal", "InspectorSwatch", Flat(field, radius: 3, border: stroke, borderWidth: 1, margin: 0f));
			Box("hover", "InspectorSwatch", Flat(field, hover, radius: 3, border: stroke, borderWidth: 1, margin: 0f));
			Box("pressed", "InspectorSwatch", Flat(field, pressed, radius: 3, border: stroke, borderWidth: 1, margin: 0f));
			Box("hover_pressed", "InspectorSwatch", Flat(field, pressed, radius: 3, border: stroke, borderWidth: 1, margin: 0f));
			Box("focus", "InspectorSwatch", Outline(accent, 0.6f, 1, 3));

			// the spin slider and the angle knob draw themselves from these
			Box("normal", "SpinSlider", Flat(field, radius: 3, border: stroke, borderWidth: 1, margin: 4f));
			Box("hover", "SpinSlider", Flat(field, hover, radius: 3, border: stroke, borderWidth: 1, margin: 4f));
			Box("focus", "SpinSlider", Flat(field, radius: 3, border: accent, borderWidth: 1, margin: 4f));
			Colour("SpinSlider", "font", font);
			Colour("SpinSlider", "unit", dim);
			Colour("SpinSlider", "mixed", dim);
			Colour("SpinSlider", "range_fill", accent, alpha: 0.35f);

			Colour("AngleKnob", "fill", field);
			Colour("AngleKnob", "track", font, alpha: 0.3f);
			Colour("AngleKnob", "pointer", accent);

			Colour("Inspector", "key", accent);
			Colour("Inspector", "key_dim", dim);
			Colour("Inspector", "arrow", font);
			Colour("Inspector", "reset", dim);

			// ---- colours code looks up by name ----
			Colour("Clip", "video", ThemeDefinition.VideoClipColor);
			Colour("Clip", "audio", ThemeDefinition.AudioClipColor);
			Colour("Clip", "text", ThemeDefinition.TextClipColor);
			Colour("Clip", "generator_video", ThemeDefinition.GeneratorVideoClipColor);
			Colour("Clip", "generator_audio", ThemeDefinition.GeneratorAudioClipColor);

			Colour("Node", "effect", ThemeDefinition.EffectNodeColor);
			Colour("Node", "mask", ThemeDefinition.MaskNodeColor);
			Colour("Node", "math", ThemeDefinition.MathNodeColor);
			Colour("Node", "keying", ThemeDefinition.KeyingNodeColor);
			Colour("Node", "source", ThemeDefinition.SourceNodeColor);
			Colour("Node", "audio", ThemeDefinition.AudioNodeColor);
			Colour("Node", "composite", ThemeDefinition.CompositeNodeColor);

			Colour("Ruler", "mark", white);
			Colour("Ruler", "submark", dim);

			Colour("Timeline", "snap_line", white, alpha: 0.6f);
			Colour("Timeline", "playhead", accent);
		}
		finally
		{
			applying = false;
		}
	}

	// ---- the user's choice of preset and accent, kept between runs ----

	public const string SettingsPath = "user://theme.json";

	sealed class Settings
	{
		public string Preset { get; set; } = "Dark";
		public string Accent { get; set; } = "";
	}

	// applies the saved preset and accent to this theme, fills in any item
	// the theme file lacks, and colours the lot; seeds the file on first run
	public void LoadUserSettings()
	{
		string file = ProjectSettings.GlobalizePath(SettingsPath);

		if (!File.Exists(file)) SaveUserSettings();
		else
		{
			try
			{
				Settings settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(file)) ?? new();

				if (Enum.TryParse(settings.Preset, ignoreCase: true, out ColorPreset preset) && preset != ColorPreset.Custom)
					ApplyPreset(preset);

				if (!string.IsNullOrWhiteSpace(settings.Accent) && Color.HtmlIsValid(settings.Accent))
					AccentColor = Color.FromHtml(settings.Accent);
			}
			catch (Exception e)
			{
				GD.PushWarning($"Could not read the theme settings from {file}: {e.Message}");
			}
		}

		Regenerate();
	}

	public void SaveUserSettings()
	{
		string file = ProjectSettings.GlobalizePath(SettingsPath);

		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(file));

			Settings settings = new() { Preset = _preset.ToString(), Accent = "#" + _accentColor.ToHtml(false) };
			File.WriteAllText(file, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
		}
		catch (Exception e)
		{
			GD.PushWarning($"Could not save the theme settings to {file}: {e.Message}");
		}
	}
}

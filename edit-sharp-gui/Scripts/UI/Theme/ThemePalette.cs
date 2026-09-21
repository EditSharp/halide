using Godot;
using Godot.Collections;
using System.Collections.Generic;

namespace EditSharpGUI.Scripts.UI.Theming;

// the definitions a theme resolves against: the colours, the rules that
// make a hover, pressed, disabled or focused look from a base colour, the
// two fonts with their weights and sizes. one resource per look - a dark
// one, a light one - referenced by the theme and by every style in it,
// and swapped for another to change the whole app. nothing here is ever
// generated: a palette is authored, and everything else resolves through
// it at draw time
[Tool, GlobalClass]
public partial class ThemePalette : Resource
{
	// bumped on every change, so a style or stylebox can tell when what it
	// resolved last is stale without keeping a copy of everything
	public int Version { get; private set; }

	// the change signal goes out at the end of the frame, by object and
	// method name: a setter runs while a file loads and while the editor
	// restores every script instance after a build, when the resources
	// listening may not exist yet - a synchronous emission would reach a
	// released instance
	bool emitQueued;

	void Bump()
	{
		Version++;
		if (emitQueued) return;
		emitQueued = true;
		new Callable(this, MethodName.EmitPending).CallDeferred();
	}

	void EmitPending()
	{
		emitQueued = false;
		EmitChanged();
	}

	void Define<T>(ref T field, T value)
	{
		if (EqualityComparer<T>.Default.Equals(field, value)) return;
		field = value;
		Bump();
	}

	// ---- colours ----

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
	// starts as the accent so nothing changes until it is tuned apart
	Color _playbackColor = new(0.692f, 0.219f, 0.130f);

	[ExportGroup("Colors")]
	[Export] public Color BackgroundColor1 { get => _backgroundColor1; set => Define(ref _backgroundColor1, value); }
	[Export] public Color BackgroundColor2 { get => _backgroundColor2; set => Define(ref _backgroundColor2, value); }
	[Export] public Color PopupBackgroundColor { get => _popupBackgroundColor; set => Define(ref _popupBackgroundColor, value); }
	[Export] public Color AccentColor { get => _accentColor; set => Define(ref _accentColor, value); }
	[Export] public Color FontColor1 { get => _fontColor1; set => Define(ref _fontColor1, value); }
	[Export] public Color FontColor2 { get => _fontColor2; set => Define(ref _fontColor2, value); }
	[Export] public Color TextFieldColor { get => _textFieldColor; set => Define(ref _textFieldColor, value); }
	[Export] public Color ButtonColor { get => _buttonColor; set => Define(ref _buttonColor, value); }
	[Export] public Color HyperlinkColor { get => _hyperlinkColor; set => Define(ref _hyperlinkColor, value); }
	[Export] public Color StrokeColor { get => _strokeColor; set => Define(ref _strokeColor, value); }
	[Export] public Color WhiteColor { get => _whiteColor; set => Define(ref _whiteColor, value); }
	[Export] public Color BlackColor { get => _blackColor; set => Define(ref _blackColor, value); }
	[Export] public Color PlaybackColor { get => _playbackColor; set => Define(ref _playbackColor, value); }

	Color _videoClipColor = new(0.102f, 0.373f, 0.706f);
	Color _audioClipColor = new(0.267f, 0.561f, 0.392f);
	Color _textClipColor = new(0.55f, 0.35f, 0.7f);
	Color _generatorVideoClipColor = new(0.7f, 0.45f, 0.15f);
	Color _generatorAudioClipColor = new(0.2f, 0.55f, 0.55f);

	[ExportGroup("Clip Colors")]
	[Export] public Color VideoClipColor { get => _videoClipColor; set => Define(ref _videoClipColor, value); }
	[Export] public Color AudioClipColor { get => _audioClipColor; set => Define(ref _audioClipColor, value); }
	[Export] public Color TextClipColor { get => _textClipColor; set => Define(ref _textClipColor, value); }
	[Export] public Color GeneratorVideoClipColor { get => _generatorVideoClipColor; set => Define(ref _generatorVideoClipColor, value); }
	[Export] public Color GeneratorAudioClipColor { get => _generatorAudioClipColor; set => Define(ref _generatorAudioClipColor, value); }

	Color _effectNodeColor = new(0.35f, 0.5f, 0.8f);
	Color _maskNodeColor = new(0.6f, 0.6f, 0.3f);
	Color _mathNodeColor = new(0.5f, 0.5f, 0.5f);
	Color _keyingNodeColor = new(0.3f, 0.7f, 0.4f);
	Color _sourceNodeColor = new(0.75f, 0.4f, 0.3f);
	Color _audioNodeColor = new(0.3f, 0.6f, 0.45f);
	Color _compositeNodeColor = new(0.6f, 0.4f, 0.7f);

	[ExportGroup("Node Colors")]
	[Export] public Color EffectNodeColor { get => _effectNodeColor; set => Define(ref _effectNodeColor, value); }
	[Export] public Color MaskNodeColor { get => _maskNodeColor; set => Define(ref _maskNodeColor, value); }
	[Export] public Color MathNodeColor { get => _mathNodeColor; set => Define(ref _mathNodeColor, value); }
	[Export] public Color KeyingNodeColor { get => _keyingNodeColor; set => Define(ref _keyingNodeColor, value); }
	[Export] public Color SourceNodeColor { get => _sourceNodeColor; set => Define(ref _sourceNodeColor, value); }
	[Export] public Color AudioNodeColor { get => _audioNodeColor; set => Define(ref _audioNodeColor, value); }
	[Export] public Color CompositeNodeColor { get => _compositeNodeColor; set => Define(ref _compositeNodeColor, value); }

	// ---- shades: how a base colour becomes a state's ----

	float _hoverShift = 0.08f;
	float _pressedShift = 0.12f;
	float _disabledAlpha = 0.5f;
	float _focusAlpha = 0.6f;
	int _focusWidth = 1;

	// nudged towards white on a dark palette and towards black on a light
	// one, so a hover always stands out from its background
	[ExportGroup("Shades")]
	[Export(PropertyHint.Range, "0,0.5,0.01")] public float HoverShift { get => _hoverShift; set => Define(ref _hoverShift, value); }
	[Export(PropertyHint.Range, "0,0.5,0.01")] public float PressedShift { get => _pressedShift; set => Define(ref _pressedShift, value); }
	[Export(PropertyHint.Range, "0,1,0.01")] public float DisabledAlpha { get => _disabledAlpha; set => Define(ref _disabledAlpha, value); }
	[Export(PropertyHint.Range, "0,1,0.01")] public float FocusAlpha { get => _focusAlpha; set => Define(ref _focusAlpha, value); }
	[Export(PropertyHint.Range, "0,8,1")] public int FocusWidth { get => _focusWidth; set => Define(ref _focusWidth, value); }

	// ---- fonts ----

	Font _uiFont;
	Font _monoFont;
	int _smallFontSize = 12;
	int _fontSize = 14;
	int _titleFontSize = 16;
	int _lightWeight = 300;
	int _regularWeight = 400;
	int _mediumWeight = 500;
	int _boldWeight = 700;

	[ExportGroup("Fonts")]
	[Export] public Font UIFont { get => _uiFont; set => Define(ref _uiFont, value); }
	[Export] public Font MonoFont { get => _monoFont; set => Define(ref _monoFont, value); }

	// when the UI font is a variable font its weight axis makes every
	// weight; a static font cannot, so a file per weight can stand in
	Font _uiFontLight, _uiFontMedium, _uiFontBold;

	[ExportSubgroup("Static Weights")]
	[Export] public Font UIFontLight { get => _uiFontLight; set => Define(ref _uiFontLight, value); }
	[Export] public Font UIFontMedium { get => _uiFontMedium; set => Define(ref _uiFontMedium, value); }
	[Export] public Font UIFontBold { get => _uiFontBold; set => Define(ref _uiFontBold, value); }

	// whether the UI font carries a weight axis to vary
	public bool UIFontIsVariable => _uiFont is not null && _uiFont.GetSupportedVariationList().ContainsKey(TextServerManager.GetPrimaryInterface().NameToTag("wght"));
	[Export] public int SmallFontSize { get => _smallFontSize; set => Define(ref _smallFontSize, value); }
	[Export] public int FontSize { get => _fontSize; set => Define(ref _fontSize, value); }
	[Export] public int TitleFontSize { get => _titleFontSize; set => Define(ref _titleFontSize, value); }
	[Export(PropertyHint.Range, "100,900,1")] public int LightWeight { get => _lightWeight; set => Define(ref _lightWeight, value); }
	[Export(PropertyHint.Range, "100,900,1")] public int RegularWeight { get => _regularWeight; set => Define(ref _regularWeight, value); }
	[Export(PropertyHint.Range, "100,900,1")] public int MediumWeight { get => _mediumWeight; set => Define(ref _mediumWeight, value); }
	[Export(PropertyHint.Range, "100,900,1")] public int BoldWeight { get => _boldWeight; set => Define(ref _boldWeight, value); }

	// ---- resolving ----

	public bool Dark => _backgroundColor1.Luminance < 0.5f;

	public Color Resolve(ThemeDefinition definition, ThemeShade shade = ThemeShade.None, float alpha = 1f)
	{
		Color c = Base(definition);

		c = shade switch
		{
			ThemeShade.Hover => Dark ? c.Lightened(_hoverShift) : c.Darkened(_hoverShift),
			ThemeShade.Pressed => Dark ? c.Lightened(_pressedShift) : c.Darkened(_pressedShift),
			ThemeShade.Disabled => new Color(c.R, c.G, c.B, c.A * _disabledAlpha),
			_ => c
		};

		return alpha >= 1f ? c : new Color(c.R, c.G, c.B, c.A * alpha);
	}

	// the shade a state applies to a base colour
	public static ThemeShade ShadeOf(StyleState state) => state switch
	{
		StyleState.Hover => ThemeShade.Hover,
		StyleState.Pressed => ThemeShade.Pressed,
		StyleState.Disabled => ThemeShade.Disabled,
		_ => ThemeShade.None
	};

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
		ThemeDefinition.PlaybackColor => _playbackColor,
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

	public int ResolveFontSize(ThemeFontSizeRole role) => role switch
	{
		ThemeFontSizeRole.Small => _smallFontSize,
		ThemeFontSizeRole.Title => _titleFontSize,
		_ => _fontSize
	};

	// the UI font at a weight is a variation of it, made once per weight and
	// remade only when the font or the weight changes - a new instance would
	// read as a change on every type that uses it
	readonly System.Collections.Generic.Dictionary<ThemeFontWeight, (Font Base, int Weight, FontVariation Variation)> weighted = new();

	public Font ResolveFont(ThemeFontFamily family, ThemeFontWeight weight)
	{
		if (family == ThemeFontFamily.Mono) return _monoFont;
		if (family == ThemeFontFamily.None || _uiFont is null) return null;

		// a static font per weight, when given, beats an axis the font may not have
		Font given = weight switch
		{
			ThemeFontWeight.Light => _uiFontLight,
			ThemeFontWeight.Medium => _uiFontMedium,
			ThemeFontWeight.Bold => _uiFontBold,
			_ => null
		};
		if (given is not null) return given;
		if (weight == ThemeFontWeight.Regular || !UIFontIsVariable) return _uiFont;

		int target = weight switch
		{
			ThemeFontWeight.Light => _lightWeight,
			ThemeFontWeight.Medium => _mediumWeight,
			ThemeFontWeight.Bold => _boldWeight,
			_ => _regularWeight
		};

		if (weighted.TryGetValue(weight, out (Font Base, int Weight, FontVariation Variation) made) && made.Base == _uiFont && made.Weight == target)
			return made.Variation;

		FontVariation variation = new() { BaseFont = _uiFont };

		using (Dictionary axes = new() { [TextServerManager.GetPrimaryInterface().NameToTag("wght")] = target })
			variation.VariationOpentype = axes;

		weighted[weight] = (_uiFont, target, variation);
		return variation;
	}
}

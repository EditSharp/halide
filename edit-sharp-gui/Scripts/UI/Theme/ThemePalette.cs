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

	// ---- swatches: the colours clips and nodes wear ----

	// one colour per swatch name, for clips and for nodes apart, so a
	// palette can tune each set to its background
	Color _clipPoppy = new(0.76f, 0.36f, 0.36f);
	Color _clipRed = new(0.85f, 0.22f, 0.23f);
	Color _clipOrange = new(0.88f, 0.48f, 0.18f);
	Color _clipMango = new(0.94f, 0.63f, 0.19f);
	Color _clipYellow = new(0.9f, 0.78f, 0.29f);
	Color _clipGreen = new(0.3f, 0.6f, 0.37f);
	Color _clipTurquoise = new(0.2f, 0.65f, 0.63f);
	Color _clipLime = new(0.56f, 0.82f, 0.31f);
	Color _clipCyan = new(0.23f, 0.7f, 0.9f);
	Color _clipPurple = new(0.55f, 0.36f, 0.71f);
	Color _clipPink = new(0.88f, 0.44f, 0.6f);
	Color _clipLavender = new(0.66f, 0.61f, 0.78f);
	Color _clipMagenta = new(0.78f, 0.31f, 0.75f);
	Color _clipBrown = new(0.55f, 0.38f, 0.26f);
	Color _clipTan = new(0.79f, 0.66f, 0.51f);
	Color _clipGray = new(0.54f, 0.56f, 0.59f);
	Color _clipBlue = new(0.102f, 0.373f, 0.706f);
	Color _clipNavy = new(0.18f, 0.29f, 0.5f);

	[ExportGroup("Clip Swatches")]
	[Export] public Color ClipPoppy { get => _clipPoppy; set => Define(ref _clipPoppy, value); }
	[Export] public Color ClipRed { get => _clipRed; set => Define(ref _clipRed, value); }
	[Export] public Color ClipOrange { get => _clipOrange; set => Define(ref _clipOrange, value); }
	[Export] public Color ClipMango { get => _clipMango; set => Define(ref _clipMango, value); }
	[Export] public Color ClipYellow { get => _clipYellow; set => Define(ref _clipYellow, value); }
	[Export] public Color ClipGreen { get => _clipGreen; set => Define(ref _clipGreen, value); }
	[Export] public Color ClipTurquoise { get => _clipTurquoise; set => Define(ref _clipTurquoise, value); }
	[Export] public Color ClipLime { get => _clipLime; set => Define(ref _clipLime, value); }
	[Export] public Color ClipCyan { get => _clipCyan; set => Define(ref _clipCyan, value); }
	[Export] public Color ClipPurple { get => _clipPurple; set => Define(ref _clipPurple, value); }
	[Export] public Color ClipPink { get => _clipPink; set => Define(ref _clipPink, value); }
	[Export] public Color ClipLavender { get => _clipLavender; set => Define(ref _clipLavender, value); }
	[Export] public Color ClipMagenta { get => _clipMagenta; set => Define(ref _clipMagenta, value); }
	[Export] public Color ClipBrown { get => _clipBrown; set => Define(ref _clipBrown, value); }
	[Export] public Color ClipTan { get => _clipTan; set => Define(ref _clipTan, value); }
	[Export] public Color ClipGray { get => _clipGray; set => Define(ref _clipGray, value); }
	[Export] public Color ClipBlue { get => _clipBlue; set => Define(ref _clipBlue, value); }
	[Export] public Color ClipNavy { get => _clipNavy; set => Define(ref _clipNavy, value); }

	Color _nodePoppy = new(0.76f, 0.36f, 0.36f);
	Color _nodeRed = new(0.85f, 0.22f, 0.23f);
	Color _nodeOrange = new(0.88f, 0.48f, 0.18f);
	Color _nodeMango = new(0.94f, 0.63f, 0.19f);
	Color _nodeYellow = new(0.9f, 0.78f, 0.29f);
	Color _nodeGreen = new(0.3f, 0.6f, 0.37f);
	Color _nodeTurquoise = new(0.2f, 0.65f, 0.63f);
	Color _nodeLime = new(0.56f, 0.82f, 0.31f);
	Color _nodeCyan = new(0.23f, 0.7f, 0.9f);
	Color _nodePurple = new(0.55f, 0.36f, 0.71f);
	Color _nodePink = new(0.88f, 0.44f, 0.6f);
	Color _nodeLavender = new(0.66f, 0.61f, 0.78f);
	Color _nodeMagenta = new(0.78f, 0.31f, 0.75f);
	Color _nodeBrown = new(0.55f, 0.38f, 0.26f);
	Color _nodeTan = new(0.79f, 0.66f, 0.51f);
	Color _nodeGray = new(0.54f, 0.56f, 0.59f);
	Color _nodeBlue = new(0.102f, 0.373f, 0.706f);
	Color _nodeNavy = new(0.18f, 0.29f, 0.5f);

	[ExportGroup("Node Swatches")]
	[Export] public Color NodePoppy { get => _nodePoppy; set => Define(ref _nodePoppy, value); }
	[Export] public Color NodeRed { get => _nodeRed; set => Define(ref _nodeRed, value); }
	[Export] public Color NodeOrange { get => _nodeOrange; set => Define(ref _nodeOrange, value); }
	[Export] public Color NodeMango { get => _nodeMango; set => Define(ref _nodeMango, value); }
	[Export] public Color NodeYellow { get => _nodeYellow; set => Define(ref _nodeYellow, value); }
	[Export] public Color NodeGreen { get => _nodeGreen; set => Define(ref _nodeGreen, value); }
	[Export] public Color NodeTurquoise { get => _nodeTurquoise; set => Define(ref _nodeTurquoise, value); }
	[Export] public Color NodeLime { get => _nodeLime; set => Define(ref _nodeLime, value); }
	[Export] public Color NodeCyan { get => _nodeCyan; set => Define(ref _nodeCyan, value); }
	[Export] public Color NodePurple { get => _nodePurple; set => Define(ref _nodePurple, value); }
	[Export] public Color NodePink { get => _nodePink; set => Define(ref _nodePink, value); }
	[Export] public Color NodeLavender { get => _nodeLavender; set => Define(ref _nodeLavender, value); }
	[Export] public Color NodeMagenta { get => _nodeMagenta; set => Define(ref _nodeMagenta, value); }
	[Export] public Color NodeBrown { get => _nodeBrown; set => Define(ref _nodeBrown, value); }
	[Export] public Color NodeTan { get => _nodeTan; set => Define(ref _nodeTan, value); }
	[Export] public Color NodeGray { get => _nodeGray; set => Define(ref _nodeGray, value); }
	[Export] public Color NodeBlue { get => _nodeBlue; set => Define(ref _nodeBlue, value); }
	[Export] public Color NodeNavy { get => _nodeNavy; set => Define(ref _nodeNavy, value); }

	// which swatch each kind of clip and node wears until the user picks
	ClipSwatch _videoClip = ClipSwatch.Blue;
	ClipSwatch _audioClip = ClipSwatch.Green;
	ClipSwatch _textClip = ClipSwatch.Purple;
	ClipSwatch _generatorVideoClip = ClipSwatch.Orange;
	ClipSwatch _generatorAudioClip = ClipSwatch.Turquoise;

	[ExportGroup("Clip Defaults")]
	[Export] public ClipSwatch VideoClip { get => _videoClip; set => Define(ref _videoClip, value); }
	[Export] public ClipSwatch AudioClip { get => _audioClip; set => Define(ref _audioClip, value); }
	[Export] public ClipSwatch TextClip { get => _textClip; set => Define(ref _textClip, value); }
	[Export] public ClipSwatch GeneratorVideoClip { get => _generatorVideoClip; set => Define(ref _generatorVideoClip, value); }
	[Export] public ClipSwatch GeneratorAudioClip { get => _generatorAudioClip; set => Define(ref _generatorAudioClip, value); }

	NodeSwatch _effectNode = NodeSwatch.Blue;
	NodeSwatch _maskNode = NodeSwatch.Purple;
	NodeSwatch _mathNode = NodeSwatch.Green;
	NodeSwatch _keyingNode = NodeSwatch.Cyan;
	NodeSwatch _inputNode = NodeSwatch.Mango;
	NodeSwatch _audioNode = NodeSwatch.Magenta;
	NodeSwatch _compositeNode = NodeSwatch.Pink;

	[ExportGroup("Node Defaults")]
	[Export] public NodeSwatch EffectNode { get => _effectNode; set => Define(ref _effectNode, value); }
	[Export] public NodeSwatch MaskNode { get => _maskNode; set => Define(ref _maskNode, value); }
	[Export] public NodeSwatch MathNode { get => _mathNode; set => Define(ref _mathNode, value); }
	[Export] public NodeSwatch KeyingNode { get => _keyingNode; set => Define(ref _keyingNode, value); }
	[Export] public NodeSwatch InputNode { get => _inputNode; set => Define(ref _inputNode, value); }
	[Export] public NodeSwatch AudioNode { get => _audioNode; set => Define(ref _audioNode, value); }
	[Export] public NodeSwatch CompositeNode { get => _compositeNode; set => Define(ref _compositeNode, value); }

	public Color Resolve(ClipSwatch swatch) => swatch switch
	{
		ClipSwatch.Poppy => _clipPoppy,
		ClipSwatch.Red => _clipRed,
		ClipSwatch.Orange => _clipOrange,
		ClipSwatch.Mango => _clipMango,
		ClipSwatch.Yellow => _clipYellow,
		ClipSwatch.Green => _clipGreen,
		ClipSwatch.Turquoise => _clipTurquoise,
		ClipSwatch.Lime => _clipLime,
		ClipSwatch.Cyan => _clipCyan,
		ClipSwatch.Purple => _clipPurple,
		ClipSwatch.Pink => _clipPink,
		ClipSwatch.Lavender => _clipLavender,
		ClipSwatch.Magenta => _clipMagenta,
		ClipSwatch.Brown => _clipBrown,
		ClipSwatch.Tan => _clipTan,
		ClipSwatch.Gray => _clipGray,
		ClipSwatch.Blue => _clipBlue,
		ClipSwatch.Navy => _clipNavy,
		_ => Colors.Magenta
	};

	public Color Resolve(NodeSwatch swatch) => swatch switch
	{
		NodeSwatch.Poppy => _nodePoppy,
		NodeSwatch.Red => _nodeRed,
		NodeSwatch.Orange => _nodeOrange,
		NodeSwatch.Mango => _nodeMango,
		NodeSwatch.Yellow => _nodeYellow,
		NodeSwatch.Green => _nodeGreen,
		NodeSwatch.Turquoise => _nodeTurquoise,
		NodeSwatch.Lime => _nodeLime,
		NodeSwatch.Cyan => _nodeCyan,
		NodeSwatch.Purple => _nodePurple,
		NodeSwatch.Pink => _nodePink,
		NodeSwatch.Lavender => _nodeLavender,
		NodeSwatch.Magenta => _nodeMagenta,
		NodeSwatch.Brown => _nodeBrown,
		NodeSwatch.Tan => _nodeTan,
		NodeSwatch.Gray => _nodeGray,
		NodeSwatch.Blue => _nodeBlue,
		NodeSwatch.Navy => _nodeNavy,
		_ => Colors.Magenta
	};

	// the swatch a clip kind wears by default, by the Clip theme type's item
	// name; null for a name that is not a kind
	public ClipSwatch? ClipKindSwatch(string kind) => kind switch
	{
		"video" => _videoClip,
		"audio" => _audioClip,
		"text" => _textClip,
		"generator_video" => _generatorVideoClip,
		"generator_audio" => _generatorAudioClip,
		_ => null
	};

	// the swatch a node category wears by default, by the Node theme type's item name
	public NodeSwatch? NodeKindSwatch(string kind) => kind switch
	{
		"effect" => _effectNode,
		"mask" => _maskNode,
		"math" => _mathNode,
		"keying" => _keyingNode,
		"input" => _inputNode,
		"audio" => _audioNode,
		"composite" => _compositeNode,
		_ => null
	};

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
		=> Shade(Base(definition), shade, alpha);

	// a colour shifted by a shade and faded by an alpha, the palette's way
	public Color Shade(Color c, ThemeShade shade, float alpha = 1f)
	{
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

namespace EditSharpGUI.Scripts.UI.Theming;

// the colour definitions a theme item can take its colour from - the
// names in the palette. None leaves a colour alone; Transparent is a
// colour of its own
public enum ThemeDefinition
{
	None,
	Transparent,

	BackgroundColor1,
	BackgroundColor2,
	PopupBackgroundColor,
	AccentColor,
	FontColor1,
	FontColor2,
	TextFieldColor,
	ButtonColor,
	HyperlinkColor,
	StrokeColor,
	WhiteColor,
	BlackColor,

	VideoClipColor,
	AudioClipColor,
	TextClipColor,
	GeneratorVideoClipColor,
	GeneratorAudioClipColor,

	EffectNodeColor,
	MaskNodeColor,
	MathNodeColor,
	KeyingNodeColor,
	SourceNodeColor,
	AudioNodeColor,
	CompositeNodeColor
}

// how a definition is shifted before use: the same colour, or the shade
// the palette derives for a hovered, pressed or disabled part
public enum ThemeShade
{
	None,
	Hover,
	Pressed,
	Disabled
}

// the state a themed stylebox stands for: its look is its style's, shifted
// by the palette's rule for the state
public enum StyleState
{
	Normal,
	Hover,
	Pressed,
	Disabled,
	Focus
}

public enum ThemeFontFamily { None, UI, Mono }

public enum ThemeFontWeight { Light, Regular, Medium, Bold }

public enum ThemeFontSizeRole { None, Small, Normal, Title }

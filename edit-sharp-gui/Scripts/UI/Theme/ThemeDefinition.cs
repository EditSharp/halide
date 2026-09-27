namespace EditSharpGUI.Scripts.UI.Theming;

// the colour definitions a theme item can take its colour from - the
// names in the palette. None leaves a colour alone; Transparent is a
// colour of its own. clip and node colours are swatches, not definitions
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

	// the colour of everything to do with time and motion: the playhead,
	// keyframes, the play button
	PlaybackColor,
	// problems: invalid values, conflicts, anything that stops an action
	ErrorColor,
	// actions that remove or close: the close button's hover, destructive buttons
	DestructiveColor
}

// the colours a clip can wear. each is a palette colour; a clip kind's
// default and the user's own pick are both one of these
public enum ClipSwatch
{
	Poppy,
	Red,
	Orange,
	Mango,
	Yellow,
	Green,
	Turquoise,
	Lime,
	Cyan,
	Purple,
	Pink,
	Lavender,
	Magenta,
	Brown,
	Tan,
	Gray,
	Blue,
	Navy
}

// the colours a node can wear, one per swatch name like the clips'
public enum NodeSwatch
{
	Poppy,
	Red,
	Orange,
	Mango,
	Yellow,
	Green,
	Turquoise,
	Lime,
	Cyan,
	Purple,
	Pink,
	Lavender,
	Magenta,
	Brown,
	Tan,
	Gray,
	Blue,
	Navy
}

// where a bound colour item takes its value from: a definition, a swatch,
// or the swatch the palette picks for the item's clip or node kind
public enum ColorSource
{
	Definition,
	ClipSwatch,
	NodeSwatch,
	ClipKind,
	NodeKind
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

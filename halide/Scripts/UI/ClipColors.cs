using System;
using System.Linq;
using EditSharp.Components.Clips;
using EditSharp.Components.Nodes.Input;
using Halide.Scripts.UI.Theming;
using Godot;

namespace Halide.Scripts.UI;

// what colour a clip is shown in. a clip carries a colour as text - a swatch
// name, or a hex value for a custom pick - and this turns it into the
// theme's colour for it, falling back to the theme's colour for the clip's
// kind when it carries none
public static class ClipColors
{
	// which of the theme's clip kinds a clip is: by what feeds its graph,
	// since a clip is its graph
	public static string Kind(Clip clip)
	{
		bool media = clip.Graph.AllNodes.Any(n => n is VideoMediaNode or TimelineVideoNode or AudioMediaNode or TimelineAudioNode);
		bool text = clip.Graph.AllNodes.Any(n => n is TextNode);

		if (clip is AudioClip) return media ? "audio" : "generator_audio";
		if (text && !media) return "text";
		return media ? "video" : "generator_video";
	}

	// the swatch a colour string names, if it names one
	public static ClipSwatch? Swatch(string color)
		=> Enum.TryParse(color, ignoreCase: true, out ClipSwatch swatch) ? swatch : null;

	// whether a colour string is a hex value rather than a swatch name
	public static bool IsCustom(string color) => !string.IsNullOrEmpty(color) && color.StartsWith('#') && Color.HtmlIsValid(color);

	// the item under the theme's Clip type that a swatch is stored as
	public static string Item(ClipSwatch swatch) => swatch.ToString().ToLowerInvariant();

	// the colour a clip shows, read through a control's theme
	public static Color Resolve(Control control, Clip clip)
	{
		string color = clip.Color;

		if (IsCustom(color)) return Color.FromHtml(color);
		if (Swatch(color) is ClipSwatch swatch) return control.GetThemeColor(Item(swatch), "Clip");

		return control.GetThemeColor(Kind(clip), "Clip");
	}

	// the waveform's colour: the clip's, lifted by the theme's brightness
	public static Color Waveform(Control control, Color clip)
	{
		float lift = control.HasThemeConstant("waveform_brightness", "Clip") ? control.GetThemeConstant("waveform_brightness", "Clip") / 100f : 0.3f;
		return clip.Lightened(lift);
	}
}

using EditSharp.Components.Nodes;
using EditSharp.Components.Nodes.Effects;
using EditSharp.Components.Nodes.Sources;
using System.Text;

namespace EditSharpGUI.Scripts.UI;

// which of the theme's node colours a node wears, and what to call it
public static class NodeCategory
{
	// a name under the theme's "Node" colour type
	public static string Of(Node node) => node switch
	{
		CompositeNode => "composite",
		ShapeMaskNode or MaskCombineNode or ImageToMaskNode => "mask",
		GainNode or CompressorNode or EQNode or AudioMixNode or ToneGeneratorInputNode or AudioSourceNode or TimelineAudioInputNode => "audio",
		InputNode => "source",
		_ when node.GetType().Namespace?.EndsWith(".Math") == true => "math",
		_ => "effect"
	};

	// "ColorGeneratorInputNode" -> "Color generator"; a custom node is its name
	public static string Title(Node node)
	{
		if (node is CompositeNode composite) return composite.Name;

		string name = node.GetType().Name;

		foreach (string suffix in new[] { "InputNode", "SourceNode", "Node" })
		{
			if (name.EndsWith(suffix) && name.Length > suffix.Length)
			{
				name = name[..^suffix.Length];
				if (suffix == "SourceNode") name += " source";
				break;
			}
		}

		StringBuilder text = new(name.Length + 4);

		for (int i = 0; i < name.Length; i++)
		{
			char c = name[i];

			if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1]))
			{
				text.Append(' ');
				text.Append(char.ToLowerInvariant(c));
			}
			else text.Append(c);
		}

		return text.ToString();
	}
}

using EditSharp.Components.Nodes;
using EditSharp.Components.Nodes.Effects;
using EditSharp.Components.Nodes.Input;
using System.Text;

namespace Halide.Scripts.UI;

// which of the theme's node colours a node wears, and what to call it
public static class NodeCategory
{
	// a name under the theme's "Node" colour type
	public static string Of(Node node) => node switch
	{
		CompositeNode => "composite",
		ShapeMaskNode or MaskCombineNode or ImageToMaskNode => "mask",
		GainNode or CompressorNode or EQNode or AudioMixNode or AudioInputNode => "audio",
		InputNode => "input",
		_ when node.GetType().Namespace?.EndsWith(".Math") == true => "math",
		_ => "effect"
	};

	// the kind's own name - "Video", "Color", "Drop shadow" - as the kind
	// picker says it; a custom node is its name, and a node without a kind
	// is its class name spaced out: "SomeOddNode" -> "Some odd"
	public static string Title(Node node)
	{
		if (node is CompositeNode composite) return composite.Name;
		if (NodeKinds.Of(node) is { } kind) return kind.DisplayName;

		string name = node.GetType().Name;
		if (name.EndsWith("Node") && name.Length > 4) name = name[..^4];

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

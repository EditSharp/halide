using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace EditSharpGUI.Scripts.UI.Docking;

// a part of a dock layout: a split of two parts, or a stack of tabbed views
public abstract class DockNode
{
	public DockSplit Parent { get; internal set; }

	// every view below this node, in reading order
	public abstract IEnumerable<string> Views { get; }

	// every stack below this node, in reading order
	public abstract IEnumerable<DockStack> Stacks { get; }

	public abstract JsonObject ToJson();

	public static DockNode FromJson(JsonNode json)
	{
		if (json is not JsonObject o) return null;

		if ((string)o["type"] == "split")
		{
			DockNode first = FromJson(o["first"]), second = FromJson(o["second"]);
			if (first is null || second is null) return first ?? second;
			return new DockSplit((bool?)o["vertical"] ?? false, (float?)o["ratio"] ?? 0.5f, first, second);
		}

		DockStack stack = new([.. (o["views"]?.AsArray() ?? []).Select(v => (string)v).Where(v => !string.IsNullOrEmpty(v))]);
		if (stack.ViewIds.Count == 0) return null;
		stack.Current = stack.ViewIds.ElementAtOrDefault((int?)o["current"] ?? 0) ?? stack.ViewIds[0];
		return stack;
	}
}

using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Halide.Scripts.UI.Docking;

// views sharing one pane as tabs, one of them showing
public sealed class DockStack(List<string> views) : DockNode
{
	public List<string> ViewIds { get; } = views;
	public string Current { get; set; } = views.FirstOrDefault();

	public override IEnumerable<string> Views => ViewIds;
	public override IEnumerable<DockStack> Stacks => [this];

	public override JsonObject ToJson() => new()
	{
		["type"] = "stack",
		["views"] = new JsonArray([.. ViewIds.Select(v => (JsonNode)v)]),
		["current"] = ViewIds.IndexOf(Current),
	};
}

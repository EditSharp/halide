using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace EditSharpGUI.Scripts.UI.Docking;

// two parts side by side, or one over the other when vertical; the first takes `Ratio` of the space
public sealed class DockSplit : DockNode
{
	public bool Vertical { get; set; }
	public float Ratio { get; set; }

	public DockNode First { get; private set; }
	public DockNode Second { get; private set; }

	public DockSplit(bool vertical, float ratio, DockNode first, DockNode second)
	{
		Vertical = vertical;
		Ratio = ratio;
		SetFirst(first);
		SetSecond(second);
	}

	internal void SetFirst(DockNode node) { First = node; node.Parent = this; }
	internal void SetSecond(DockNode node) { Second = node; node.Parent = this; }

	internal void Replace(DockNode child, DockNode with)
	{
		if (First == child) SetFirst(with);
		else SetSecond(with);
	}

	public override IEnumerable<string> Views => First.Views.Concat(Second.Views);
	public override IEnumerable<DockStack> Stacks => First.Stacks.Concat(Second.Stacks);

	public override JsonObject ToJson() => new()
	{
		["type"] = "split",
		["vertical"] = Vertical,
		["ratio"] = Ratio,
		["first"] = First.ToJson(),
		["second"] = Second.ToJson(),
	};
}

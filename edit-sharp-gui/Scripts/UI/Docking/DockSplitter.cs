using Godot;
using System;

namespace EditSharpGUI.Scripts.UI.Docking;

// shows a DockSplit: its two parts share the space by the split's ratio, which a drag rewrites
[GlobalClass]
public partial class DockSplitter : SplitContainer
{
	public DockSplit Split { get; private set; }

	public event Action RatioChanged;

	public override void _Ready() => DragEnded += Fold;

	public void Hold(DockSplit split, Control first, Control second)
	{
		Split = split;
		Vertical = split.Vertical;
		AddChild(first);
		AddChild(second);
		Apply();
	}

	// the ratio as stretch ratios, so the parts keep their shares as the window resizes
	void Apply()
	{
		Control first = GetChild<Control>(0), second = GetChild<Control>(1);
		first.SizeFlagsHorizontal = second.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		first.SizeFlagsVertical = second.SizeFlagsVertical = SizeFlags.ExpandFill;
		first.SizeFlagsStretchRatio = Mathf.Clamp(Split.Ratio, 0.01f, 0.99f);
		second.SizeFlagsStretchRatio = 1 - first.SizeFlagsStretchRatio;
		SplitOffset = 0;
	}

	// a drag leaves an offset in pixels; it becomes the ratio instead
	void Fold()
	{
		Control first = GetChild<Control>(0), second = GetChild<Control>(1);
		float a = Vertical ? first.Size.Y : first.Size.X;
		float b = Vertical ? second.Size.Y : second.Size.X;
		if (a + b <= 0) return;

		Split.Ratio = a / (a + b);
		Apply();
		RatioChanged?.Invoke();
	}
}

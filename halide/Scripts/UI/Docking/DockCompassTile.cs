using Godot;

namespace Halide.Scripts.UI.Docking;

// one target of the compass: where a view dropped on it goes
[GlobalClass]
public partial class DockCompassTile : Panel
{
	[Export] public DockSide Side;

	public void SetHot(bool hot) => ThemeTypeVariation = hot ? "DockCompassTileHot" : "DockCompassTile";
}

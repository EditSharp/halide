using Godot;
using System.Linq;

namespace Halide.Scripts.UI.Docking;

// the cross of drop targets over a pane; laid out in DockCompass.tscn
[GlobalClass]
public partial class DockCompass : Control
{
	// centred on `centre`, a global point
	public void ShowAt(Vector2 centre)
	{
		GlobalPosition = (centre - Size / 2).Round();
		Visible = true;
	}

	// the tile under `at`, lit; null when the point misses them all
	public DockCompassTile TileAt(Vector2 at)
	{
		DockCompassTile hit = GetChildren().OfType<DockCompassTile>().FirstOrDefault(t => t.GetGlobalRect().HasPoint(at));
		foreach (DockCompassTile tile in GetChildren().OfType<DockCompassTile>()) tile.SetHot(tile == hit);
		return hit;
	}

	public void Unlight()
	{
		foreach (DockCompassTile tile in GetChildren().OfType<DockCompassTile>()) tile.SetHot(false);
	}
}

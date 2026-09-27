using System.Collections.Generic;

namespace Halide.Scripts.UI.Docking;

// where a view sat before it left a tree: beside the views it was split from, or among its tab mates
public sealed record DockHome(DockSide Side, float Ratio, IReadOnlyList<string> Anchors);

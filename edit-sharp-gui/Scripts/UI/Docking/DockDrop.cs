namespace EditSharpGUI.Scripts.UI.Docking;

// where a dragged view would land: a gap in a stack's tabs (Index), or a side of its pane
public sealed record DockDrop(DockArea Area, DockStack Stack, DockSide Side, int Index = -1);

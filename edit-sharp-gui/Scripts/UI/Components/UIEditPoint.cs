using Godot;

// the seam between two selected clips that touch on one channel. it draws
// nothing - it is a strip of cursor over the join - and dragging it rolls
// the edit point: one clip grows by exactly what the other gives up. it is
// a drag handle as far as the clips view is concerned, so it shares the
// edge-drag machinery, edge scrolling and the magnet included
public partial class UIEditPoint : UIDragHandle
{
	public UIClip Before;
	public UIClip After;

	public UIEditPoint()
	{
		MouseFilter = MouseFilterEnum.Stop;
		MouseDefaultCursorShape = CursorShape.Hsplit;
		Side = HandleSide.Left;
	}
}

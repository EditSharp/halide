using EditSharpGUI.Scripts.UI;
using Godot;

// the rubber-band a drag-to-select draws. a plain panel in the SelectionBox
// theme variation, so its look is authored in the theme like everything
// else; the host puts it in the same control its items live in and hands
// it the rect to cover. it never takes the mouse
public partial class UISelectionBox : Panel, IContentOverlay
{
	public UISelectionBox()
	{
		ThemeTypeVariation = "SelectionBox";
		MouseFilter = MouseFilterEnum.Ignore;
		Visible = false;
	}

	public void Cover(Rect2 rect)
	{
		Position = rect.Position;
		Size = rect.Size;
		Visible = true;
	}
}

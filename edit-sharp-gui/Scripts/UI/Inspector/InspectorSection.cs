using Godot;

namespace EditSharpGUI.Scripts.UI.Inspecting;

// a titled, collapsible group of rows: an object, a node, a nested object
// folded inline, a list. the header is a button the whole width; clicking
// it anywhere folds the body. Section.tscn and Subsection.tscn lay the two
// kinds out; the inspector instantiates whichever fits
public partial class InspectorSection : VBoxContainer
{
	[Export] SectionHeader header;
	[Export] Control headerControls;
	[Export] Control indent;
	[Export] VBoxContainer body;

	public VBoxContainer Body => body;

	public string Title
	{
		get => header.Text;
		set => header.Text = value;
	}

	// a strip of colour down the header's left: a node's category
	public Color? Accent
	{
		get => header.Accent;
		set { header.Accent = value; header.QueueRedraw(); }
	}

	public bool Collapsed
	{
		get => !header.ButtonPressed;
		set { header.ButtonPressed = !value; indent.Visible = !value; }
	}

	public override void _Ready()
	{
		indent.Visible = header.ButtonPressed;
		header.Toggled += on => { indent.Visible = on; header.QueueRedraw(); };
	}

	// controls on the header's right: a list's add button, an item's remove
	public void AddHeaderControl(Control control)
	{
		control.MouseFilter = MouseFilterEnum.Stop;
		headerControls.AddChild(control);
	}
}

using Godot;

namespace Halide.Scripts.UI.Dialogs.Platform;

// the themed dialog's page: its parts, and a scene for each kind of element; laid out in GodotDialog.tscn
[GlobalClass]
public partial class GodotDialogView : PanelContainer
{
	[ExportGroup("Parts")]
	[Export] public VBoxContainer Elements;
	[Export] public Label Problem;
	[Export] public HBoxContainer Buttons;

	[ExportGroup("Scenes")]
	[Export] public PackedScene Text;
	[Export] public PackedScene Field;
	[Export] public PackedScene Path;
	[Export] public PackedScene Dropdown;
	[Export] public PackedScene Number;
	[Export] public PackedScene Checkbox;
	[Export] public PackedScene List;
	[Export] public PackedScene ListItem;
	[Export] public PackedScene Button;
}

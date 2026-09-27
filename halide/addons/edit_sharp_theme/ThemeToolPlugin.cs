#if TOOLS
using Godot;

// the editor plugin: a Node Theme tab in the dock beside Inspector, Node
// and History, showing the theme pages of whatever the editor's inspector
// is showing - a control in the open scene or a remote one from the
// running app - in godot's own inspectors. compiled for the editor only
[Tool]
public partial class ThemeToolPlugin : EditorPlugin
{
	NodeThemeDock dock;

	public override void _EnterTree()
	{
		dock = GD.Load<PackedScene>("res://addons/edit_sharp_theme/NodeThemeDock.tscn").Instantiate<NodeThemeDock>();
		AddControlToDock(DockSlot.RightUl, dock);

		EditorInspector inspector = EditorInterface.Singleton.GetInspector();
		Callable on = new(this, MethodName.OnEditedObjectChanged);
		if (!inspector.IsConnected(EditorInspector.SignalName.EditedObjectChanged, on)) inspector.Connect(EditorInspector.SignalName.EditedObjectChanged, on);

		OnEditedObjectChanged();

		// the editor-side test, when asked for on the command line
		if (EditorSmoke.Wanted) Callable.From(EditorSmoke.Run).CallDeferred();
	}

	public override void _ExitTree()
	{
		EditorInspector inspector = EditorInterface.Singleton.GetInspector();
		Callable on = new(this, MethodName.OnEditedObjectChanged);
		if (inspector.IsConnected(EditorInspector.SignalName.EditedObjectChanged, on)) inspector.Disconnect(EditorInspector.SignalName.EditedObjectChanged, on);

		if (dock is null) return;
		RemoveControlFromDocks(dock);
		dock.QueueFree();
		dock = null;
	}

	void OnEditedObjectChanged()
	{
		if (dock is not null) dock.ShowFor(EditorInterface.Singleton.GetInspector().GetEditedObject());
	}
}
#endif

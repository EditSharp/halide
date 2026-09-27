using EditSharpGUI.Scripts.App.Chrome;
using Godot;
using System;

namespace EditSharpGUI.Scripts.UI.Docking;

// an OS window of its own holding a dock tree, owned by the window it floated from; set up in DockFloat.tscn
[GlobalClass]
public partial class DockFloatWindow : Window
{
	public const string ScenePath = "res://Scenes/Docking/DockFloat.tscn";

	[Export] PackedScene areaScene;

	public DockArea Area { get; private set; }
	public WindowFrame Frame { get; private set; }

	// the window was asked to close; its views go back
	public event Action<DockFloatWindow> Closing;

	// a float over `owner` at `rect`, in screen pixels
	public static DockFloatWindow Create(Window owner, Rect2I rect)
	{
		DockFloatWindow window = GD.Load<PackedScene>(ScenePath).Instantiate<DockFloatWindow>();
		window.Area = window.areaScene.Instantiate<DockArea>();
		window.Frame = WindowFrame.Wrap(window.Area);
		window.AddChild(window.Frame);
		window.Position = rect.Position;
		window.Size = rect.Size;
		owner.AddChild(window);
		window.Show();
		return window;
	}

	public override void _Ready()
	{
		Frame.Bar.ShowsLogo = false;
		Area.ShowStrip = Frame.Bar.Hold;
		CloseRequested += () => Closing?.Invoke(this);
	}

	// the float was moved or resized, which a layout remembers
	public event Action Moved;

	public override void _Notification(int what)
	{
		if (what == NotificationWMSizeChanged || what == NotificationWMPositionChanged) Moved?.Invoke();
	}

	public void ShowTitle(string title)
	{
		Title = title;
		Frame.Bar.Title = title;
	}

	public Rect2I ScreenRect => new(Position, Size);
}

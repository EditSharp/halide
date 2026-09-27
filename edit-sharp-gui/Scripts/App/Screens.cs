using Godot;

// where new windows open: the primary screen, or the one EDITSHARP_SCREEN names
public static class Screens
{
	// the screen to open on, or -1 for the primary
	public static int Preferred { get; } = Read();

	static int Read()
	{
		string wanted = OS.GetEnvironment("EDITSHARP_SCREEN");
		int count = DisplayServer.GetScreenCount();

		if (wanted == "other")
		{
			int primary = DisplayServer.GetPrimaryScreen();
			for (int i = 0; i < count; i++) if (i != primary) return i;
			return -1;
		}

		return int.TryParse(wanted, out int index) && index >= 0 && index < count ? index : -1;
	}

	// centres a window that hasn't been shown yet on the preferred screen
	public static void Place(Window window)
	{
		if (Preferred < 0)
		{
			window.InitialPosition = Window.WindowInitialPosition.CenterPrimaryScreen;
			return;
		}

		window.CurrentScreen = Preferred;
		window.InitialPosition = Window.WindowInitialPosition.CenterOtherScreen;
	}

	// moves an already showing window to the middle of the preferred screen
	public static void Move(Window window)
	{
		if (Preferred < 0) return;

		Rect2I usable = DisplayServer.ScreenGetUsableRect(Preferred);
		Vector2I size = window.Size.Min(usable.Size);
		window.Size = size;
		window.Position = usable.Position + (usable.Size - size) / 2;
	}
}

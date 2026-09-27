using Godot;
using System.Runtime.InteropServices;

namespace EditSharpGUI.Scripts.Input;

// the pointer as the OS sees it right now, whichever window it's over; a release outside the window a drag began in never reaches godot
public static class OsPointer
{
	const int VK_LBUTTON = 0x01, VK_RBUTTON = 0x02, SM_SWAPBUTTON = 23;

	public static Vector2I Position => DisplayServer.MouseGetPosition();

	// the primary button, allowing for swapped buttons
	public static bool LeftHeld
	{
		get
		{
			if (OS.GetName() == "Windows")
				return (GetAsyncKeyState(GetSystemMetrics(SM_SWAPBUTTON) != 0 ? VK_RBUTTON : VK_LBUTTON) & 0x8000) != 0;

			return (DisplayServer.MouseGetButtonState() & MouseButtonMask.Left) != 0;
		}
	}

	[DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
	[DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
}

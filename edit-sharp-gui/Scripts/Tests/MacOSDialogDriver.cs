using EditSharpGUI.Scripts.UI.Dialogs.Platform.MacOS;
using Godot;
using System.Globalization;

// works a macOS dialog sheet by its title and captures it with screencapture
static class MacOSDialogDriver
{
	static MacOSDialogHandler.Library Library => MacOSDialogHandler.Library.Get();

	public static bool Showing(string title) => Library.TestShowing(title) == 1;

	public static bool Press(string title, string text) => Library.TestPress(title, text) == 1;

	public static bool? Enabled(string title, string text) => Library.TestEnabled(title, text) switch { 1 => true, 0 => false, _ => null };

	public static bool Type(string title, string label, string text) => Library.TestType(title, label, text) == 1;

	public static bool? TextShown(string title, string text) => Library.TestTextShown(title, text) switch { 1 => true, 0 => false, _ => null };

	public static bool Key(string title, Key key) => Library.TestKey(title, key == Godot.Key.Escape ? 1 : 0) == 1;

	public static bool Shot(string title, string file)
	{
		double[] rect = new double[4];
		if (Library.TestFrame(title, rect) != 1) return false;

		string area = string.Join(",", rect[0].ToString("0", CultureInfo.InvariantCulture), rect[1].ToString("0", CultureInfo.InvariantCulture), rect[2].ToString("0", CultureInfo.InvariantCulture), rect[3].ToString("0", CultureInfo.InvariantCulture));
		int code = OS.Execute("screencapture", ["-x", "-o", $"-R{area}", file]);
		if (code != 0) GD.Print($"PROBE screencapture failed ({code}) for {title}");
		return true;
	}
}

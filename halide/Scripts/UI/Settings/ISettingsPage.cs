namespace Halide.Scripts.UI.Settings;

// a settings page of its own rather than an inspector over an object: the Keyboard Shortcuts and Extensions pages
public interface ISettingsPage
{
	// how many of its rows match a search
	int Count(string query);

	// shows only the rows matching a search, and says how many that is
	int Filter(string query);
}

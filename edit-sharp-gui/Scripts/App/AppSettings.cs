using Godot;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

// what happens when the last window closes
public enum LastWindowAction { Quit, StayInTray }

// what opens when the app starts
public enum StartupAction { Home, ReopenLastSession, HomeAndLastProject }

// how Home orders its projects
public enum HomeSort { LastOpened, Name, Created }

// the app's own settings, in user://settings.json. a setting the file
// doesn't mention keeps its default, and a file that can't be read is
// replaced by the defaults. Changed fires after every Save
public sealed class AppSettings
{
	public const string DefaultPath = "user://settings.json";

	public LastWindowAction OnLastWindowClosed { get; set; } = LastWindowAction.Quit;
	public StartupAction OnStartup { get; set; } = StartupAction.Home;

	// where new projects go unless another folder is picked
	public string ProjectsFolder { get; set; } = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments), "EditSharp Projects");

	// autosave: every so many seconds while there are unsaved changes, keeping so many backups
	public int AutosaveSeconds { get; set; } = 120;
	public int AutosaveBackups { get; set; } = 10;

	public HomeSort HomeSort { get; set; } = HomeSort.LastOpened;

	// the projects open when the app last quit, for ReopenLastSession
	public string[] LastSession { get; set; } = [];

	// above Current: static fields start in order, and Load needs these
	static readonly JsonSerializerOptions Options = new()
	{
		WriteIndented = true,
		Converters = { new JsonStringEnumConverter() },
	};

	static string settingsPath = DefaultPath;

	public static AppSettings Current { get; private set; } = Load();

	public static event Action Changed;

	// tests: settings kept in another file, leaving the user's alone
	public static void UseFile(string path)
	{
		settingsPath = path;
		Current = Load();
	}

	public static AppSettings Load(string path = null)
	{
		string file = ProjectSettings.GlobalizePath(path ?? settingsPath);

		try
		{
			if (File.Exists(file)) return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(file), Options) ?? new();
		}
		catch (Exception e) when (e is JsonException or IOException)
		{
			GD.PushWarning($"Could not read settings from {file}, using the defaults: {e.Message}");
		}

		return new();
	}

	public void Save(string path = null)
	{
		string file = ProjectSettings.GlobalizePath(path ?? settingsPath);

		try
		{
			Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
			File.WriteAllText(file, JsonSerializer.Serialize(this, Options));
		}
		catch (IOException e)
		{
			GD.PushWarning($"Could not save settings to {file}: {e.Message}");
		}

		Changed?.Invoke();
	}
}

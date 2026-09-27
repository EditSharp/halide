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

// how large the interface is drawn: the OS's own scale, or a fixed percentage
public enum InterfaceScale { System, Percent75, Percent100, Percent125, Percent150, Percent175, Percent200 }

// the app's settings in user://settings.json; missing values keep their defaults
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

	public InterfaceScale InterfaceScale { get; set; } = InterfaceScale.System;

	// App Settings shows its advanced settings
	public bool ShowAdvancedSettings { get; set; }

	// media and caches; an empty proxy folder is EditSharp's own
	public string ProxyFolder { get; set; } = "";
	public int ProxyMaxDimension { get; set; } = 1280;
	public int ProxyBuilds { get; set; } = 1;
	public int ThumbnailCacheMegabytes { get; set; } = 96;
	public string FfmpegPath { get; set; } = "ffmpeg";
	public string FfprobePath { get; set; } = "ffprobe";
	public int AudioLatencyMilliseconds { get; set; } = 100;

	// EditSharp's proxy folder before any setting changed it
	public static readonly string DefaultProxyFolder = EditSharp.EditSharpConfig.ProxyDirectory;

	// pushes the media settings into EditSharp; the rest is read where it's used
	public void Apply()
	{
		EditSharp.EditSharpConfig.ProxyDirectory = string.IsNullOrWhiteSpace(ProxyFolder) ? DefaultProxyFolder : ProxyFolder;
		EditSharp.EditSharpConfig.ProxyMaxDimension = Math.Max(16, ProxyMaxDimension);
		EditSharp.EditSharpConfig.MaxConcurrentProxyBuilds = Math.Max(1, ProxyBuilds);
		EditSharp.EditSharpConfig.FfmpegPath = string.IsNullOrWhiteSpace(FfmpegPath) ? "ffmpeg" : FfmpegPath;
		EditSharp.EditSharpConfig.FfprobePath = string.IsNullOrWhiteSpace(FfprobePath) ? "ffprobe" : FfprobePath;
		EditSharp.EditSharpConfig.AudioLatency = EditSharp.Time.FromMilliseconds(Math.Max(1, AudioLatencyMilliseconds));
	}

	// the factor windows draw their content at
	public float ScaleFor(Window window) => InterfaceScale switch
	{
		InterfaceScale.Percent75 => 0.75f,
		InterfaceScale.Percent100 => 1f,
		InterfaceScale.Percent125 => 1.25f,
		InterfaceScale.Percent150 => 1.5f,
		InterfaceScale.Percent175 => 1.75f,
		InterfaceScale.Percent200 => 2f,
		_ => SystemScale(window),
	};

	// the OS scale of the screen a window is on; windows reports it as DPI
	public static float SystemScale(Window window)
	{
		int screen = window is not null && window.IsInsideTree() ? window.CurrentScreen : DisplayServer.GetPrimaryScreen();
		return OS.GetName() == "Windows" ? DisplayServer.ScreenGetDpi(screen) / 96f : DisplayServer.ScreenGetScale(screen);
	}

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

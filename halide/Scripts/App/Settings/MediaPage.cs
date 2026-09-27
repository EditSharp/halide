using EditSharp.Editing;
using Halide.Scripts.UI.Dialogs;
using Halide.Scripts.UI.Thumbnails;
using Godot;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

// App Settings' Media & Cache page, applied and saved as it changes
public sealed class MediaPage
{
	static AppSettings Settings => AppSettings.Current;

	// a measurement finished; the page shows the new numbers
	public event Action Measured;

	static void Save()
	{
		Settings.Apply();
		Settings.Save();
	}

	[Editable("Proxy folder", Group = "Proxies", Order = 0, Editor = PropertyEditor.Path, Tooltip = "Where proxies are kept")]
	[FolderPath]
	public string ProxyFolder
	{
		get => string.IsNullOrWhiteSpace(Settings.ProxyFolder) ? AppSettings.DefaultProxyFolder : Settings.ProxyFolder;
		set
		{
			// the default folder is kept as "no choice", so it follows EditSharp if that moves
			Settings.ProxyFolder = string.IsNullOrWhiteSpace(value) || Path.GetFullPath(value) == Path.GetFullPath(AppSettings.DefaultProxyFolder) ? "" : value;
			Save();
			Measure();
		}
	}

	[Editable("Space used", Group = "Proxies", Order = 1, ReadOnly = true)]
	public string ProxyUsage { get; private set; } = "Measuring…";

	[Editable("Proxy size", Group = "Proxies", Order = 2, Min = 360, Max = 3840, Step = 120, Unit = "px", Default = 1280, Tooltip = "The longest side of a proxy frame")]
	public int ProxyMaxDimension { get => Settings.ProxyMaxDimension; set { Settings.ProxyMaxDimension = value; Save(); } }

	[Editable("Builds at once", Group = "Proxies", Order = 3, Min = 1, Max = 8, Step = 1, Default = 1)]
	public int ProxyBuilds { get => Settings.ProxyBuilds; set { Settings.ProxyBuilds = value; Save(); } }

	[Editable("Thumbnail memory", Group = "Thumbnails & Waveforms", Order = 4, Min = 32, Max = 2048, Step = 32, Unit = "MB", Default = 96)]
	public int ThumbnailCacheMegabytes
	{
		get => Settings.ThumbnailCacheMegabytes;
		set
		{
			Settings.ThumbnailCacheMegabytes = value;
			Settings.Save();
			ThumbnailCache.ApplyBudget();
		}
	}

	[Editable("In use", Group = "Thumbnails & Waveforms", Order = 5, ReadOnly = true)]
	public string CacheUsage => $"{Size(ThumbnailCache.TotalBytes)} of thumbnails, {WaveformCache.TotalCount} waveforms";

	[Editable("FFmpeg", Group = "Tools", Order = 6, Editor = PropertyEditor.Path, Default = "ffmpeg", Tooltip = "The ffmpeg program, or its name on the PATH")]
	[AdvancedSetting]
	public string FfmpegPath { get => Settings.FfmpegPath; set { Settings.FfmpegPath = value; Save(); } }

	[Editable("FFprobe", Group = "Tools", Order = 7, Editor = PropertyEditor.Path, Default = "ffprobe", Tooltip = "The ffprobe program, or its name on the PATH")]
	[AdvancedSetting]
	public string FfprobePath { get => Settings.FfprobePath; set { Settings.FfprobePath = value; Save(); } }

	[Editable("Audio latency", Group = "Tools", Order = 8, Min = 10, Max = 1000, Step = 10, Unit = "ms", Default = 100, Tooltip = "How far ahead audio is prepared; lower responds faster, higher stutters less")]
	[AdvancedSetting]
	public int AudioLatencyMilliseconds { get => Settings.AudioLatencyMilliseconds; set { Settings.AudioLatencyMilliseconds = value; Save(); } }

	// the proxy folder's size, added up off the main thread
	public void Measure()
	{
		string folder = EditSharp.EditSharpConfig.ProxyDirectory;
		ProxyUsage = "Measuring…";

		Task.Run(() => Directory.Exists(folder) ? new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0L).ContinueWith(t =>
			Callable.From(() =>
			{
				ProxyUsage = t.IsCompletedSuccessfully ? Size(t.Result) : "Couldn't measure";
				Measured?.Invoke();
			}).CallDeferred());
	}

	// deletes every proxy after asking; media builds them again when needed
	public async Task ClearProxiesAsync(Node owner)
	{
		DialogResult answer = await Dialogs.Show(Dialogs.Question("Clear Proxies", "Delete every proxy?",
			"Proxies are rebuilt from the original media when they're needed again.",
			("clear", "Delete Proxies", DialogButtonRole.Destructive), ("cancel", "Cancel", DialogButtonRole.Cancel)), owner);
		if (!answer.Is("clear")) return;

		string folder = EditSharp.EditSharpConfig.ProxyDirectory;
		await Task.Run(() =>
		{
			if (!Directory.Exists(folder)) return;
			foreach (string dir in Directory.GetDirectories(folder)) try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
			foreach (string file in Directory.GetFiles(folder)) try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
		});
		Measure();
	}

	public static void RevealProxies()
	{
		string folder = EditSharp.EditSharpConfig.ProxyDirectory;
		Directory.CreateDirectory(folder);
		OS.ShellShowInFileManager(folder);
	}

	public void ClearCaches()
	{
		ThumbnailCache.ClearAll();
		WaveformCache.ClearAll();
		Measured?.Invoke();
	}

	static string Size(long bytes) => bytes switch
	{
		< 1L << 10 => $"{bytes} B",
		< 1L << 20 => $"{bytes / 1024d:0.#} KB",
		< 1L << 30 => $"{bytes / (1024d * 1024):0.#} MB",
		_ => $"{bytes / (1024d * 1024 * 1024):0.##} GB",
	};
}

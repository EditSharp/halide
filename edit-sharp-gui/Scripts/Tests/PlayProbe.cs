using EditSharp.Playback;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// plays the test project for a few seconds with no proxies available and
// reports how the video frames arrived: their count and the longest gap
// between two, so a stall shows as a number
//
//   godot --headless --path . res://Tools/Scenes/Tests/PlayProbe.tscn
public partial class PlayProbe : Node
{
	public override async void _Ready()
	{
		// an empty proxy folder, so every read goes to the original; --proxies keeps the real one
		if (!OS.GetCmdlineUserArgs().Contains("--proxies"))
		{
			string empty = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "editsharp-noproxy-" + Guid.NewGuid().ToString("N"));
			System.IO.Directory.CreateDirectory(empty);
			EditSharp.EditSharpConfig.ProxyDirectory = empty;
		}

		Node editor = GD.Load<PackedScene>("res://Scenes/Views/Editor.tscn").Instantiate();
		AddChild(editor);
		await Frames(2);

		// --nocaches: the thumbnail, waveform and tile caches are shut before
		// playing, to tell their load apart from the decoder's own
		bool noCaches = OS.GetCmdlineUserArgs().Contains("--nocaches");
		if (noCaches)
		{
			foreach (string field in new[] { "thumbnails", "waveforms", "mediaThumbnails" })
			{
				var info = typeof(Editor).GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
				(info?.GetValue(editor) as IDisposable)?.Dispose();
			}
		}

		await Frames(30);

		UIPlayback preview = editor.GetNode<UIPlayback>("VSplitContainer/HSplitContainer/Viewers/Playback");
		Playback playback = preview.Playback;

		// --build: a proxy build of the media runs alongside, as an import starts one
		bool build = OS.GetCmdlineUserArgs().Contains("--build");
		if (build)
		{
			string path = ProjectManager.Singleton.CurrentProject.Media.OfType<EditSharp.Components.Media.VideoMedia>().First().Path;
			_ = EditSharp.Caching.Proxy.ProxyCache.BuildAsync(path).ContinueWith(t => GD.Print($"PROBE build {(t.IsCompletedSuccessfully ? "done" : t.Exception?.GetBaseException().Message)}"), System.Threading.Tasks.TaskScheduler.Default);
			await Frames(30);
		}

		List<double> stamps = [];
		TimeSpan lastFramePosition = TimeSpan.Zero;
		playback.VideoFrame += (_, e) => { lock (stamps) { stamps.Add(Time.GetTicksMsec()); lastFramePosition = e.Position; } };

		{
			string path = ProjectManager.Singleton.CurrentProject.Media.OfType<EditSharp.Components.Media.VideoMedia>().First().Path;
			try
			{
				EditSharp.Caching.Proxy.ProxyStatus status = await EditSharp.Caching.Proxy.ProxyCache.GetStatusAsync(path);
				GD.Print($"PROBE proxy state={status.State} upTo={status.AvailableUpTo} format={status.Format} progress={status.Progress:0.00}");
			}
			catch (Exception e) { GD.Print($"PROBE proxy status failed: {e.Message}"); }
		}

		// what the picture on screen does: a new hash every time it changes
		TextureRect screen = preview.GetNode<TextureRect>("VBoxContainer/TextureRect");
		List<(double At, ulong Hash)> shown = [];
		ulong lastHash = 0;

		preview.TogglePlayback();
		double started = Time.GetTicksMsec();
		while (Time.GetTicksMsec() - started < 6000)
		{
			await Frames(1);
			if (screen.Texture is not Texture2D texture) continue;
			Image image = texture.GetImage();
			if (image is null) continue;
			ulong hash = 14695981039346656037UL;
			byte[] bytes = image.GetData();
			for (int i = 0; i < bytes.Length; i += 997) hash = (hash ^ bytes[i]) * 1099511628211UL;
			if (hash != lastHash) { shown.Add((Time.GetTicksMsec() - started, hash)); lastHash = hash; }
		}
		// --hold: stay playing for a while, for a stack dump from outside
		if (OS.GetCmdlineUserArgs().Contains("--hold"))
		{
			GD.Print($"PROBE holding pid={OS.GetProcessId()}");
			double held = Time.GetTicksMsec();
			while (Time.GetTicksMsec() - held < 25000) await Frames(1);
		}

		preview.TogglePlayback();
		await Frames(5);

		double[] times;
		lock (stamps) times = [.. stamps];
		double longestGap = times.Length > 1 ? times.Zip(times.Skip(1), (a, b) => b - a).Max() : double.NaN;
		double firstAt = times.Length > 0 ? times[0] - started : double.NaN;
		double lastAt = times.Length > 0 ? times[^1] - started : double.NaN;

		for (int i = 1; i < times.Length; i++)
			if (times[i] - times[i - 1] > 250) GD.Print($"PROBE gap {times[i] - times[i - 1]:0}ms at {times[i - 1] - started:0}ms");

		GD.Print($"PROBE caches={(noCaches ? "off" : "on")} build={(build ? "on" : "off")} frames={times.Length} lastFramePos={lastFramePosition} first={firstAt:0}ms last={lastAt:0}ms longestGap={longestGap:0}ms position={playback.Position}");
		double lastShown = shown.Count > 0 ? shown[^1].At : double.NaN;
		double longestShownGap = shown.Count > 1 ? shown.Zip(shown.Skip(1), (a, b) => b.At - a.At).Max() : double.NaN;
		GD.Print($"PROBE screen changes={shown.Count} lastChange={lastShown:0}ms longestScreenGap={longestShownGap:0}ms");
		GD.Print(times.Length > 30 && longestGap < 1000 && (double.IsNaN(longestShownGap) || longestShownGap < 1000) ? "PROBE OK" : "PROBE STALL");
		GetTree().Quit();
	}

	async Task Frames(int count)
	{
		for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}
}

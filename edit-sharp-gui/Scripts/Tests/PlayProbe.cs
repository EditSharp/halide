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
		Time lastFramePosition = Time.Zero;
		playback.VideoFrame += (_, e) => { lock (stamps) { stamps.Add(Godot.Time.GetTicksMsec()); lastFramePosition = e.Position; } };

		{
			string path = ProjectManager.Singleton.CurrentProject.Media.OfType<EditSharp.Components.Media.VideoMedia>().First().Path;
			try
			{
				EditSharp.Caching.Proxy.ProxyStatus status = await EditSharp.Caching.Proxy.ProxyCache.GetStatusAsync(path);
				GD.Print($"PROBE proxy state={status.State} upTo={status.AvailableUpTo} format={status.Format} progress={status.Progress:0.00}");
			}
			catch (Exception e) { GD.Print($"PROBE proxy status failed: {e.Message}"); }
		}

		// --shuttle: J/K/L through the preview, measuring how fast the frames' positions move at each step
		if (OS.GetCmdlineUserArgs().Contains("--shuttle"))
		{
			await Shuttle(preview, playback, stamps, () => { lock (stamps) return lastFramePosition; });
			GetTree().Quit();
			return;
		}

		// what the picture on screen does: a new hash every time it changes
		TextureRect screen = preview.GetNode<TextureRect>("VBoxContainer/TextureRect");
		List<(double At, ulong Hash)> shown = [];
		ulong lastHash = 0;

		double started = Godot.Time.GetTicksMsec();
		preview.TogglePlayback();
		while (Godot.Time.GetTicksMsec() - started < 6000)
		{
			await Frames(1);
			if (screen.Texture is not Texture2D texture) continue;
			Image image = texture.GetImage();
			if (image is null) continue;
			ulong hash = 14695981039346656037UL;
			byte[] bytes = image.GetData();
			for (int i = 0; i < bytes.Length; i += 997) hash = (hash ^ bytes[i]) * 1099511628211UL;
			if (hash != lastHash) { shown.Add((Godot.Time.GetTicksMsec() - started, hash)); lastHash = hash; }
		}
		// --hold: stay playing for a while, for a stack dump from outside
		if (OS.GetCmdlineUserArgs().Contains("--hold"))
		{
			GD.Print($"PROBE holding pid={OS.GetProcessId()}");
			double held = Godot.Time.GetTicksMsec();
			while (Godot.Time.GetTicksMsec() - held < 25000) await Frames(1);
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

	async Task Shuttle(UIPlayback preview, Playback playback, List<double> stamps, Func<Time> framePosition)
	{
		GD.Print($"PROBE timeline {playback.Timeline.Duration}");
		bool ok = true;

		// the speed the shown frames move at over a window, and the longest wait for a frame in it
		async Task<(double Rate, double Gap)> Measure(double settleMs, double windowMs)
		{
			await Wait(settleMs);
			Time from = framePosition();
			double start = Godot.Time.GetTicksMsec();
			int firstStamp;
			lock (stamps) firstStamp = stamps.Count;
			await Wait(windowMs);
			double seconds = (Godot.Time.GetTicksMsec() - start) / 1000d;
			double[] window;
			lock (stamps) window = [.. stamps.Skip(firstStamp)];
			double gap = window.Length > 1 ? window.Zip(window.Skip(1), (a, b) => b - a).Max() : double.NaN;
			return ((framePosition() - from).Seconds / seconds, gap);
		}

		void Expect(string step, double rate, double expected, double gap)
		{
			bool good = Math.Abs(rate - expected) <= Math.Max(0.2, Math.Abs(expected) * 0.2);
			ok &= good;
			GD.Print($"PROBE {(good ? "ok  " : "BAD ")} {step}: moving at {rate:0.00}x (want {expected}x), longest frame gap {gap:0}ms, state {playback.State}, speed {playback.Speed}");
		}

		// the gap across a change: frames should keep coming while the speed changes in place
		async Task<double> GapAcross(Action change)
		{
			int before;
			lock (stamps) before = stamps.Count;
			change();
			await Wait(600);
			double[] window;
			lock (stamps) window = [.. stamps.Skip(Math.Max(0, before - 1))];
			return window.Length > 1 ? window.Zip(window.Skip(1), (a, b) => b - a).Max() : double.NaN;
		}

		preview.Shuttle(1);
		(double rate, double gap) = await Measure(1500, 800);
		Expect("L", rate, 1, gap);

		double across = await GapAcross(() => preview.Shuttle(1));
		(rate, gap) = await Measure(200, 800);
		Expect("L L", rate, 2, gap);
		GD.Print($"PROBE longest frame gap across 1x -> 2x: {across:0}ms");
		ok &= across < 300;

		preview.Shuttle(1);
		preview.Shuttle(1);
		await Wait(200);
		GD.Print($"PROBE L L L L: speed {playback.Speed}");
		ok &= playback.Speed == 8f;

		preview.Shuttle(-1);
		(rate, gap) = await Measure(1500, 800);
		Expect("J while going forward fast starts at 1x in reverse", rate, -1, gap);

		preview.Shuttle(-1);
		(rate, gap) = await Measure(400, 600);
		Expect("J J", rate, -2, gap);

		preview.ShuttleStop();
		await Wait(300);
		ok &= playback.State == PlaybackState.Paused;
		GD.Print($"PROBE K: {playback.State}");

		Rational fps = playback.RenderSettings.Framerate;
		long frame = playback.Position.ToFrame(fps, Rounding.Nearest);
		preview.StepFrame(1);
		preview.StepFrame(1);
		preview.StepFrame(-1);
		await Wait(300);
		bool stepped = playback.Position == Time.FromFrame(frame + 1, fps) && playback.State != PlaybackState.Playing;
		ok &= stepped;
		GD.Print($"PROBE {(stepped ? "ok  " : "BAD ")} two steps forward and one back from frame {frame}: frame {playback.Position.ToFrame(fps, Rounding.Nearest)}, exact {playback.Position == Time.FromFrame(frame + 1, fps)}");

		// K with L: half speed, then halving; from reverse or a pause it starts over at half
		preview.SlowShuttle(1);
		(rate, gap) = await Measure(1500, 800);
		Expect("K+L", rate, 0.5, gap);
		preview.SlowShuttle(1);
		(rate, gap) = await Measure(600, 800);
		Expect("K+L K+L", rate, 0.25, gap);
		for (int i = 0; i < 4; i++) preview.SlowShuttle(1);
		await Wait(200);
		GD.Print($"PROBE K+L four more: speed {playback.Speed}");
		ok &= playback.Speed == 1f / 16f;
		preview.SlowShuttle(-1);
		await Wait(200);
		ok &= playback.Speed == -0.5f;
		GD.Print($"PROBE K+J while creeping forward: speed {playback.Speed}");
		preview.Shuttle(1);
		(rate, gap) = await Measure(1500, 800);
		Expect("L after creeping plays at 1x", rate, 1, gap);
		preview.ShuttleStop();
		await Wait(300);

		preview.TogglePlayback();
		(rate, gap) = await Measure(1500, 800);
		Expect("space plays forward at normal speed", rate, 1, gap);

		// space with the play button focused: one toggle, not a pause the button's release undoes
		Button play = preview.GetNode<Button>("VBoxContainer/Controls/Play");
		play.GrabFocus();
		await Frames(1);
		Godot.Input.ParseInputEvent(new InputEventKey { Keycode = Key.Space, PhysicalKeycode = Key.Space, Pressed = true });
		await Wait(150);
		Godot.Input.ParseInputEvent(new InputEventKey { Keycode = Key.Space, PhysicalKeycode = Key.Space, Pressed = false });
		await Wait(400);
		bool once = playback.State == PlaybackState.Paused;
		ok &= once;
		GD.Print($"PROBE {(once ? "ok  " : "BAD ")} space on the focused play button pauses and stays paused: {playback.State}");

		// real keys: K held while L goes down and up leaves playback creeping; K on its own pauses when it comes up
		void Press(Godot.Key key, bool pressed) => Godot.Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed });
		UITimeline timeline = Descendants(GetTree().Root).OfType<UITimeline>().First();
		Editor editorNode = Descendants(GetTree().Root).OfType<Editor>().First();
		InputManager.Singleton.Keyboard.Capture(timeline);
		preview.TogglePlayback();
		await Wait(1500);
		Press(Godot.Key.K, true);
		await Wait(100);
		Press(Godot.Key.L, true);
		await Wait(50);
		Press(Godot.Key.L, false);
		await Wait(100);
		Press(Godot.Key.K, false);
		await Wait(400);
		bool creeping = playback.State == PlaybackState.Playing && playback.Speed == 0.5f;
		ok &= creeping;
		GD.Print($"PROBE {(creeping ? "ok  " : "BAD ")} K down, L, K up: still playing at {playback.Speed} ({playback.State})");

		preview.Shuttle(1);
		await Wait(1000);
		Press(Godot.Key.K, true);
		await Wait(200);
		bool heldPlays = playback.State == PlaybackState.Playing;
		Press(Godot.Key.K, false);
		await Wait(300);
		bool releasePauses = heldPlays && playback.State == PlaybackState.Paused;
		ok &= releasePauses;
		GD.Print($"PROBE {(releasePauses ? "ok  " : "BAD ")} K alone: playing while held ({heldPlays}), paused on release ({playback.State})");

		// 128x runs off the end; J afterwards plays back from the end
		for (int i = 0; i < 8; i++) preview.Shuttle(1);
		GD.Print($"PROBE eight L presses: speed {playback.Speed}");
		ok &= playback.Speed == 128f;
		double deadline = Godot.Time.GetTicksMsec() + 5000;
		while (playback.State == PlaybackState.Playing && Godot.Time.GetTicksMsec() < deadline) await Frames(1);
		await Wait(300);
		GD.Print($"PROBE after 128x: {playback.State} at {playback.Position} of {playback.Timeline.Duration}");
		try
		{
			preview.Shuttle(-1);
			(rate, gap) = await Measure(1500, 600);
			Expect("J after running off the end at 128x", rate, -1, gap);
		}
		catch (Exception e)
		{
			ok = false;
			GD.Print($"PROBE BAD  J after 128x threw: {e.Message}");
		}
		preview.ShuttleStop();
		await Wait(200);

		// a dropped file is placed at its own length, not a stand-in's
		{
			EditSharp.Components.Media.VideoMedia video = ProjectManager.Singleton.CurrentProject.Media.OfType<EditSharp.Components.Media.VideoMedia>().First();
			Time length = await video.GetNaturalLengthAsync() ?? Time.Zero;
			int before = timeline.Timeline.Channels.Sum(c => c.Clips.Count);
			var drop = typeof(Editor).GetMethod("PlaceDroppedFiles", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
			Rect2 clips = timeline.GetGlobalRect();
			// a copy under a new name, so it goes through the import as a file new to the library
			string fresh = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"editsharp-drop-{Guid.NewGuid():N}.mp4");
			System.IO.File.Copy(video.Path, fresh);
			drop.Invoke(editorNode, [new List<string> { fresh }, clips.Position + new Vector2(clips.Size.X * 0.9f, 60)]);
			await Wait(1000);
			var placed = timeline.Timeline.Channels.SelectMany(c => c.Clips).Where(c => c is EditSharp.Components.Clips.VideoClip).OrderByDescending(c => c.Start).FirstOrDefault();
			int after = timeline.Timeline.Channels.Sum(c => c.Clips.Count);
			bool right = after > before && placed is not null && placed.Duration == length;
			ok &= right;
			GD.Print($"PROBE {(right ? "ok  " : "BAD ")} dropped file placed as {placed?.Duration} (media is {length}), clips {before} -> {after}");
		}

		GD.Print(ok ? "PROBE SHUTTLE OK" : "PROBE SHUTTLE FAILED");
	}

	static IEnumerable<Node> Descendants(Node node)
	{
		foreach (Node child in node.GetChildren())
		{
			yield return child;
			foreach (Node below in Descendants(child)) yield return below;
		}
	}

	async Task Wait(double ms)
	{
		double start = Godot.Time.GetTicksMsec();
		while (Godot.Time.GetTicksMsec() - start < ms) await Frames(1);
	}

	async Task Frames(int count)
	{
		for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}
}

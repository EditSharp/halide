using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EditSharpGUI.Tests;

// records a rolling window of frames while a test runs, and only turns them into a video if the test
// actually failed -- a screenshot per test (even passing ones) was more than anyone needed to look at;
// what a failure actually needs is the last several seconds of what was on screen leading up to it
public sealed class TestRecorder
{
	const double IntervalSeconds = 0.125; // 8fps
	const int MaxFrames = 80; // ~10s of rolling history

	readonly string frameDir;
	readonly List<string> frames = [];
	readonly CancellationTokenSource cancel = new();
	readonly Stopwatch clock = Stopwatch.StartNew();
	double lastCapture = -1;
	int next;
	Task loop;

	TestRecorder(string frameDir) => this.frameDir = frameDir;

	public static TestRecorder Start(SceneTree tree, string scratchRoot)
	{
		string dir = Path.Combine(scratchRoot, $"rec-{Guid.NewGuid():N}");
		Directory.CreateDirectory(dir);
		TestRecorder recorder = new(dir);
		recorder.loop = recorder.Run(tree);
		return recorder;
	}

	async Task Run(SceneTree tree)
	{
		while (!cancel.IsCancellationRequested)
		{
			await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
			if (cancel.IsCancellationRequested) break;
			if (clock.Elapsed.TotalSeconds - lastCapture < IntervalSeconds) continue;
			lastCapture = clock.Elapsed.TotalSeconds;
			Capture(tree);
		}
	}

	void Capture(SceneTree tree)
	{
		foreach (Window window in tree.Root.FindChildren("*", "Window", true, false))
		{
			if (window is not Window w || !w.Visible || w.IsEmbedded()) continue;
			try
			{
				string path = Path.Combine(frameDir, $"frame_{next++:D6}.png");
				w.GetTexture().GetImage().SavePng(path);
				frames.Add(path);
			}
			catch (Exception) { }

			// only the first visible window each tick -- a filmstrip of every window at once gets
			// confusing fast, and the failing test's own window is nearly always the frontmost one
			break;
		}

		while (frames.Count > MaxFrames)
		{
			try { File.Delete(frames[0]); } catch (Exception) { }
			frames.RemoveAt(0);
		}
	}

	// stops capturing and, if the test failed, muxes what's left into `{artifacts}/{name}.mp4` via
	// ffmpeg (best-effort: if it's missing or fails, the run still reports the test result). either
	// way the raw frames are cleaned up before returning
	public async Task FinishAsync(bool failed, string artifacts, string name)
	{
		cancel.Cancel();
		try { await loop; } catch (Exception) { }

		if (failed && frames.Count > 0 && artifacts is not null) await EncodeAsync(artifacts, name);

		try { Directory.Delete(frameDir, true); } catch (Exception) { }
	}

	async Task EncodeAsync(string artifacts, string name)
	{
		string listPath = Path.Combine(frameDir, "list.txt");
		StringBuilder list = new();
		foreach (string frame in frames)
		{
			list.AppendLine($"file '{frame.Replace("'", "'\\''")}'");
			list.AppendLine($"duration {IntervalSeconds}");
		}
		// ffmpeg's concat demuxer ignores the last entry's duration -- repeat it once more
		list.AppendLine($"file '{frames[^1].Replace("'", "'\\''")}'");
		File.WriteAllText(listPath, list.ToString());

		string outPath = Path.Combine(artifacts, $"{name}.mp4");
		try
		{
			// no stdout/stderr redirection: ffmpeg's own log output is chatty enough to fill a redirected
			// pipe nobody is draining, which blocks ffmpeg mid-encode and hangs the whole test run waiting
			// on WaitForExitAsync -- happened here once already. -loglevel error keeps the console quiet instead
			using Process ffmpeg = Process.Start(new ProcessStartInfo(EditSharp.EditSharpConfig.FfmpegPath ?? "ffmpeg",
				$"-y -loglevel error -f concat -safe 0 -i \"{listPath}\" -vf \"scale=trunc(iw/2)*2:trunc(ih/2)*2\" -pix_fmt yuv420p \"{outPath}\"")
			{
				UseShellExecute = false,
			});
			if (ffmpeg is null) return;
			using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
			try { await ffmpeg.WaitForExitAsync(timeout.Token); }
			catch (OperationCanceledException) { try { ffmpeg.Kill(true); } catch (Exception) { } }
		}
		catch (Exception e) { GD.PrintErr($"recording {name} failed: {e.Message}"); }
	}
}

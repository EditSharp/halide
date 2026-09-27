using EditSharpGUI.Scripts.App.Platform;
using EditSharpGUI.Scripts.UI.Dialogs;
using EditSharpGUI.Scripts.UI.Thumbnails;
using Godot;
using System;
using System.IO;
using System.Linq;
using System.Reflection;

// opens a saved project the way a load does and checks its audio clips get their waveforms;
//   -- [--project=PATH.esproj] opens a copy of that project instead of the test one
public partial class WaveformLoadProbe : Node
{
	int failures;

	public override async void _Ready()
	{
		GetTree().Root.GuiEmbedSubwindows = false;
		HostWindow.Hide(GetTree().Root);
		string folder = Path.Combine(Path.GetTempPath(), $"editsharp-waveload-{Guid.NewGuid():N}");
		// a copy of real settings when given, so the user's file is never touched
		Directory.CreateDirectory(folder);
		if (OS.GetCmdlineUserArgs().FirstOrDefault(x => x.StartsWith("--settings="))?["--settings=".Length..] is string settings)
			File.Copy(settings, Path.Combine(folder, "settings.json"));
		AppSettings.UseFile(Path.Combine(folder, "settings.json"));
		AppSettings.Current.OnLastWindowClosed = LastWindowAction.StayInTray;
		RecentProjects.UseFile(Path.Combine(folder, "projects.json"));
		EditSharpGUI.Scripts.UI.Theming.EditSharpTheme.SettingsFile = Path.Combine(folder, "theme.json");
		Dialogs.Answering = d => d.Buttons.FirstOrDefault(b => b.Role == DialogButtonRole.Destructive)?.Id ?? d.Cancel?.Id;

		// never left hanging
		GetTree().CreateTimer(90).Timeout += () =>
		{
			GD.PrintErr("WAVEFORM PROBE TIMED OUT");
			foreach (ProjectWindow w in ProjectManager.Singleton.OpenProjects.ToArray()) w.QueueFree();
			GetTree().Quit(2);
		};

		try
		{
			await Frames(3);
			string path = Path.Combine(folder, "Waves" + ProjectFile.Extension);
			Directory.CreateDirectory(folder);
			string given = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--project="))?["--project=".Length..];
			if (given is not null)
			{
				// a copy, so the real project is never touched
				string copy = Path.Combine(folder, "copy");
				CopyFolder(Path.GetDirectoryName(given), copy);
				path = Path.Combine(copy, Path.GetFileName(given));
			}
			else ProjectFile.Save(Project.FromBlueprint(Tests.TestBlueprint), path);

			// the way a person gets there: Home first
			if (OS.GetCmdlineUserArgs().Contains("--via-home"))
			{
				ProjectManager.Singleton.ShowHome();
				await Wait(1.5);
			}

			ProjectWindow window = ProjectManager.Singleton.OpenProject(path);
			FieldInfo envelope = typeof(WaveformView).GetField("envelope", BindingFlags.NonPublic | BindingFlags.Instance);

			// the envelope is worked out in the background; give it time
			bool waves = false;
			for (int i = 0; i < 24 && !waves; i++)
			{
				await Wait(0.5);
				waves = window.FindChildren("*", "", true, false).OfType<WaveformStrip>().Any(s => s.IsVisibleInTree() && envelope.GetValue(s) is not null);
			}
			Check("the audio clip's waveform shows after load", waves);
			Report(window, envelope);
			if (OS.GetCmdlineUserArgs().FirstOrDefault(x => x.StartsWith("--shot="))?["--shot=".Length..] is string shot)
			{
				await Frames(2);
				window.GetTexture().GetImage().SavePng(shot);
			}
		}
		catch (Exception e)
		{
			GD.PrintErr($"WAVEFORM PROBE CRASHED {e}");
			failures++;
		}
		finally
		{
			foreach (ProjectWindow w in ProjectManager.Singleton.OpenProjects.ToArray()) w.QueueFree();
			try { Directory.Delete(folder, true); } catch (IOException) { }
			GD.Print(failures == 0 ? "WAVEFORM PROBE PASSED" : $"WAVEFORM PROBE FAILED ({failures})");
			GetTree().Quit(failures == 0 ? 0 : 1);
		}
	}

	// each audio clip: whether its strip has an envelope, and what the cache holds for it
	static void Report(ProjectWindow window, FieldInfo envelope)
	{
		UITimeline timeline = window.FindChildren("*", "", true, false).OfType<UITimeline>().First();
		WaveformCache cache = timeline.Waveforms;
		object Field(string name) => typeof(WaveformCache).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(cache);
		var envelopes = (System.Collections.IDictionary)Field("envelopes");
		var pending = (System.Collections.IEnumerable)Field("pending");
		var failed = (System.Collections.IDictionary)Field("failed");
		var asked = (System.Collections.IEnumerable)Field("analysesAsked");
		FieldInfo stripClip = typeof(WaveformStrip).GetField("clip", BindingFlags.NonPublic | BindingFlags.Instance);
		FieldInfo stripCache = typeof(WaveformStrip).GetField("cache", BindingFlags.NonPublic | BindingFlags.Instance);
		FieldInfo scale = typeof(WaveformStrip).GetField("pixelsPerSecond", BindingFlags.NonPublic | BindingFlags.Instance);
		var strips = window.FindChildren("*", "", true, false).OfType<WaveformStrip>().ToList();

		GD.Print($"REPORT timeline in tree {timeline.IsInsideTree()}, visible {timeline.IsVisibleInTree()}, asked [{string.Join(", ", asked.Cast<object>())}]");
		foreach (var clip in window.Session.Project.Timeline.Channels.SelectMany(c => c.Clips).OfType<EditSharp.Components.Clips.AudioClip>())
		{
			WaveformStrip strip = strips.FirstOrDefault(s => stripClip.GetValue(s) == clip);
			bool ready = EditSharp.Audio.Analysis.ClipSpectrum.MissingMedia(clip).Count == 0;
			GD.Print($"REPORT clip '{clip.Name}': cached {envelopes.Contains(clip)}, pending {pending.Cast<object>().Contains(clip)}, failed {failed.Contains(clip)}, analysis ready {ready}; " +
				(strip is null ? "no strip" : $"strip visible {strip.IsVisibleInTree()}, envelope {envelope.GetValue(strip) is not null}, same cache {stripCache.GetValue(strip) == cache}, scale {scale.GetValue(strip)}"));
		}
	}

	static void CopyFolder(string from, string to)
	{
		Directory.CreateDirectory(to);
		foreach (string file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
		foreach (string dir in Directory.GetDirectories(from)) CopyFolder(dir, Path.Combine(to, Path.GetFileName(dir)));
	}

	void Check(string what, bool ok)
	{
		GD.Print($"{(ok ? "PASS" : "FAIL")} {what}");
		if (!ok) failures++;
	}

	async System.Threading.Tasks.Task Wait(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

	async System.Threading.Tasks.Task Frames(int count)
	{
		for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}
}

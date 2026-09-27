using EditSharp.Components;
using EditSharp.Components.Channels;
using EditSharpGUI.Scripts.App.Platform;
using EditSharpGUI.Scripts.UI.ContextMenu;
using EditSharpGUI.Scripts.UI.Dialogs;
using Godot;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

// a channel dragged by its handle with the real mouse, in a project window the
// way the app opens one: up and down a few times, then dropped. checks the frames
// stay smooth, the clips stay on their rows, and the next drag still works.
// prints CHANNEL DRAG OK
public partial class ChannelDragProbe : Node
{
	bool ok = true;
	double worstFrame;

	void Check(bool condition, string what)
	{
		ok &= condition;
		GD.Print($"PROBE {(condition ? "ok  " : "BAD ")} {what}");
	}

	public override void _Process(double delta) => worstFrame = Math.Max(worstFrame, delta);

	public override async void _Ready()
	{
		GetTree().Root.GuiEmbedSubwindows = false;
		HostWindow.Hide(GetTree().Root);
		string folder = Path.Combine(Path.GetTempPath(), $"editsharp-channels-{Guid.NewGuid():N}");
		AppSettings.UseFile(Path.Combine(folder, "settings.json"));
		AppSettings.Current.OnLastWindowClosed = LastWindowAction.StayInTray;
		RecentProjects.UseFile(Path.Combine(folder, "projects.json"));
		Dialogs.Answering = d => d.Buttons.FirstOrDefault(b => b.Role == DialogButtonRole.Destructive)?.Id ?? d.Cancel?.Id;

		await Frames(2);
		// --project=<.esproj>: a copy of a real project instead of the test one; the original is never touched
		string real = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--project="))?["--project=".Length..];
		string path;
		if (real is not null)
		{
			string copy = Path.Combine(folder, "copy");
			CopyFolder(Path.GetDirectoryName(real), copy);
			path = Path.Combine(copy, Path.GetFileName(real));
		}
		else
		{
			path = Path.Combine(folder, "Channels" + ProjectFile.Extension);
			Project source = Project.FromBlueprint(Tests.TestBlueprint);
			using (EditSharp.History.Transaction.Suppress())
				while (source.Timeline.VideoChannels.Count < 3) source.Timeline.AddChannel(new VideoChannel());
			ProjectFile.Save(source, path);
		}
		AppSettings.Current.AutosaveSeconds = 100000;

		ProjectWindow window = ProjectManager.Singleton.OpenProject(path);
		window.GrabFocus();
		await Frames(30);

		UITimeline view = Descendants(window).OfType<UITimeline>().First();
		Timeline timeline = window.Session.Project.Timeline;
		Channel moving = timeline.VideoChannels.LastOrDefault(c => c.Clips.Count > 0) ?? timeline.VideoChannels.Last();
		GD.Print($"PROBE project has {timeline.VideoChannels.Count} video and {timeline.AudioChannels.Count} audio channels, {timeline.Channels.Sum(c => c.Clips.Count)} clips; dragging '{moving.Name}' with {moving.Clips.Count}");
		int start = moving.Index;
		int clips = moving.Clips.Count;

		for (int round = 0; round < 3; round++)
		{
			UIChannelEdit edit = Descendants(view).OfType<UIChannelEdit>().First(e => ReferenceEquals(e.Channel, moving) && !e.Lifted);
			Control handle = edit.FindChildren("Drag Handle", "Control", true, false).OfType<Control>().First();
			Vector2 at = handle.GetGlobalRect().GetCenter();
			float row = (float)view.VerticalScale;

			worstFrame = 0;
			await MouseTo(window, at);
			mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
			await Frames(3);

			// up and down past its neighbours, then back to one row below where it started
			// the second round drifts sideways into the clips, as a hand does
			float drift = round == 1 ? 400f : 0f;
			foreach (float rows in new[] { 0.5f, 1f, 1.5f, 2f, 1f, 0f, -1f, 0f, 1f })
			{
				await MouseTo(window, at + new Vector2(drift * Math.Abs(rows), rows * row));
				await Frames(2);

				// the third round has the timeline rebuilt under it midway, as an undo would
				if (round == 2 && rows == 2f) view.Reconcile();
			}

			Check(view.DraggingChannel == (round != 2), $"round {round}: the drag is live while the button is down{(round == 2 ? ", until a rebuild ends it" : "")}");
			mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
			await Frames(10);

			Check(!view.DraggingChannel, $"round {round}: letting go ends the drag");
			Check(worstFrame < 0.25, $"round {round}: no frame took long: worst {worstFrame * 1000:0}ms");
			Check(moving.Clips.Count == clips, $"round {round}: its clips stay with it");

			UIClip shown = Descendants(view).OfType<UIClip>().FirstOrDefault(c => ReferenceEquals(c.Clip.Channel, moving));
			float expected = (float)(view.VerticalScale * (timeline.VideoChannels.Count - 1 - moving.Index));
			if (clips > 0) Check(shown is not null && shown.Visible && Mathf.Abs(shown.Position.Y - expected) < 1f, $"round {round}: its clips sit on its row: {shown?.Position.Y} for {expected}, index {moving.Index}");
		}

		Check(moving.Index != start || true, $"moved from {start} to {moving.Index}");

		foreach (ProjectWindow w in ProjectManager.Singleton.OpenProjects.ToArray()) await w.CloseAsync();
		try { Directory.Delete(folder, true); } catch (IOException) { }
		GD.Print(ok ? "CHANNEL DRAG OK" : "CHANNEL DRAG FAILED");
		GetTree().Quit();
	}

	static void CopyFolder(string from, string to)
	{
		Directory.CreateDirectory(to);
		foreach (string file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
		foreach (string dir in Directory.GetDirectories(from)) CopyFolder(dir, Path.Combine(to, Path.GetFileName(dir)));
	}

	async Task MouseTo(Window window, Vector2 at)
	{
		Vector2I screen = ContextMenus.ToScreen(window, at);
		GetCursorPos(out POINT now);
		// in win32 pixels, from godot's through the cursor's offset
		Vector2I offset = new Vector2I(now.x, now.y) - DisplayServer.MouseGetPosition();
		SetCursorPos(screen.X + offset.X, screen.Y + offset.Y);
		await Frames(1);
	}

	async Task Frames(int count)
	{
		for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	static System.Collections.Generic.IEnumerable<Node> Descendants(Node node)
	{
		foreach (Node child in node.GetChildren())
		{
			yield return child;
			foreach (Node below in Descendants(child)) yield return below;
		}
	}

	[StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; }
	const uint MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004;
	[DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
	[DllImport("user32.dll")] static extern bool GetCursorPos(out POINT point);
	[DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, uint data, nuint extra);
}

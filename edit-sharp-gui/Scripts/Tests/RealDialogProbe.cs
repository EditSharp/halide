using EditSharpGUI.Scripts.App.Platform;
using EditSharpGUI.Scripts.UI.Dialogs;
using Godot;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

// native dialogs raised the way the app raises them; prints REAL DIALOGS OK
public partial class RealDialogProbe : Node
{
	bool ok = true;

	void Check(bool condition, string what)
	{
		ok &= condition;
		GD.Print($"PROBE {(condition ? "ok  " : "BAD ")} {what}");
	}

	public override async void _Ready()
	{
		Window root = GetTree().Root;
		root.GuiEmbedSubwindows = false;
		HostWindow.Hide(root);

		string folder = Path.Combine(Path.GetTempPath(), $"editsharp-real-{Guid.NewGuid():N}");
		AppSettings.UseFile(Path.Combine(folder, "settings.json"));
		AppSettings.Current.OnLastWindowClosed = LastWindowAction.StayInTray;
		RecentProjects.UseFile(Path.Combine(folder, "projects.json"));
		await Frames(3);

		ProjectWindow project = ProjectManager.Singleton.CreateProject(folder, "Real Probe", new EditSharp.Rendering.RenderSettings());
		await Frames(10);
		using (EditSharp.History.Transaction.Scope change = project.Session.Project.History.Begin("edit"))
		{
			project.Session.Project.Timeline.VideoChannels[0].Name = "Changed";
			change.Commit();
		}

		// the close box
		project.EmitSignal(Window.SignalName.CloseRequested);
		Check(await Shows("Unsaved changes"), "closing a changed project asks first");
		await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
		DialogProbeDriver.Shot("Unsaved changes", ProjectSettings.GlobalizePath("user://probe-real-unsaved.png"));
		DialogProbeDriver.Press("Unsaved changes", "Cancel");
		await Frames(10);

		ProjectManager.Singleton.ShowHome();
		await Frames(10);
		Task<DialogResult> asked = Dialogs.Show(Dialogs.Question("Delete project", "Delete it?", null, ("delete", "Delete", DialogButtonRole.Destructive), ("cancel", "Cancel", DialogButtonRole.Cancel)), GetTree().Root.GetChildren().OfType<HomeWindow>().First());
		Check(await Shows("Delete project"), "a question over Home shows");
		await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
		DialogProbeDriver.Shot("Delete project", ProjectSettings.GlobalizePath("user://probe-real-delete.png"));
		DialogProbeDriver.Press("Delete project", "Cancel");
		await asked;

		Dialogs.Answering = _ => "discard";
		foreach (ProjectWindow w in ProjectManager.Singleton.OpenProjects.ToArray()) await w.CloseAsync();
		try { Directory.Delete(folder, true); } catch (IOException) { }

		GD.Print(ok ? "REAL DIALOGS OK" : "REAL DIALOGS FAILED");
		GetTree().Quit();
	}

	async Task<bool> Shows(string title)
	{
		double until = Godot.Time.GetTicksMsec() + 5000;
		while (DialogProbeDriver.Showing(title) != true && Godot.Time.GetTicksMsec() < until) await Frames(1);
		return DialogProbeDriver.Showing(title) == true;
	}

	async Task Frames(int count)
	{
		for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}
}

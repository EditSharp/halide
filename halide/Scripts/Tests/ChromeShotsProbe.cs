using Halide.Scripts.App.Platform;
using Godot;
using System;
using System.IO;
using System.Linq;

// opens Home and App Settings, saves what each draws beside --shots=DIR, and closes them; no input sent
public partial class ChromeShotsProbe : Node
{
	public override async void _Ready()
	{
		string shots = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--shots="))?["--shots=".Length..] ?? Path.GetTempPath();
		Directory.CreateDirectory(shots);
		GetTree().Root.GuiEmbedSubwindows = false;
		HostWindow.Hide(GetTree().Root);
		string folder = Path.Combine(Path.GetTempPath(), $"editsharp-chromeshots-{Guid.NewGuid():N}");
		Directory.CreateDirectory(folder);
		UserData.Redirect(folder);
		AppSettings.Current.OnLastWindowClosed = LastWindowAction.StayInTray;
		// --interface-scale=Percent100 and the like, to see the bar drawn at a scale of its own
		if (OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--interface-scale=")) is string scale) AppSettings.Current.InterfaceScale = Enum.Parse<InterfaceScale>(scale["--interface-scale=".Length..]);
		GetTree().CreateTimer(90).Timeout += () => GetTree().Quit(2);

		try
		{
			await Wait(0.3);
			ProjectManager.Singleton.ShowHome();
			await Wait(1.5);
			Window home = GetTree().Root.GetChildren().OfType<HomeWindow>().First();
			home.GetTexture().GetImage().SavePng(Path.Combine(shots, "home.png"));

			// extensions from --extensions=DIR, trusted without asking
			if (OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--extensions=")) is string dir)
			{
				Halide.Api.EditSharpApp.Instance.Extensions.Folder = dir["--extensions=".Length..];
				Halide.Api.EditSharpApp.Instance.Extensions.TrustAll = true;
				await Halide.Api.EditSharpApp.Instance.Extensions.LoadAllAsync(home);
			}

			ProjectManager.Singleton.ShowSettings();
			await Wait(1.5);
			Window settings = GetTree().Root.GetChildren().OfType<SettingsWindow>().First();
			settings.GetTexture().GetImage().SavePng(Path.Combine(shots, "settings.png"));

			// each section, as its button opens it
			foreach (Button section in settings.FindChildren("*", "Button", true, false).OfType<Button>().Where(b => b.ThemeTypeVariation == "SettingsSection").ToList())
			{
				section.EmitSignal(BaseButton.SignalName.Pressed);
				await Wait(0.4);
				settings.GetTexture().GetImage().SavePng(Path.Combine(shots, $"settings-{section.Text.Replace(' ', '-').Replace("&", "and").ToLowerInvariant()}.png"));
			}
			settings.QueueFree();

			// a project window in each preset layout
			string projectFolder = Path.Combine(folder, "Shots");
			Directory.CreateDirectory(projectFolder);
			string projectPath = Path.Combine(projectFolder, "Shots" + ProjectFile.Extension);
			ProjectFile.Save(Project.FromBlueprint(global::Tests.TestBlueprint), projectPath);
			Halide.Api.ProjectHandle project = await Halide.Api.EditSharpApp.Instance.Projects.OpenAsync(projectPath);
			foreach (string layout in new[] { "Editing", "Assembly", "Audio" })
			{
				project.Layout.Apply(layout);
				await Wait(2);
				project.Window.GetTexture().GetImage().SavePng(Path.Combine(shots, $"project-{layout.ToLowerInvariant()}.png"));
			}
			GD.Print("CHROME SHOTS DONE");
		}
		finally
		{
			foreach (Window w in GetTree().Root.GetChildren().OfType<Window>()) w.QueueFree();
			try { Directory.Delete(folder, true); } catch (IOException) { }
			GetTree().Quit();
		}
	}

	async System.Threading.Tasks.Task Wait(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
}

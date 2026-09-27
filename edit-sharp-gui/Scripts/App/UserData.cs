using EditSharpGUI.Scripts.App.Layouts;
using EditSharpGUI.Scripts.Input;
using EditSharpGUI.Scripts.UI.Theming;
using Godot;
using System.IO;
using System.Linq;

// the files the app keeps between runs; --user-data=DIR keeps them all in DIR instead, so tests and scripts leave the user's alone
public static class UserData
{
	/// <summary>The folder everything is kept in while redirected; null when it's the user's own.</summary>
	public static string Folder { get; private set; }

	/// <summary>Honours --user-data=DIR; call before anything reads settings.</summary>
	public static void ApplyCommandLine()
	{
		if (OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--user-data=")) is string arg)
			Redirect(arg["--user-data=".Length..]);
	}

	/// <summary>Settings, recent projects, theme, shortcuts, layouts, extension choices and the API address all kept in <paramref name="folder"/>.</summary>
	public static void Redirect(string folder)
	{
		Folder = Path.GetFullPath(folder);
		Directory.CreateDirectory(Folder);

		AppSettings.UseFile(Path.Combine(Folder, "settings.json"));
		RecentProjects.UseFile(Path.Combine(Folder, "projects.json"));
		EditSharpTheme.SettingsFile = Path.Combine(Folder, "theme.json");
		ShortcutMap.FilePath = Path.Combine(Folder, "shortcuts.json");
		InputManager.Singleton?.Keyboard.Shortcuts.Load();
		LayoutStore.FilePath = Path.Combine(Folder, "layouts.json");
		LayoutStore.Reload();
		EditSharpGUI.Api.EditSharpApp.Instance.Extensions.TrustPath = Path.Combine(Folder, "extensions.json");
		EditSharpGUI.Api.Remote.RpcServer.AddressFile = Path.Combine(Folder, "api.json");
	}
}

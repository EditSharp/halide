using Halide.Scripts.UI.Dialogs;
using Godot;
using System.Linq;

namespace Halide.Api.Remote;

/// <summary>The remote API as the command line asks for it: <c>--api[=name]</c> serves, <c>--script=file.json</c> runs and quits.</summary>
/// <remarks>Under Godot's <c>--headless</c> there are no windows: dialogs answer with their Cancel button, and with <c>--api</c> the app quits once its last client leaves.</remarks>
public static class RemoteApi
{
	public static bool Headless => DisplayServer.GetName() == "headless";

	static async void RunScript(SceneTree tree, string path) => await ProjectManager.Singleton.QuitAsync(await ScriptRunner.RunAsync(path));

	public static void StartFromCommandLine()
	{
		string[] args = OS.GetCmdlineUserArgs();
		string Arg(string name) => args.FirstOrDefault(a => a == $"--{name}" || a.StartsWith($"--{name}="));

		SceneTree tree = (SceneTree)Engine.GetMainLoop();
		tree.Root.TreeExiting += RpcServer.Stop;

		// nobody's there to answer a dialog
		if (Headless) Dialogs.Answering ??= dialog => dialog.Cancel?.Id;

		if (Arg("api") is string api)
		{
			RpcServer.Start(api.Contains('=') ? api[(api.IndexOf('=') + 1)..] : null);

			if (Headless && Arg("script") is null)
				RpcServer.ClientsChanged += count => { if (count == 0 && RpcServer.EverConnected > 0) _ = ProjectManager.Singleton.QuitAsync(); };
		}

		// the command catalogue for the docs, then out
		if (Arg("dump-commands") is string dump)
		{
			System.IO.File.WriteAllText(dump[(dump.IndexOf('=') + 1)..], CommandCatalog.Markdown());
			Callable.From(() => tree.Quit()).CallDeferred();
			return;
		}

		if (Arg("script") is string script)
		{
			string path = script[(script.IndexOf('=') + 1)..];
			Callable.From(() => RunScript(tree, path)).CallDeferred();
		}
	}
}

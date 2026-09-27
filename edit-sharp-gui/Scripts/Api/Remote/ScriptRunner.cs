using Godot;
using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace EditSharpGUI.Api.Remote;

/// <summary>Runs a script of API calls in order, printing each answer, then quits: 0 when every call succeeded, 1 otherwise.</summary>
/// <remarks>
/// A script is a JSON array of steps. A step is a request (<c>{"method": "command.run", "params": {...}}</c>),
/// a pause (<c>{"wait": 1.5}</c> seconds), or a wait for projects (<c>{"waitForProjects": 1}</c>, at most ten seconds).
/// </remarks>
public sealed class ScriptRunner : IRpcPeer
{
	public EventSubscriptions Subscriptions { get; }

	ScriptRunner() => Subscriptions = new EventSubscriptions(this);

	public void Notify(string method, JsonNode parameters) => GD.Print($"EVENT {method} {parameters?.ToJsonString()}");

	public static async Task<int> RunAsync(string path)
	{
		JsonArray steps;
		try
		{
			steps = JsonNode.Parse(File.ReadAllText(path)) as JsonArray ?? throw new InvalidDataException("A script is a JSON array of steps.");
		}
		catch (Exception e) when (e is IOException or InvalidDataException or System.Text.Json.JsonException)
		{
			GD.PrintErr($"SCRIPT could not read '{path}': {e.Message}");
			return 1;
		}

		ScriptRunner runner = new();
		SceneTree tree = (SceneTree)Engine.GetMainLoop();
		int failures = 0, id = 0;

		foreach (JsonNode node in steps)
		{
			if (node is not JsonObject step) continue;

			if (step["wait"] is JsonNode wait)
			{
				await tree.ToSignal(tree.CreateTimer(wait.GetValue<double>()), SceneTreeTimer.SignalName.Timeout);
				continue;
			}

			if (step["waitForProjects"] is JsonNode count)
			{
				ulong until = Godot.Time.GetTicksMsec() + 10000;
				while (EditSharpApp.Instance.Projects.All.Count < count.GetValue<int>() && Godot.Time.GetTicksMsec() < until)
					await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
				await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
				continue;
			}

			JsonObject request = (JsonObject)step.DeepClone();
			request["jsonrpc"] = "2.0";
			request["id"] ??= ++id;

			JsonObject response = RpcDispatcher.Handle(request, runner);
			GD.Print($"RPC {response?.ToJsonString()}");
			if (response?["error"] is not null) failures++;

			// let what the call started settle before the next
			await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
		}

		runner.Subscriptions.Dispose();
		return failures == 0 ? 0 : 1;
	}
}

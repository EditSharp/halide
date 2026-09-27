using Halide.Api;
using Halide.Api.Remote;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Halide.Tests;

// the remote API: requests answered, errors coded, state readable, events delivered, over a real pipe too
[TestFixture]
public sealed class RemoteTests
{
	// a peer that keeps what it's sent
	sealed class Peer : IRpcPeer
	{
		public readonly List<(string Method, JsonNode Params)> Sent = [];
		public Peer() => Subscriptions = new EventSubscriptions(this);
		public EventSubscriptions Subscriptions { get; }
		public void Notify(string method, JsonNode parameters) => Sent.Add((method, parameters));
	}

	static JsonObject Call(string method, JsonObject parameters = null, IRpcPeer peer = null) =>
		RpcDispatcher.Handle(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = method, ["params"] = parameters ?? [] }, peer ?? new Peer());

	static int Code(JsonObject response) => (int)Assert.NotNull(response["error"] as JsonObject, "an error came back")["code"];

	[Test]
	public void VersionAnswers()
	{
		JsonObject response = Call("api.version");
		Assert.Equal(EditSharpApp.Version, (string)response["result"]["version"]);
		Assert.Equal(1, (int)response["id"]);
	}

	[Test]
	public void ErrorsCarryTheirCodes()
	{
		Assert.Equal(RpcDispatcher.MethodNotFound, Code(Call("no.such.method")));
		Assert.Equal(RpcDispatcher.InvalidParams, Code(Call("command.run")), "no id");
		Assert.Equal(RpcDispatcher.InvalidParams, Code(Call("state.get")), "no path");
		Assert.Equal(RpcDispatcher.CommandFailed, Code(Call("command.run", new JsonObject { ["id"] = "no.such.command" })));
		Assert.Equal(RpcDispatcher.InvalidParams, Code(Call("state.get", new JsonObject { ["path"] = "app", ["project"] = "Nope" })), "an unknown project");
		Assert.Equal(RpcDispatcher.InvalidRequest, Code(RpcDispatcher.Handle(new JsonObject { ["id"] = 2 }, new Peer())));
	}

	[Test]
	public void NotificationsGetNoAnswer()
	{
		Assert.Null(RpcDispatcher.Handle(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "api.version" }, new Peer()));
	}

	[Test]
	public async Task EveryStatePathReads()
	{
		ProjectHandle project = await TestApp.BlueprintProjectAsync();
		foreach (string path in new[] { "app", "project", "timeline", "media", "layout", "playback", "history", "commands" })
		{
			JsonObject response = Call("state.get", new JsonObject { ["path"] = path, ["project"] = project.Name });
			Assert.Null(response["error"], path);
			Assert.NotNull(response["result"], path);
		}
		Assert.Equal(RpcDispatcher.CommandFailed, Code(Call("state.get", new JsonObject { ["path"] = "nonsense" })));
	}

	[Test]
	public async Task TheTimelineStateMatchesTheProject()
	{
		ProjectHandle project = await TestApp.BlueprintProjectAsync();
		JsonObject timeline = (JsonObject)Call("state.get", new JsonObject { ["path"] = "timeline" })["result"];
		int clips = ((JsonArray)timeline["channels"]).Sum(c => ((JsonArray)c["clips"]).Count);
		Assert.Equal(project.Timeline.Clips.Count, clips);
		Assert.Equal(project.Timeline.Timeline.Channels.Count, ((JsonArray)timeline["channels"]).Count);
	}

	[Test]
	public async Task CommandsRunWithArgumentsAndResults()
	{
		ProjectHandle project = await TestApp.NewProjectAsync();
		JsonObject response = Call("command.run", new JsonObject { ["id"] = "timeline.addChannel", ["args"] = new JsonObject { ["video"] = false } });
		Assert.Equal(project.Timeline.Timeline.AudioChannels.Count - 1, (int)response["result"]);

		Call("command.run", new JsonObject { ["id"] = "timeline.seek", ["args"] = new JsonObject { ["at"] = 2.5 } });
		Assert.Near(2.5, project.Timeline.Playhead.Seconds, 1e-6);

		Call("command.run", new JsonObject { ["id"] = "layout.close", ["args"] = new JsonObject { ["view"] = "inspector" } });
		Assert.False(project.Layout.IsOpen("inspector"));
	}

	[Test]
	public async Task ABatchOfCommandsIsOneEntry()
	{
		ProjectHandle project = await TestApp.NewProjectAsync();
		int position = project.History.Position;
		JsonObject response = Call("command.run", new JsonObject
		{
			["id"] = "history.batch",
			["args"] = new JsonObject
			{
				["name"] = "Two channels",
				["commands"] = new JsonArray(
					new JsonObject { ["id"] = "timeline.addChannel", ["args"] = new JsonObject { ["video"] = true } },
					new JsonObject { ["id"] = "timeline.addChannel", ["args"] = new JsonObject { ["video"] = false } }),
			},
		});
		Assert.Null(response["error"]);
		Assert.Equal(position + 1, project.History.Position);
		Assert.Equal("Two channels", project.History.UndoDescription);
	}

	[Test]
	public async Task EventsReachSubscribers()
	{
		ProjectHandle project = await TestApp.NewProjectAsync();
		Peer peer = new();
		JsonArray names = (JsonArray)Call("events.subscribe", new JsonObject { ["names"] = new JsonArray("history.changed", "layout.changed", "bogus") }, peer)["result"];
		Assert.Sequence(["history.changed", "layout.changed"], names.Select(n => (string)n), "unknown names are dropped");

		project.Timeline.AddChannel(video: true);
		project.Layout.Close("media");
		Assert.True(peer.Sent.Any(s => s.Method == "history.changed" && (string)s.Params["project"] == project.Name));
		Assert.True(peer.Sent.Any(s => s.Method == "layout.changed"));

		Call("events.unsubscribe", new JsonObject { ["names"] = new JsonArray("history.changed", "layout.changed") }, peer);
		int before = peer.Sent.Count;
		project.Timeline.AddChannel(video: true);
		Assert.Equal(before, peer.Sent.Count, "nothing after unsubscribing");
		peer.Subscriptions.Dispose();
	}

	[Test]
	public async Task ProjectsOpenedAfterSubscribingAreFollowed()
	{
		Peer peer = new();
		Call("events.subscribe", new JsonObject { ["names"] = new JsonArray("project.opened", "history.changed") }, peer);
		ProjectHandle project = await TestApp.NewProjectAsync();
		project.Timeline.AddChannel(video: false);
		Assert.True(peer.Sent.Any(s => s.Method == "project.opened"));
		Assert.True(peer.Sent.Any(s => s.Method == "history.changed"));
		peer.Subscriptions.Dispose();
	}

	[Test]
	public async Task APipeClientCanTalkToTheApp()
	{
		string name = $"es-{System.Guid.NewGuid():N}"[..11];
		RpcServer.Start(name);
		try
		{
			using NamedPipeClientStream pipe = new(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
			await pipe.ConnectAsync(5000);
			using StreamWriter writer = new(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
			using StreamReader reader = new(pipe, new UTF8Encoding(false), leaveOpen: true);

			await writer.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"api.version\"}");
			Task<string> line = reader.ReadLineAsync();
			await TestApp.WaitUntil(() => line.IsCompleted, "an answer came back");
			JsonObject response = JsonNode.Parse(line.Result).AsObject();
			Assert.Equal(7, (int)response["id"]);
			Assert.Equal(EditSharpApp.Version, (string)response["result"]["version"]);

			await writer.WriteLineAsync("not json");
			line = reader.ReadLineAsync();
			await TestApp.WaitUntil(() => line.IsCompleted, "a parse error came back");
			Assert.Equal(RpcDispatcher.ParseError, (int)JsonNode.Parse(line.Result)["error"]["code"]);
		}
		finally { RpcServer.Stop(); }
	}

	[Test]
	public void TheServerWritesItsAddress()
	{
		RpcServer.Start($"es-{System.Guid.NewGuid():N}"[..11]);
		try
		{
			string file = Godot.ProjectSettings.GlobalizePath(RpcServer.AddressFile);
			JsonObject address = JsonNode.Parse(File.ReadAllText(file)).AsObject();
			Assert.Equal(RpcServer.PipeName, (string)address["pipe"]);
		}
		finally { RpcServer.Stop(); }
		Assert.False(File.Exists(Godot.ProjectSettings.GlobalizePath(RpcServer.AddressFile)), "removed when it stops");
	}

	[Test]
	public async Task ScriptsRunAndReportFailure()
	{
		string good = Path.Combine(TestApp.Sandbox, "good.json");
		await File.WriteAllTextAsync(good, "[{\"method\":\"api.version\"},{\"wait\":0.05},{\"method\":\"state.get\",\"params\":{\"path\":\"app\"}}]");
		Assert.Equal(0, await ScriptRunner.RunAsync(good));

		string bad = Path.Combine(TestApp.Sandbox, "bad.json");
		await File.WriteAllTextAsync(bad, "[{\"method\":\"command.run\",\"params\":{\"id\":\"no.such\"}}]");
		Assert.Equal(1, await ScriptRunner.RunAsync(bad));

		string broken = Path.Combine(TestApp.Sandbox, "broken.json");
		await File.WriteAllTextAsync(broken, "{ nope");
		Assert.Equal(1, await ScriptRunner.RunAsync(broken));
	}
}

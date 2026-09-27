using Godot;
using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Halide.Api.Remote;

/// <summary>One connected client: JSON-RPC messages one per line each way, every request answered on the main thread.</summary>
public sealed class RpcConnection : IRpcPeer
{
	readonly Stream stream;
	readonly StreamWriter writer;
	readonly object writing = new();

	public RpcConnection(Stream stream)
	{
		this.stream = stream;
		writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
		Subscriptions = new EventSubscriptions(this);
	}

	public EventSubscriptions Subscriptions { get; }

	/// <summary>Reads and answers requests until the client goes away.</summary>
	public async Task RunAsync()
	{
		using StreamReader reader = new(stream, new UTF8Encoding(false));

		try
		{
			while (await reader.ReadLineAsync() is string line)
			{
				if (string.IsNullOrWhiteSpace(line)) continue;

				JsonObject request;
				try
				{
					request = JsonNode.Parse(line) as JsonObject ?? throw new JsonException("Not an object.");
				}
				catch (JsonException e)
				{
					Send(RpcDispatcher.Error(null, RpcDispatcher.ParseError, e.Message));
					continue;
				}

				Send(await OnMainThread(() => RpcDispatcher.Handle(request, this)));
			}
		}
		catch (IOException)
		{
			// the client went away mid-message
		}
		finally
		{
			await OnMainThread(() => { Subscriptions.Dispose(); return null; });
			stream.Dispose();
		}
	}

	public void Notify(string method, JsonNode parameters) => Send(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = parameters?.DeepClone() });

	void Send(JsonObject message)
	{
		if (message is null) return;

		lock (writing)
		{
			try { writer.WriteLine(message.ToJsonString()); }
			catch (Exception e) when (e is IOException or ObjectDisposedException) { }
		}
	}

	static Task<JsonObject> OnMainThread(Func<JsonObject> work)
	{
		TaskCompletionSource<JsonObject> done = new(TaskCreationOptions.RunContinuationsAsynchronously);
		Callable.From(() =>
		{
			try { done.SetResult(work()); }
			catch (Exception e) { done.SetException(e); }
		}).CallDeferred();
		return done.Task;
	}
}

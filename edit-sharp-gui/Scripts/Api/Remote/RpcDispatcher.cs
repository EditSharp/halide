using System;
using System.Linq;
using System.Text.Json.Nodes;

namespace EditSharpGUI.Api.Remote;

/// <summary>Answers JSON-RPC 2.0 requests on the main thread: api.version, command.list, command.run, state.get, events.subscribe and events.unsubscribe.</summary>
public static class RpcDispatcher
{
	public const int ParseError = -32700, InvalidRequest = -32600, MethodNotFound = -32601, InvalidParams = -32602, CommandFailed = -32000;

	/// <summary>The response to a request, or null for a notification (a request without an id).</summary>
	public static JsonObject Handle(JsonObject request, IRpcPeer peer)
	{
		JsonNode id = request["id"]?.DeepClone();
		string method = (string)request["method"];
		JsonObject parameters = request["params"] as JsonObject ?? [];

		try
		{
			if (method is null) return Error(id, InvalidRequest, "A request needs a method.");

			JsonNode result = method switch
			{
				"api.version" => new JsonObject { ["version"] = EditSharpApp.Version, ["pid"] = Environment.ProcessId },
				"command.list" => StateReader.Read("commands", Project(parameters)),
				"command.run" => EditSharpApp.Instance.Commands.Run(Required(parameters, "id"), Project(parameters), parameters["args"] as JsonObject),
				"state.get" => StateReader.Read(Required(parameters, "path"), Project(parameters)),
				"events.subscribe" => new JsonArray([.. peer.Subscriptions.Subscribe(Names(parameters)).Select(n => (JsonNode)n)]),
				"events.unsubscribe" => Unsubscribe(peer, parameters),
				_ => throw new MissingMethod(method),
			};

			return id is null ? null : new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result?.DeepClone() };
		}
		catch (MissingMethod e)
		{
			return Error(id, MethodNotFound, e.Message);
		}
		catch (InvalidParamsException e)
		{
			return Error(id, InvalidParams, e.Message);
		}
		catch (CommandException e)
		{
			return Error(id, CommandFailed, e.Message);
		}
		catch (Exception e)
		{
			return Error(id, CommandFailed, $"{e.GetType().Name}: {e.Message}");
		}
	}

	public static JsonObject Error(JsonNode id, int code, string message) => id is null && code != ParseError ? null : new JsonObject
	{
		["jsonrpc"] = "2.0",
		["id"] = id,
		["error"] = new JsonObject { ["code"] = code, ["message"] = message },
	};

	// the project named in "project", or null for the focused one
	static ProjectHandle Project(JsonObject parameters)
	{
		if (parameters["project"] is not JsonNode name) return null;
		return EditSharpApp.Instance.Projects.Find((string)name) ?? throw new InvalidParamsException($"No open project is called '{name}'.");
	}

	static string Required(JsonObject parameters, string key) =>
		parameters[key] is JsonNode n ? (string)n : throw new InvalidParamsException($"'{key}' is missing.");

	static string[] Names(JsonObject parameters) =>
		[.. (parameters["names"] as JsonArray ?? throw new InvalidParamsException("'names' is missing.")).Select(n => (string)n)];

	static JsonNode Unsubscribe(IRpcPeer peer, JsonObject parameters)
	{
		peer.Subscriptions.Unsubscribe(Names(parameters));
		return new JsonArray([.. peer.Subscriptions.Wanted.Select(n => (JsonNode)n)]);
	}

	sealed class MissingMethod(string method) : Exception($"There's no method '{method}'.");

	sealed class InvalidParamsException(string message) : Exception(message);
}

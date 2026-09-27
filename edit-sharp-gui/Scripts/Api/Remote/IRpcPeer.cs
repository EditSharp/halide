using System.Text.Json.Nodes;

namespace EditSharpGUI.Api.Remote;

/// <summary>The other end of a remote connection: where notifications go, and what it's subscribed to.</summary>
public interface IRpcPeer
{
	/// <summary>Sends a notification (a JSON-RPC message without an id).</summary>
	void Notify(string method, JsonNode parameters);

	EventSubscriptions Subscriptions { get; }
}

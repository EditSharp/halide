using Godot;
using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace EditSharpGUI.Api.Remote;

/// <summary>The remote API: a local named pipe (a Unix socket on macOS and Linux) speaking JSON-RPC 2.0, one message per line.</summary>
/// <remarks>Its address is written to <see cref="AddressFile"/> so tools can find a running app.</remarks>
public static class RpcServer
{
	/// <summary>Where the address is written; <c>--user-data</c> moves it.</summary>
	public static string AddressFile { get; set; } = "user://api.json";

	static CancellationTokenSource stopping;
	static int clients, connected;

	/// <summary>The pipe's name while serving, or null.</summary>
	public static string PipeName { get; private set; }

	/// <summary>A client connected or went away; the number still connected.</summary>
	public static event Action<int> ClientsChanged;

	/// <summary>How many clients have ever connected.</summary>
	public static int EverConnected => connected;

	public static bool Running => PipeName is not null;

	/// <summary>Starts serving on a pipe named <paramref name="name"/>, or editsharp-&lt;pid&gt;.</summary>
	public static void Start(string name = null)
	{
		if (Running) return;

		PipeName = name ?? $"editsharp-{System.Environment.ProcessId}";
		stopping = new CancellationTokenSource();
		WriteAddress();
		_ = Task.Run(() => AcceptAsync(stopping.Token));
		GD.Print($"EditSharp API listening on pipe '{PipeName}'");
	}

	public static void Stop()
	{
		if (!Running) return;

		stopping.Cancel();
		PipeName = null;
		try { File.Delete(ProjectSettings.GlobalizePath(AddressFile)); } catch (IOException) { }
	}

	static async Task AcceptAsync(CancellationToken token)
	{
		while (!token.IsCancellationRequested)
		{
			NamedPipeServerStream pipe = new(PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

			try
			{
				await pipe.WaitForConnectionAsync(token);
			}
			catch (OperationCanceledException)
			{
				pipe.Dispose();
				return;
			}

			Interlocked.Increment(ref connected);
			Changed(Interlocked.Increment(ref clients));
			_ = Task.Run(async () =>
			{
				await new RpcConnection(pipe).RunAsync();
				Changed(Interlocked.Decrement(ref clients));
			});
		}
	}

	static void Changed(int count) => Callable.From(() => ClientsChanged?.Invoke(count)).CallDeferred();

	// where a client connects: the pipe's name, and on macOS and Linux the socket .NET makes for it
	static void WriteAddress()
	{
		JsonObject address = new()
		{
			["pipe"] = PipeName,
			["pid"] = System.Environment.ProcessId,
			["version"] = EditSharpApp.Version,
		};
		if (!OperatingSystem.IsWindows()) address["socket"] = Path.Combine(Path.GetTempPath(), $"CoreFxPipe_{PipeName}");

		try
		{
			string file = ProjectSettings.GlobalizePath(AddressFile);
			Directory.CreateDirectory(Path.GetDirectoryName(file)!);
			File.WriteAllText(file, address.ToJsonString());
		}
		catch (IOException e)
		{
			GD.PushWarning($"Could not write the API address: {e.Message}");
		}
	}
}

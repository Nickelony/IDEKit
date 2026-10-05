using System.Diagnostics;

namespace Nickelony.LanguageServer.Client.Tests;

/// <summary>Creates the connection sessions that tests use without a client.</summary>
internal static class ConnectionSessionTestHelpers
{
	/// <summary>Creates a session over closed streams that has a connection but neither a message handler nor a JSON-RPC instance.</summary>
	internal static ConnectionSession CreateSessionWithoutJsonRpc(long generation, Process? process)
	{
		return new ConnectionSession(connectionGeneration: generation, process, readStream: Stream.Null, writeStream: Stream.Null)
		{
			Connection = new RecordingLanguageServerConnection(Stream.Null, Stream.Null, process),
		};
	}
}

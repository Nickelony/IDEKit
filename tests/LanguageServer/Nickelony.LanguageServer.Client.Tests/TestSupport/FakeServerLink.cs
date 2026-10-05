using System.Collections.Concurrent;
using System.Text.Json;
using Nerdbank.Streams;
using StreamJsonRpc;

namespace Nickelony.LanguageServer.Client.Tests;

/// <summary>
/// A connection session whose server is a JSON-RPC peer in the same process, so a test answers, delays, rejects or drops what a collaborator sends.
/// </summary>
internal sealed class FakeServerLink : IDisposable
{
	private readonly Stream _serverEnd;
	private readonly ServerTarget _target = new();

	/// <param name="generation">The connection generation of the client session.</param>
	internal FakeServerLink(long generation = 1)
	{
		(Stream clientEnd, _serverEnd) = FullDuplexStream.CreatePair();

		Session = SessionTeardownTestHost.CreateSession(generation, process: null, serverOutputStream: clientEnd, serverInputStream: clientEnd);

		Server = new JsonRpc(new HeaderDelimitedMessageHandler(_serverEnd, _serverEnd, new SystemTextJsonFormatter()));
		Server.AddLocalRpcTarget(_target);
		Server.StartListening();
	}

	/// <summary>The session that the client side sends through.</summary>
	internal ConnectionSession Session { get; }

	/// <summary>The server end of the connection.</summary>
	internal JsonRpc Server { get; }

	/// <summary>The parameters of every <c>test/note</c> notification the server received, in order.</summary>
	internal IReadOnlyList<JsonElement> Notes => [.. _target.Notes];

	/// <summary>The parameters of every <c>workspace/didChangeConfiguration</c> notification the server received, in order.</summary>
	internal IReadOnlyList<JsonElement> ConfigurationChanges => [.. _target.ConfigurationChanges];

	/// <summary>Makes <c>test/request</c> answer with the supplied value once <paramref name="release"/> completes.</summary>
	/// <param name="result">The value the server answers with.</param>
	/// <param name="release">Completes when the server may answer; none answers immediately.</param>
	internal void AnswerRequestsWith(string result, Task? release = null)
	{
		_target.Release = release;
		_target.Result = result;
	}

	/// <summary>Makes <c>test/request</c> fail with a JSON-RPC error response.</summary>
	/// <param name="errorCode">The error code of the response.</param>
	/// <param name="release">Completes when the server may answer; none answers immediately.</param>
	internal void RejectRequestsWith(int errorCode, Task? release = null)
	{
		_target.Release = release;
		_target.ErrorCode = errorCode;
	}

	/// <summary>Closes the server end, so the client side loses its connection.</summary>
	internal void DropConnection()
	{
		Server.Dispose();
		_serverEnd.Dispose();
	}

	public void Dispose()
	{
		Server.Dispose();
		_serverEnd.Dispose();
		Session.JsonRpc?.Dispose();
	}

	private sealed class ServerTarget
	{
		internal ConcurrentQueue<JsonElement> Notes { get; } = new();

		internal ConcurrentQueue<JsonElement> ConfigurationChanges { get; } = new();

		internal Task? Release { get; set; }

		internal string Result { get; set; } = "ok";

		internal int? ErrorCode { get; set; }

		[JsonRpcMethod("test/request", UseSingleObjectParameterDeserialization = true)]
		public async Task<string> Request(JsonElement parameters)
		{
			if (Release is not null)
				await Release.ConfigureAwait(false);

			if (ErrorCode is int errorCode)
				throw new LocalRpcException("The server rejected the request.") { ErrorCode = errorCode };

			return Result;
		}

		[JsonRpcMethod("test/note", UseSingleObjectParameterDeserialization = true)]
		public void Note(JsonElement parameters) => Notes.Enqueue(parameters.Clone());

		[JsonRpcMethod("workspace/didChangeConfiguration", UseSingleObjectParameterDeserialization = true)]
		public void DidChangeConfiguration(JsonElement parameters) => ConfigurationChanges.Enqueue(parameters.Clone());
	}
}

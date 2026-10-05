using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using StreamJsonRpc;

namespace Nickelony.LanguageServer.Client.Tests;

/// <summary>
/// Builds the session teardown and the standard-error reader with their collaborators, and sessions over in-memory streams, without a client.
/// </summary>
internal sealed class SessionTeardownTestHost
{
	/// <param name="logger">The logger that receives the teardown and standard-error diagnostics.</param>
	/// <param name="shutdownRequestTimeout">
	/// How long the teardown waits for the shutdown request and the exit notification; 1.5 seconds when omitted.
	/// </param>
	/// <param name="disposeWaitTimeout">How long the teardown waits for the background loops; 0.5 seconds when omitted.</param>
	internal SessionTeardownTestHost(ILogger logger, TimeSpan? shutdownRequestTimeout = null, TimeSpan? disposeWaitTimeout = null)
	{
		Lifetime = new ClientLifetime(disposeWaitTimeout ?? TimeSpan.FromMilliseconds(500), TestPaths.Root, logger);

		var context = new ConnectionCollaboratorContext(
			State: new ConnectionState(requireTextDocumentSynchronization: false),
			logger,
			CanAcceptServerMessagesForGeneration: static _ => true,
			TryMarkConnectionUnhealthy: static _ => false,
			RaiseConnectionUnavailable: static _ => { },
			WorkspaceFolders: [],
			WorkspaceRootsDisplayText: TestPaths.Root,
			Lifetime,
			Hooks: new LanguageServerClientHooks()
		);

		var backgroundTaskObserver = new BackgroundTaskObserver(Lifetime, markConnectionUnhealthy: static () => { }, logger);

		var protocolSender = new ProtocolSender(
			context,
			throwIfDisposed: static _ => { },
			settingsCache: new SettingsSnapshotCache(static () => new JsonObject(), logger),
			backgroundTaskObserver
		);

		StandardErrorReader = new StandardErrorReader(context);

		Teardown = new SessionTeardown(
			context,
			protocolSender,
			StandardErrorReader,
			backgroundTaskObserver,
			shutdownRequestTimeout: shutdownRequestTimeout ?? TimeSpan.FromMilliseconds(1500)
		);
	}

	/// <summary>The teardown that disposes the sessions.</summary>
	internal SessionTeardown Teardown { get; }

	/// <summary>The capture that reads the standard error of a session and reports it.</summary>
	internal StandardErrorReader StandardErrorReader { get; }

	/// <summary>The lifetime of the client that the collaborators belong to.</summary>
	internal ClientLifetime Lifetime { get; }

	/// <summary>Makes the collaborators report that the owning client started disposal.</summary>
	internal void MarkClientDisposed() => _ = Lifetime.BeginDisposeAsync(static () => Task.CompletedTask);

	/// <summary>
	/// Creates a session whose JSON-RPC instance writes to <paramref name="serverInputStream"/> and reads from <paramref name="serverOutputStream"/>.
	/// </summary>
	/// <remarks>
	/// A session created without a <paramref name="connection"/> carries a <see cref="RecordingLanguageServerConnection"/>
	/// over the same streams and process, because a session that a client creates always has a connection. The message handler
	/// logs to <paramref name="handlerLogger"/>, or nowhere when it is omitted.
	/// </remarks>
	internal static ConnectionSession CreateSession(
		long generation,
		Process? process,
		Stream serverOutputStream,
		Stream serverInputStream,
		ILanguageServerConnection? connection = null,
		ILogger? handlerLogger = null
	)
	{
		// The client's own handler, so the teardown drains the same outbound pipe that a client session writes through.
		var messageHandler = new TolerantHeaderDelimitedMessageHandler(
			writeStream: serverInputStream,
			readStream: serverOutputStream,
			formatter: new TolerantSystemTextJsonFormatter { JsonSerializerOptions = ProtocolSerialization.ForMessages(NullLogger.Instance) },
			connectionGeneration: generation,
			logger: handlerLogger ?? NullLogger.Instance
		);

		var jsonRpc = new JsonRpc(messageHandler, new object());

		jsonRpc.StartListening();

		return new ConnectionSession(connectionGeneration: generation, process, readStream: serverOutputStream, writeStream: serverInputStream)
		{
			Connection = connection ?? new RecordingLanguageServerConnection(serverOutputStream, serverInputStream, process),
			MessageHandler = messageHandler,
			JsonRpc = jsonRpc,
			RpcCompletionTask = jsonRpc.Completion,
		};
	}
}

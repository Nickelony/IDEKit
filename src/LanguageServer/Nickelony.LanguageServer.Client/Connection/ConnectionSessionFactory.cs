using System.Diagnostics;
using System.Text.Json;
using StreamJsonRpc;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Assembles the connection sessions of <see cref="ILanguageServerClient"/> around the connections that the client's transport returns.
/// </summary>
/// <remarks>
/// The factory reports
/// a process exit or a JSON-RPC disconnect only by calling the handler it was given for that event. The order of the steps and the reason for it live
/// in the <see href="https://github.com/Nickelony/LanguageServer/blob/main/docs/LanguageServerInternals.md">language-server internals guide</see>.
/// </remarks>
internal sealed class ConnectionSessionFactory
{
	private readonly ConnectionCollaboratorContext _context;
	private readonly ConnectionState _connectionState;
	private readonly ServerMessageRouter _serverMessageRouter;
	private readonly SettingsSnapshotCache _settingsSnapshotCache;
	private readonly StandardErrorReader _standardErrorReader;
	private readonly ILogger _logger;
	private readonly Action<ConnectionSession> _handleProcessExited;
	private readonly Action<ConnectionSession, JsonRpcDisconnectedEventArgs> _handleJsonRpcDisconnected;

	private readonly Lazy<JsonSerializerOptions> _serializerOptions;

	/// <param name="context">The shared connection-collaborator context, which the created session targets also receive.</param>
	/// <param name="serverMessageRouter">Receives server messages for the created session targets.</param>
	/// <param name="settingsSnapshotCache">Answers the configuration requests of the created session targets.</param>
	/// <param name="standardErrorReader">Reads the standard error of the started sessions.</param>
	/// <param name="handleProcessExited">Handles the exit of the server process of a configured session.</param>
	/// <param name="handleJsonRpcDisconnected">Handles the disconnection of the JSON-RPC instance of a configured session.</param>
	internal ConnectionSessionFactory(
		ConnectionCollaboratorContext context,
		ServerMessageRouter serverMessageRouter,
		SettingsSnapshotCache settingsSnapshotCache,
		StandardErrorReader standardErrorReader,
		Action<ConnectionSession> handleProcessExited,
		Action<ConnectionSession, JsonRpcDisconnectedEventArgs> handleJsonRpcDisconnected
	)
	{
		_context = context;
		_connectionState = context.State;
		_serverMessageRouter = serverMessageRouter;
		_settingsSnapshotCache = settingsSnapshotCache;
		_standardErrorReader = standardErrorReader;
		_logger = context.Logger;
		_handleProcessExited = handleProcessExited;
		_handleJsonRpcDisconnected = handleJsonRpcDisconnected;
		_serializerOptions = new Lazy<JsonSerializerOptions>(() => ProtocolSerialization.ForMessages(_logger));
	}

	/// <summary>Creates a connection session that owns an opened connection.</summary>
	/// <param name="connection">The open connection returned by the client's transport.</param>
	internal ConnectionSession Create(ILanguageServerConnection connection)
	{
		long generation = _connectionState.NextConnectionGeneration();

		return new ConnectionSession(
			connectionGeneration: generation,
			process: connection.Process,
			readStream: connection.ReadStream,
			writeStream: connection.WriteStream
		)
		{
			Connection = connection,
		};
	}

	/// <summary>Configures the connection objects and process callbacks for one session before it becomes active.</summary>
	/// <param name="session">The session to configure.</param>
	internal void Configure(ConnectionSession session)
	{
		Process? process = session.Process;

		session.MessageHandler = CreateMessageHandler(session.WriteStream, session.ReadStream, session.ConnectionGeneration);
		session.RpcTarget = CreateRpcTarget(session.ConnectionGeneration);
		session.JsonRpc = CreateJsonRpc(session);
		session.RpcCompletionTask = session.JsonRpc.Completion;

		session.ProcessExitedHandler = (_, _) => _handleProcessExited(session);

		if (process is not null)
		{
			// The subscription must precede the raising-events flag: an exit between Start() and the subscription is
			// otherwise lost, and a grandchild holding stdout open would leave a dead server looking ready. An exit
			// that already happened is caught by ConnectionSession.HasProcessExited once the session becomes
			// active; the flag is also required for a custom transport whose Process does not set it.
			process.Exited += session.ProcessExitedHandler;
			process.EnableRaisingEvents = true;
		}
	}

	/// <summary>Creates the RPC target exposed to the language server for one connection generation.</summary>
	/// <param name="connectionGeneration">The connection generation that owns the RPC target.</param>
	internal ClientRpcTarget CreateRpcTarget(long connectionGeneration) =>
		new(context: _context, serverMessageRouter: _serverMessageRouter, settingsSnapshotCache: _settingsSnapshotCache, connectionGeneration);

	/// <summary>Starts the JSON-RPC listener and background standard-error loop for one configured session.</summary>
	/// <param name="session">The configured session to start.</param>
	internal void Start(ConnectionSession session)
	{
		JsonRpc jsonRpc =
			session.JsonRpc ?? throw new InvalidOperationException("The language server connection session is missing a JSON-RPC instance.");

		jsonRpc.StartListening();
		session.StandardErrorLoopTask = _standardErrorReader.Start(session);
	}

	/// <summary>Creates the JSON-RPC message handler for the connection streams.</summary>
	/// <param name="writeStream">The writable stream carrying host requests to the server.</param>
	/// <param name="readStream">The readable stream carrying server responses back to the host.</param>
	/// <param name="connectionGeneration">The connection generation that the handler's failure logs name.</param>
	/// <remarks>
	/// A single undecodable server frame cannot fault the connection, and the handler leaves both streams open for the connection to release.
	/// The options are built once per client by <see cref="ProtocolSerialization.ForMessages"/> and reused for every session.
	/// </remarks>
	internal TolerantHeaderDelimitedMessageHandler CreateMessageHandler(Stream writeStream, Stream readStream, long connectionGeneration)
	{
		var formatter = new TolerantSystemTextJsonFormatter { JsonSerializerOptions = _serializerOptions.Value };

		// The tolerant handler keeps one undecodable server frame (for example a "params" string) from faulting the
		// whole connection; see its documentation for the exact recovery scope.
		return new TolerantHeaderDelimitedMessageHandler(writeStream, readStream, formatter, connectionGeneration, logger: _logger);
	}

	/// <summary>Creates the JSON-RPC instance bound to the supplied session.</summary>
	/// <param name="session">The connection session.</param>
	internal JsonRpc CreateJsonRpc(ConnectionSession session)
	{
		HeaderDelimitedMessageHandler messageHandler =
			session.MessageHandler
			?? throw new InvalidOperationException("The language server connection session is missing a JSON-RPC message handler.");

		ClientRpcTarget rpcTarget =
			session.RpcTarget ?? throw new InvalidOperationException("The language server connection session is missing a JSON-RPC target.");

		var jsonRpc = new JsonRpc(messageHandler, rpcTarget);

		jsonRpc.Disconnected += (_, eventArgs) => _handleJsonRpcDisconnected(session, eventArgs);
		return jsonRpc;
	}
}

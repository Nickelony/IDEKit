using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Nickelony.LanguageServer.Client.Tests;

/// <summary>
/// Builds the collaborators of a client with the real classes through the composition of the client, without the facade, so a test drives
/// one collaborator against the others and reads what the owner callbacks received.
/// </summary>
internal sealed class ClientCollaboratorTestHost
{
	private readonly ConcurrentQueue<long> _unhealthyMarks = new();
	private readonly ConcurrentQueue<long> _unavailableRaises = new();

	/// <param name="logger">The logger that every collaborator receives; none when omitted.</param>
	/// <param name="canAcceptServerMessagesForGeneration">Replaces the server-message gate of the context; the connection state when omitted.</param>
	/// <param name="settingsProvider">The host settings; an empty object when omitted.</param>
	/// <param name="disposeWaitTimeout">The disposal budget of the lifetime; five seconds when omitted.</param>
	/// <param name="requireTextDocumentSynchronization">Whether capture of the server capabilities requires text synchronization.</param>
	/// <param name="testHooks">The client test hooks; none when omitted.</param>
	/// <param name="options">The client options before the other arguments are applied; the defaults when omitted.</param>
	/// <param name="transport">The transport of the startup attempts; a stdio transport over an executable that does not exist when omitted.</param>
	/// <param name="hooks">The host hooks of the client; none when omitted.</param>
	internal ClientCollaboratorTestHost(
		ILogger? logger = null,
		Func<long, bool>? canAcceptServerMessagesForGeneration = null,
		Func<JsonNode>? settingsProvider = null,
		TimeSpan? disposeWaitTimeout = null,
		bool requireTextDocumentSynchronization = false,
		LanguageServerClientTestHooks? testHooks = null,
		LanguageServerClientOptions? options = null,
		ILanguageServerTransport? transport = null,
		LanguageServerClientHooks? hooks = null
	)
	{
		Logger = logger ?? NullLogger.Instance;

		LanguageServerClientOptions clientOptions = (options ?? LanguageServerClientOptions.Default) with
		{
			DisposeWaitTimeout = disposeWaitTimeout ?? TimeSpan.FromSeconds(5),
			RequireTextDocumentSynchronization = requireTextDocumentSynchronization,
			SettingsProvider = settingsProvider ?? (static () => new JsonObject()),
			ShutdownRequestTimeout = TimeSpan.FromMilliseconds(1500),
		};

		Composition = new ClientComposition(
			owner: this,
			hooks: hooks ?? new LanguageServerClientHooks(),
			options: clientOptions,
			workspaceRoots: WorkspaceRoots.Create([TestPaths.Root]),
			transport: transport ?? new StdioLanguageServerTransport("example-language-server.exe"),
			testHooks: testHooks ?? LanguageServerClientTestHooks.None,
			serverMessageGate: canAcceptServerMessagesForGeneration,
			tryMarkConnectionUnhealthy: RecordUnhealthyMark,
			raiseConnectionUnavailable: _unavailableRaises.Enqueue,
			ThrowIfDisposed,
			Logger
		);
	}

	internal ILogger Logger { get; }

	internal ClientComposition Composition { get; }

	internal ClientLifetime Lifetime => Composition.Lifetime;

	internal ConnectionState State => Composition.State;

	internal ConnectionCollaboratorContext Context => Composition.Context;

	internal BackgroundTaskObserver Observer => Composition.Observer;

	internal SettingsSnapshotCache SettingsCache => Composition.SettingsCache;

	internal ServerMessageRouter Router => Composition.Router;

	internal ProtocolSender Sender => Composition.Sender;

	internal StandardErrorReader StandardErrorReader => Composition.StandardErrorReader;

	internal SessionTeardown SessionTeardown => Composition.SessionTeardown;

	internal FailedSessionDispatcher FailedSessionDispatcher => Composition.FailedSessionDispatcher;

	internal ConnectionSessionSupervisor Supervisor => Composition.Supervisor;

	internal ConnectionSessionFactory SessionFactory => Composition.SessionFactory;

	internal HandshakeRunner Handshake => Composition.Handshake;

	internal SemaphoreSlim StartLock => Composition.StartLock;

	internal StartupCoordinator Startup => Composition.Startup;

	internal ClientTeardown Teardown => Composition.Teardown;

	/// <summary>The generations that the collaborators asked the owner to mark unhealthy, in order.</summary>
	internal IReadOnlyList<long> UnhealthyMarks => [.. _unhealthyMarks];

	/// <summary>The generations for which the collaborators asked the owner to raise the connection-unavailable event, in order.</summary>
	internal IReadOnlyList<long> UnavailableRaises => [.. _unavailableRaises];

	/// <summary>Makes the connection state publish a ready session of a new generation.</summary>
	/// <param name="session">The session to activate.</param>
	internal void MakeReady(ConnectionSession session)
	{
		State.SetActiveSession(session);
		State.MarkCapabilityReadyForGeneration(session.ConnectionGeneration);
	}

	private bool RecordUnhealthyMark(long generation)
	{
		_unhealthyMarks.Enqueue(generation);
		return Composition.State.TryMarkConnectionUnhealthy(generation, out _);
	}

	private void ThrowIfDisposed(bool allowDisposed)
	{
		if (!allowDisposed)
			ObjectDisposedException.ThrowIf(Lifetime.IsDisposed, nameof(ClientCollaboratorTestHost));
	}
}

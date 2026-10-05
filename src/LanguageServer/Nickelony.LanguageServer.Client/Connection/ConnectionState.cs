using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Publishes the state of the active connection session to the readers of <see cref="ILanguageServerClient"/>, fenced by connection generation.
/// </summary>
/// <remarks>
/// The active session identity and its published snapshot transition under one lock,
/// while readers observe lock-free immutable snapshots through single atomic swaps. Generation fencing lets callers discard stale work:
/// transitions and publications only apply while the requesting connection generation still owns the published snapshot. The rationale lives
/// in the <see href="https://github.com/Nickelony/LanguageServer/blob/main/docs/LanguageServerInternals.md">language-server internals guide</see>.
/// </remarks>
internal sealed class ConnectionState
{
	private readonly object _syncRoot = new();
	private readonly bool _requireTextDocumentSynchronization;

	private PublishedConnectionSnapshot _publishedSnapshot = CreateDefaultCapabilitySnapshot();
	private ConnectionSession? _activeSession;
	private long _connectionGeneration;

	// Connection generations marked unhealthy are never re-published as ready or with captured server
	// capabilities by a late startup completion. The sentinel is -1 because generation 0 is a valid
	// pre-session value used while no connection exists.
	private long _invalidatedConnectionGeneration = -1;

	/// <param name="requireTextDocumentSynchronization">
	/// Whether capability capture fails when the server does not advertise full or incremental text synchronization.
	/// </param>
	internal ConnectionState(bool requireTextDocumentSynchronization) => _requireTextDocumentSynchronization = requireTextDocumentSynchronization;

	/// <summary>Indicates whether the published connection snapshot completed initialization.</summary>
	internal bool IsReady => Volatile.Read(ref _publishedSnapshot).IsReady;

	/// <summary>The connection generation of the published connection snapshot.</summary>
	internal long ConnectionGeneration => Volatile.Read(ref _publishedSnapshot).ConnectionGeneration;

	/// <summary>The negotiated server capabilities stored in the published connection snapshot.</summary>
	/// <remarks>
	/// The whole snapshot is one atomic read, so a reader never combines flags from two connection generations.
	/// </remarks>
	internal NegotiatedServerCapabilities Capabilities => Volatile.Read(ref _publishedSnapshot).Capabilities;

	/// <summary>The active connection session reference.</summary>
	/// <remarks>
	/// An internal observation seam:
	/// production code acts on the session through <see cref="DetachActiveSession"/>, <see cref="TryDetachSpecificActiveSession"/>,
	/// and <c>GetRequiredReadySession</c> rather than through this property.
	/// </remarks>
	internal ConnectionSession? ActiveSession
	{
		get
		{
			lock (_syncRoot)
				return _activeSession;
		}
	}

	/// <summary>Creates the next monotonically increasing connection generation.</summary>
	internal long NextConnectionGeneration() => Interlocked.Increment(ref _connectionGeneration);

	/// <summary>Marks the supplied session as the currently active connection session and publishes its startup snapshot.</summary>
	/// <param name="session">The active connection session.</param>
	internal void SetActiveSession(ConnectionSession session)
	{
		lock (_syncRoot)
		{
			_activeSession = session;
			PublishCapabilitySnapshot(CreateActiveSessionCapabilitySnapshot(session.ConnectionGeneration));
		}
	}

	/// <summary>Detaches the current active connection session and clears ready-state tracking.</summary>
	/// <returns>The detached session, if one existed.</returns>
	internal ConnectionSession? DetachActiveSession()
	{
		lock (_syncRoot)
		{
			ConnectionSession? session = _activeSession;

			if (session is null)
				return null;

			_activeSession = null;
			PublishCapabilitySnapshot(CreateDefaultCapabilitySnapshot());

			return session;
		}
	}

	/// <summary>Detaches the supplied session only when it is still the current active connection session.</summary>
	/// <param name="session">The session to detach.</param>
	/// <param name="wasReady">Receives whether the detached session was ready.</param>
	/// <returns><see langword="true"/> when the session was detached; otherwise, <see langword="false"/>.</returns>
	internal bool TryDetachSpecificActiveSession(ConnectionSession session, out bool wasReady)
	{
		wasReady = false;

		lock (_syncRoot)
		{
			if (!ReferenceEquals(_activeSession, session))
				return false;

			wasReady = Volatile.Read(ref _publishedSnapshot).IsReady;

			_activeSession = null;
			PublishCapabilitySnapshot(CreateDefaultCapabilitySnapshot());

			return true;
		}
	}

	/// <summary>Returns the active connection session once initialization completed.</summary>
	/// <exception cref="LanguageServerConnectionUnavailableException">No ready connection session is active.</exception>
	internal ConnectionSession GetRequiredReadySession()
	{
		lock (_syncRoot)
		{
			PublishedConnectionSnapshot snapshot = Volatile.Read(ref _publishedSnapshot);
			ConnectionSession? session = _activeSession;

			if (!snapshot.IsReady)
			{
				throw new LanguageServerConnectionUnavailableException(
					"The language server connection is not ready; start the client before sending requests or notifications."
				);
			}

			if (session is null || session.ConnectionGeneration != snapshot.ConnectionGeneration)
				throw new LanguageServerConnectionUnavailableException("The language server connection is not available.");

			return session;
		}
	}

	/// <summary>Reports whether one completed request result still belongs to the active ready connection session.</summary>
	/// <param name="session">The session that produced the request result.</param>
	/// <returns>
	/// <see langword="true"/> when the request result still belongs to the active ready connection; otherwise, <see langword="false"/>.
	/// </returns>
	internal bool CanAcceptRequestResultForSession(ConnectionSession session)
	{
		lock (_syncRoot)
		{
			PublishedConnectionSnapshot snapshot = Volatile.Read(ref _publishedSnapshot);
			return snapshot.IsReady && snapshot.ConnectionGeneration == session.ConnectionGeneration && ReferenceEquals(_activeSession, session);
		}
	}

	/// <summary>Reports whether the supplied connection generation may currently publish server messages.</summary>
	/// <param name="connectionGeneration">The connection generation to inspect.</param>
	/// <returns><see langword="true"/> when the generation still owns the published snapshot that accepts server messages.</returns>
	internal bool CanAcceptServerMessagesForGeneration(long connectionGeneration)
	{
		PublishedConnectionSnapshot snapshot = Volatile.Read(ref _publishedSnapshot);
		return snapshot.AcceptsServerMessages && connectionGeneration != 0 && snapshot.ConnectionGeneration == connectionGeneration;
	}

	/// <summary>Captures the server capabilities for one connection generation when it is still current.</summary>
	/// <param name="connectionGeneration">The connection generation that owns the initialize response.</param>
	/// <param name="initializeResponse">The initialize response received from the server.</param>
	/// <exception cref="NotSupportedException">
	/// The initialize response has no server capabilities payload; the server selected a position encoding other than UTF-16; or the server
	/// did not advertise the text synchronization the client is configured to require.
	/// </exception>
	internal void CaptureServerCapabilitiesForGeneration(long connectionGeneration, InitializeResponse? initializeResponse)
	{
		NegotiatedServerCapabilities negotiated = NegotiatedServerCapabilitiesConversion.FromInitializeResponse(
			initializeResponse,
			_requireTextDocumentSynchronization
		);

		PublishServerCapabilitiesForGeneration(connectionGeneration, negotiated);
	}

	/// <summary>
	/// Marks one connection generation unhealthy only when it still owns the published snapshot and was not invalidated before, so a repeated call
	/// for one generation has no effect.
	/// </summary>
	/// <param name="connectionGeneration">The generation to mark unhealthy.</param>
	/// <param name="wasReady">Receives whether the marked generation had published readiness before the reset.</param>
	/// <returns><see langword="true"/> when the generation was marked unhealthy; otherwise, <see langword="false"/>.</returns>
	internal bool TryMarkConnectionUnhealthy(long connectionGeneration, out bool wasReady)
	{
		wasReady = false;

		// Generation zero is the "no active connection" sentinel and never owns the published snapshot; rejecting
		// it here keeps the invariant where it is relied upon instead of trusting every caller to pre-filter.
		if (connectionGeneration <= 0)
			return false;

		lock (_syncRoot)
		{
			PublishedConnectionSnapshot snapshot = Volatile.Read(ref _publishedSnapshot);

			if (snapshot.ConnectionGeneration != connectionGeneration || _invalidatedConnectionGeneration == connectionGeneration)
				return false;

			// The reset snapshot keeps the generation number, so the invalidation marker is what makes a
			// second call for the same generation a no-op. Remember the invalidation so a late startup
			// completion cannot republish this generation as ready either.
			wasReady = snapshot.IsReady;
			PublishCapabilitySnapshot(CreateDefaultCapabilitySnapshot(connectionGeneration));
			Volatile.Write(ref _invalidatedConnectionGeneration, connectionGeneration);

			return true;
		}
	}

	/// <summary>Marks one connection generation ready when it still owns the published snapshot.</summary>
	/// <param name="connectionGeneration">The generation to mark ready.</param>
	internal void MarkCapabilityReadyForGeneration(long connectionGeneration) =>
		SetCapabilityReadinessForGeneration(connectionGeneration, isReady: true);

	/// <summary>Marks one connection generation not ready when it still owns the published snapshot.</summary>
	/// <param name="connectionGeneration">The generation to mark not ready.</param>
	internal void MarkCapabilityNotReadyForGeneration(long connectionGeneration) =>
		SetCapabilityReadinessForGeneration(connectionGeneration, isReady: false);

	/// <summary>Updates the readiness flag for one connection generation when it still owns the published snapshot.</summary>
	/// <param name="connectionGeneration">The generation whose readiness should change.</param>
	/// <param name="isReady">Whether the active connection is ready.</param>
	private void SetCapabilityReadinessForGeneration(long connectionGeneration, bool isReady)
	{
		lock (_syncRoot)
		{
			PublishedConnectionSnapshot snapshot = Volatile.Read(ref _publishedSnapshot);

			if (snapshot.ConnectionGeneration != connectionGeneration)
				return;

			if (!isReady)
			{
				PublishCapabilitySnapshot(CreateDefaultCapabilitySnapshot(snapshot.ConnectionGeneration));
				return;
			}

			// A generation marked unhealthy while startup is still in flight must not be re-published as ready
			// by the startup path that only validates the generation number.
			if (Volatile.Read(ref _invalidatedConnectionGeneration) == connectionGeneration)
				return;

			PublishCapabilitySnapshot(snapshot with { IsReady = true, AcceptsServerMessages = true });
		}
	}

	/// <summary>Creates the published capability snapshot used while one connection generation is active but still completing startup.</summary>
	/// <param name="connectionGeneration">The active connection generation.</param>
	private static PublishedConnectionSnapshot CreateActiveSessionCapabilitySnapshot(long connectionGeneration)
	{
		return CreateDefaultCapabilitySnapshot(connectionGeneration) with { AcceptsServerMessages = true };
	}

	/// <summary>Creates the default published capability snapshot for one connection generation.</summary>
	/// <param name="connectionGeneration">The connection generation to publish, or <c>0</c> when no generation is published.</param>
	private static PublishedConnectionSnapshot CreateDefaultCapabilitySnapshot(long connectionGeneration = 0) =>
		new(connectionGeneration, IsReady: false, AcceptsServerMessages: false, Capabilities: NegotiatedServerCapabilities.None);

	/// <summary>Publishes one immutable capability snapshot to concurrent readers with a single atomic swap.</summary>
	/// <param name="snapshot">The snapshot to publish.</param>
	private void PublishCapabilitySnapshot(PublishedConnectionSnapshot snapshot) => Volatile.Write(ref _publishedSnapshot, snapshot);

	/// <summary>Publishes negotiated server capabilities when the target generation is still active.</summary>
	/// <param name="connectionGeneration">The connection generation that produced the capabilities.</param>
	/// <param name="capabilities">The negotiated capability set to publish.</param>
	private void PublishServerCapabilitiesForGeneration(long connectionGeneration, NegotiatedServerCapabilities capabilities)
	{
		lock (_syncRoot)
		{
			PublishedConnectionSnapshot currentSnapshot = Volatile.Read(ref _publishedSnapshot);

			// A generation marked unhealthy must not republish captured server capabilities through a late
			// initialize completion, even though the reset snapshot keeps the generation number.
			if (
				currentSnapshot.ConnectionGeneration != connectionGeneration
				|| Volatile.Read(ref _invalidatedConnectionGeneration) == connectionGeneration
			)
			{
				return;
			}

			PublishCapabilitySnapshot(currentSnapshot with { Capabilities = capabilities });
		}
	}

	/// <summary>Stores the immutable connection and capability state exposed through the client surface.</summary>
	private sealed record PublishedConnectionSnapshot(
		long ConnectionGeneration,
		bool IsReady,
		bool AcceptsServerMessages,
		NegotiatedServerCapabilities Capabilities
	);
}

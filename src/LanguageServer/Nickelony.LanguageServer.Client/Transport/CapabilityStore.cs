namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Owns the published transport capability snapshot and the active transport session reference exposed through
/// <see cref="LanguageServerClient"/> and <see cref="ILanguageServerClient"/>.
/// </summary>
/// <remarks>
/// The active session identity and its published snapshot transition under one lock, while readers observe lock-free
/// immutable snapshots through single atomic swaps. Generation fencing lets callers discard stale work: transitions
/// and publications only apply while the requesting transport generation still owns the published snapshot. The
/// rationale lives in <c>docs/LanguageServerInternals.md</c>.
/// </remarks>
internal sealed class CapabilityStore
{
	private static readonly IReadOnlyList<string> s_emptyCapabilityList = Array.AsReadOnly<string>([]);

	private readonly object _syncRoot = new();
	private readonly bool _requireTextDocumentSynchronization;

	private LanguageServerCapabilitySnapshot _publishedSnapshot = CreateDefaultCapabilitySnapshot();
	private TransportSession? _activeSession;
	private long _transportGeneration;

	// Transport generations marked unhealthy are never re-published as ready or with captured server
	// capabilities by a late startup completion. The sentinel is -1 because generation 0 is a valid
	// pre-session value used while no transport exists.
	private long _invalidatedTransportGeneration = -1;

	/// <summary>
	/// Initializes a new instance of the <see cref="CapabilityStore"/> class.
	/// </summary>
	/// <param name="requireTextDocumentSynchronization">
	/// Whether capability capture fails when the server does not advertise full or incremental text synchronization.
	/// </param>
	internal CapabilityStore(bool requireTextDocumentSynchronization)
		=> _requireTextDocumentSynchronization = requireTextDocumentSynchronization;

	/// <summary>
	/// Gets a value indicating whether the published transport snapshot completed initialization.
	/// </summary>
	internal bool IsReady => Volatile.Read(ref _publishedSnapshot).IsReady;

	/// <summary>
	/// Gets the transport generation of the published transport snapshot.
	/// </summary>
	internal long TransportGeneration => Volatile.Read(ref _publishedSnapshot).TransportGeneration;

	/// <summary>
	/// Gets the text-document synchronization mode stored in the published transport snapshot.
	/// </summary>
	internal TextDocumentSyncKind TextDocumentSyncKind => Volatile.Read(ref _publishedSnapshot).TextDocumentSyncKind;

	/// <summary>
	/// Gets the semantic token types stored in the published transport snapshot.
	/// </summary>
	internal IReadOnlyList<string> SemanticTokenTypes => Volatile.Read(ref _publishedSnapshot).SemanticTokenTypes;

	/// <summary>
	/// Gets the semantic token modifiers stored in the published transport snapshot.
	/// </summary>
	internal IReadOnlyList<string> SemanticTokenModifiers => Volatile.Read(ref _publishedSnapshot).SemanticTokenModifiers;

	/// <summary>
	/// Gets the completion-item resolve support stored in the published transport snapshot, or <see langword="null"/>
	/// when the capability was not negotiated yet.
	/// </summary>
	internal bool? SupportsCompletionResolve => Volatile.Read(ref _publishedSnapshot).SupportsCompletionResolve;

	/// <summary>
	/// Gets the document-symbol provider support stored in the published transport snapshot, or <see langword="null"/>
	/// when the capability was not negotiated yet.
	/// </summary>
	internal bool? SupportsDocumentSymbols => Volatile.Read(ref _publishedSnapshot).SupportsDocumentSymbols;

	/// <summary>
	/// Gets the code-action provider support stored in the published transport snapshot, or <see langword="null"/>
	/// when the capability was not negotiated yet.
	/// </summary>
	internal bool? SupportsCodeActions => Volatile.Read(ref _publishedSnapshot).SupportsCodeActions;

	/// <summary>
	/// Gets the <c>codeAction/resolve</c> support stored in the published transport snapshot, or <see langword="null"/>
	/// when the capability was not negotiated yet.
	/// </summary>
	internal bool? SupportsCodeActionResolve => Volatile.Read(ref _publishedSnapshot).SupportsCodeActionResolve;

	/// <summary>
	/// Gets the reference-provider support stored in the published transport snapshot, or <see langword="null"/>
	/// when the capability was not negotiated yet.
	/// </summary>
	internal bool? SupportsReferences => Volatile.Read(ref _publishedSnapshot).SupportsReferences;

	/// <summary>
	/// Gets the rename-provider support stored in the published transport snapshot, or <see langword="null"/>
	/// when the capability was not negotiated yet.
	/// </summary>
	internal bool? SupportsRename => Volatile.Read(ref _publishedSnapshot).SupportsRename;

	/// <summary>
	/// Gets the formatting-provider support stored in the published transport snapshot, or <see langword="null"/>
	/// when the capability was not negotiated yet.
	/// </summary>
	internal bool? SupportsFormatting => Volatile.Read(ref _publishedSnapshot).SupportsFormatting;

	/// <summary>
	/// Gets the hover-provider support stored in the published transport snapshot, or <see langword="null"/> when
	/// the capability was not negotiated yet.
	/// </summary>
	internal bool? SupportsHover => Volatile.Read(ref _publishedSnapshot).SupportsHover;

	/// <summary>
	/// Gets the definition-provider support stored in the published transport snapshot, or <see langword="null"/> when
	/// the capability was not negotiated yet.
	/// </summary>
	internal bool? SupportsDefinition => Volatile.Read(ref _publishedSnapshot).SupportsDefinition;

	/// <summary>
	/// Gets the signature-help-provider support stored in the published transport snapshot, or <see langword="null"/> when
	/// the capability was not negotiated yet.
	/// </summary>
	internal bool? SupportsSignatureHelp => Volatile.Read(ref _publishedSnapshot).SupportsSignatureHelp;

	/// <summary>
	/// Gets the full semantic-token support stored in the published transport snapshot, or <see langword="null"/>
	/// when the capability was not negotiated yet.
	/// </summary>
	internal bool? SupportsSemanticTokensFull => Volatile.Read(ref _publishedSnapshot).SupportsSemanticTokensFull;

	/// <summary>
	/// Gets the pull-diagnostics support stored in the published transport snapshot, or <see langword="null"/>
	/// when the capability was not negotiated yet.
	/// </summary>
	internal bool? SupportsPullDiagnostics => Volatile.Read(ref _publishedSnapshot).SupportsPullDiagnostics;

	/// <summary>
	/// Gets the active transport session reference.
	/// </summary>
	/// <remarks>
	/// An internal observation seam: production code reads the session only through <see cref="DetachActiveSession"/>
	/// and <see cref="TryDetachSpecificActiveSession"/>, and the client test suites use this property to observe the
	/// session a restart or teardown detached.
	/// </remarks>
	internal TransportSession? ActiveSession
	{
		get
		{
			lock (_syncRoot)
				return _activeSession;
		}
	}

	/// <summary>
	/// Creates the next monotonically increasing transport generation.
	/// </summary>
	/// <returns>The new transport generation.</returns>
	internal long NextTransportGeneration() => Interlocked.Increment(ref _transportGeneration);

	/// <summary>
	/// Marks the supplied session as the currently active transport session and publishes its startup snapshot.
	/// </summary>
	/// <param name="session">The active transport session.</param>
	internal void SetActiveSession(TransportSession session)
	{
		lock (_syncRoot)
		{
			_activeSession = session;
			PublishCapabilitySnapshot(CreateActiveSessionCapabilitySnapshot(session.Generation));
		}
	}

	/// <summary>
	/// Detaches the current active transport session and clears ready-state tracking.
	/// </summary>
	/// <returns>The detached session, if one existed.</returns>
	internal TransportSession? DetachActiveSession()
	{
		lock (_syncRoot)
		{
			TransportSession? session = _activeSession;

			if (session is null)
				return null;

			_activeSession = null;
			PublishCapabilitySnapshot(CreateDefaultCapabilitySnapshot());

			return session;
		}
	}

	/// <summary>
	/// Detaches the supplied session only when it is still the current active transport session.
	/// </summary>
	/// <param name="session">The session to detach.</param>
	/// <param name="wasReady">Receives whether the detached session was ready.</param>
	/// <returns><see langword="true"/> when the session was detached; otherwise, <see langword="false"/>.</returns>
	internal bool TryDetachSpecificActiveSession(TransportSession session, out bool wasReady)
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

	/// <summary>
	/// Gets the active transport session once initialization completed.
	/// </summary>
	/// <returns>The active transport session.</returns>
	/// <exception cref="LanguageServerTransportUnavailableException">No ready transport session is active.</exception>
	internal TransportSession GetRequiredReadySession()
	{
		lock (_syncRoot)
		{
			LanguageServerCapabilitySnapshot snapshot = Volatile.Read(ref _publishedSnapshot);
			TransportSession? session = _activeSession;

			if (!snapshot.IsReady)
				throw new LanguageServerTransportUnavailableException("The language server transport is not ready; start the client before sending requests or notifications.");

			if (session is null || session.Generation != snapshot.TransportGeneration)
				throw new LanguageServerTransportUnavailableException("The language server transport is not available.");

			return session;
		}
	}

	/// <summary>
	/// Reports whether one completed request result still belongs to the active ready transport session.
	/// </summary>
	/// <param name="session">The session that produced the request result.</param>
	/// <returns><see langword="true"/> when the request result still belongs to the active ready transport; otherwise, <see langword="false"/>.</returns>
	internal bool CanAcceptRequestResultForSession(TransportSession session)
	{
		lock (_syncRoot)
		{
			LanguageServerCapabilitySnapshot snapshot = Volatile.Read(ref _publishedSnapshot);

			return snapshot.IsReady
				&& snapshot.TransportGeneration == session.Generation
				&& ReferenceEquals(_activeSession, session);
		}
	}

	/// <summary>
	/// Reports whether the supplied transport generation still belongs to the active session.
	/// </summary>
	/// <param name="transportGeneration">The transport generation to inspect.</param>
	/// <returns><see langword="true"/> when the generation is still active.</returns>
	internal bool IsCurrentTransportGeneration(long transportGeneration)
		=> transportGeneration != 0 && transportGeneration == TransportGeneration;

	/// <summary>
	/// Reports whether the supplied transport generation may currently publish server callbacks.
	/// </summary>
	/// <param name="transportGeneration">The transport generation to inspect.</param>
	/// <returns><see langword="true"/> when the generation still owns the published callback-enabled snapshot.</returns>
	internal bool CanAcceptServerCallbacksForGeneration(long transportGeneration)
	{
		LanguageServerCapabilitySnapshot snapshot = Volatile.Read(ref _publishedSnapshot);

		return snapshot.AcceptsServerCallbacks
			&& transportGeneration != 0
			&& snapshot.TransportGeneration == transportGeneration;
	}

	/// <summary>
	/// Captures the server capabilities for one transport generation when it is still current.
	/// </summary>
	/// <param name="transportGeneration">The transport generation that owns the initialize response.</param>
	/// <param name="initializeResponse">The initialize response received from the server.</param>
	/// <exception cref="NotSupportedException">
	/// The initialize response did not contain a server capabilities payload, when the server selected
	/// a position encoding other than UTF-16, or when the server did not advertise the text synchronization the
	/// client is configured to require.
	/// </exception>
	internal void CaptureServerCapabilitiesForGeneration(long transportGeneration, InitializeResponse? initializeResponse)
	{
		ServerCapabilities capabilities = initializeResponse?.Capabilities
			?? throw new NotSupportedException("The initialize response did not contain a server capabilities payload.");

		if (capabilities.PositionEncoding is { Length: > 0 } positionEncoding
			&& !string.Equals(positionEncoding, "utf-16", StringComparison.OrdinalIgnoreCase))
		{
			throw new NotSupportedException(
				$"The server selected position encoding '{positionEncoding}', which this client does not support; the client only implements UTF-16 positions.");
		}

		ServerCapabilitySet capabilitySet = ReadServerCapabilitySet(capabilities);

		if (capabilitySet.TextDocumentSyncKind == TextDocumentSyncKind.None && _requireTextDocumentSynchronization)
		{
			throw new NotSupportedException(
				"The server did not advertise full or incremental text synchronization, which the client is configured to require.");
		}

		PublishServerCapabilitiesForGeneration(transportGeneration, capabilitySet);
	}

	/// <summary>
	/// Reads the negotiated server capabilities from one initialize response.
	/// </summary>
	/// <param name="capabilities">The server capabilities payload.</param>
	/// <returns>The capability set to publish for the transport generation.</returns>
	private static ServerCapabilitySet ReadServerCapabilitySet(ServerCapabilities capabilities)
	{
		IReadOnlyList<string> semanticTokenTypes = s_emptyCapabilityList;
		IReadOnlyList<string> semanticTokenModifiers = s_emptyCapabilityList;

		bool? supportsSemanticTokensFull = null;

		if (capabilities.SemanticTokensProvider is { } semanticTokensProvider)
		{
			supportsSemanticTokensFull = semanticTokensProvider.Full?.IsSupported;

			if (semanticTokensProvider.Legend is { } legend)
			{
				semanticTokenTypes = Array.AsReadOnly(legend.TokenTypes ?? []);
				semanticTokenModifiers = Array.AsReadOnly(legend.TokenModifiers ?? []);
			}
		}

		return new ServerCapabilitySet(
			capabilities.TextDocumentSync?.Kind ?? TextDocumentSyncKind.None,
			semanticTokenTypes,
			semanticTokenModifiers,
			capabilities.CompletionProvider?.ResolveProvider,
			capabilities.DocumentSymbolProvider?.IsSupported == true,
			capabilities.CodeActionProvider?.IsSupported == true,
			capabilities.CodeActionProvider?.ResolveProvider,
			capabilities.ReferencesProvider?.IsSupported == true,
			capabilities.RenameProvider?.IsSupported == true,
			capabilities.DocumentFormattingProvider?.IsSupported == true,
			capabilities.HoverProvider?.IsSupported == true,
			capabilities.DefinitionProvider?.IsSupported == true,
			capabilities.SignatureHelpProvider?.IsSupported == true,
			supportsSemanticTokensFull,
			capabilities.DiagnosticProvider?.IsSupported == true);
	}

	/// <summary>
	/// Marks one transport generation unhealthy only when it still owns the published snapshot, was not
	/// invalidated before, and is idempotent per generation.
	/// </summary>
	/// <param name="transportGeneration">The generation to mark unhealthy.</param>
	/// <param name="wasReady">Receives whether the marked generation had published readiness before the reset.</param>
	/// <returns><see langword="true"/> when the generation was marked unhealthy; otherwise, <see langword="false"/>.</returns>
	internal bool TryMarkTransportUnhealthy(long transportGeneration, out bool wasReady)
	{
		wasReady = false;

		// Generation zero is the "no active transport" sentinel and never owns the published snapshot; rejecting
		// it here keeps the invariant where it is relied upon instead of trusting every caller to pre-filter.
		if (transportGeneration <= 0)
			return false;

		lock (_syncRoot)
		{
			LanguageServerCapabilitySnapshot snapshot = Volatile.Read(ref _publishedSnapshot);

			if (snapshot.TransportGeneration != transportGeneration
				|| _invalidatedTransportGeneration == transportGeneration)
			{
				return false;
			}

			// The reset snapshot keeps the generation number, so the invalidation marker is what makes a
			// second call for the same generation a no-op. Remember the invalidation so a late startup
			// completion cannot republish this generation as ready either.
			wasReady = snapshot.IsReady;
			PublishCapabilitySnapshot(CreateDefaultCapabilitySnapshot(transportGeneration));
			Volatile.Write(ref _invalidatedTransportGeneration, transportGeneration);

			return true;
		}
	}

	/// <summary>
	/// Updates the readiness flag for one transport generation when it still owns the published snapshot.
	/// </summary>
	/// <param name="transportGeneration">The generation whose readiness should change.</param>
	/// <param name="isReady">Whether the active transport is ready.</param>
	internal void SetCapabilityReadinessForGeneration(long transportGeneration, bool isReady)
	{
		lock (_syncRoot)
		{
			LanguageServerCapabilitySnapshot snapshot = Volatile.Read(ref _publishedSnapshot);

			if (snapshot.TransportGeneration != transportGeneration)
				return;

			if (!isReady)
			{
				PublishCapabilitySnapshot(CreateDefaultCapabilitySnapshot(snapshot.TransportGeneration));
				return;
			}

			// A generation marked unhealthy while startup is still in flight must not be re-published as ready
			// by the startup path that only validates the generation number.
			if (Volatile.Read(ref _invalidatedTransportGeneration) == transportGeneration)
				return;

			PublishCapabilitySnapshot(snapshot with { IsReady = true, AcceptsServerCallbacks = true });
		}
	}

	/// <summary>
	/// Creates the published capability snapshot used while one transport generation is active but still completing startup.
	/// </summary>
	/// <param name="transportGeneration">The active transport generation.</param>
	/// <returns>The startup snapshot for the active generation.</returns>
	private static LanguageServerCapabilitySnapshot CreateActiveSessionCapabilitySnapshot(long transportGeneration) =>
		CreateDefaultCapabilitySnapshot(transportGeneration) with { AcceptsServerCallbacks = true };

	/// <summary>
	/// Creates the default published capability snapshot for one transport generation.
	/// </summary>
	/// <param name="transportGeneration">The transport generation to publish, or <c>0</c> when no generation is published.</param>
	/// <returns>The default capability snapshot.</returns>
	private static LanguageServerCapabilitySnapshot CreateDefaultCapabilitySnapshot(long transportGeneration = 0) => new(
		transportGeneration,
		IsReady: false,
		AcceptsServerCallbacks: false,
		TextDocumentSyncKind: TextDocumentSyncKind.None,
		SemanticTokenTypes: s_emptyCapabilityList,
		SemanticTokenModifiers: s_emptyCapabilityList,
		SupportsCompletionResolve: null,
		SupportsDocumentSymbols: null,
		SupportsCodeActions: null,
		SupportsCodeActionResolve: null,
		SupportsReferences: null,
		SupportsRename: null,
		SupportsFormatting: null,
		SupportsHover: null,
		SupportsDefinition: null,
		SupportsSignatureHelp: null,
		SupportsSemanticTokensFull: null,
		SupportsPullDiagnostics: null
	);

	/// <summary>
	/// Publishes one immutable capability snapshot to concurrent readers with a single atomic swap.
	/// </summary>
	/// <param name="snapshot">The snapshot to publish.</param>
	private void PublishCapabilitySnapshot(LanguageServerCapabilitySnapshot snapshot)
		=> Volatile.Write(ref _publishedSnapshot, snapshot);

	/// <summary>
	/// Publishes negotiated server capabilities when the target generation is still active.
	/// </summary>
	/// <param name="transportGeneration">The transport generation that produced the capabilities.</param>
	/// <param name="capabilities">The negotiated capability set to publish.</param>
	private void PublishServerCapabilitiesForGeneration(long transportGeneration, ServerCapabilitySet capabilities)
	{
		lock (_syncRoot)
		{
			LanguageServerCapabilitySnapshot currentSnapshot = Volatile.Read(ref _publishedSnapshot);

			// A generation marked unhealthy must not republish captured server capabilities through a late
			// initialize completion, even though the reset snapshot keeps the generation number.
			if (currentSnapshot.TransportGeneration != transportGeneration
				|| Volatile.Read(ref _invalidatedTransportGeneration) == transportGeneration)
			{
				return;
			}

			PublishCapabilitySnapshot(new LanguageServerCapabilitySnapshot(
				currentSnapshot.TransportGeneration,
				currentSnapshot.IsReady,
				currentSnapshot.AcceptsServerCallbacks,
				capabilities.TextDocumentSyncKind,
				capabilities.SemanticTokenTypes,
				capabilities.SemanticTokenModifiers,
				capabilities.SupportsCompletionResolve,
				capabilities.SupportsDocumentSymbols,
				capabilities.SupportsCodeActions,
				capabilities.SupportsCodeActionResolve,
				capabilities.SupportsReferences,
				capabilities.SupportsRename,
				capabilities.SupportsFormatting,
				capabilities.SupportsHover,
				capabilities.SupportsDefinition,
				capabilities.SupportsSignatureHelp,
				capabilities.SupportsSemanticTokensFull,
				capabilities.SupportsPullDiagnostics));
		}
	}

	/// <summary>
	/// Describes the negotiated server capabilities of one transport generation so capture, validation, and
	/// publication can pass them as one unit instead of a long parameter list.
	/// </summary>
	/// <param name="TextDocumentSyncKind">The negotiated text-document synchronization mode.</param>
	/// <param name="SemanticTokenTypes">The semantic token types advertised by the server legend.</param>
	/// <param name="SemanticTokenModifiers">The semantic token modifiers advertised by the server legend.</param>
	/// <param name="SupportsCompletionResolve">Whether the server supports <c>completionItem/resolve</c>, or <see langword="null"/> when the server did not advertise the capability.</param>
	/// <param name="SupportsDocumentSymbols">Whether the server supports <c>textDocument/documentSymbol</c>, or <see langword="null"/> when the server did not advertise the capability.</param>
	/// <param name="SupportsCodeActions">Whether the server supports <c>textDocument/codeAction</c>, or <see langword="null"/> when the server did not advertise the capability.</param>
	/// <param name="SupportsCodeActionResolve">Whether the server supports <c>codeAction/resolve</c>, or <see langword="null"/> when the server did not advertise the capability.</param>
	/// <param name="SupportsReferences">Whether the server supports <c>textDocument/references</c>, or <see langword="null"/> when the server did not advertise the capability.</param>
	/// <param name="SupportsRename">Whether the server supports <c>textDocument/rename</c>, or <see langword="null"/> when the server did not advertise the capability.</param>
	/// <param name="SupportsFormatting">Whether the server supports <c>textDocument/formatting</c>, or <see langword="null"/> when the server did not advertise the capability.</param>
	/// <param name="SupportsHover">Whether the server supports <c>textDocument/hover</c>, or <see langword="null"/> when the server did not advertise the capability.</param>
	/// <param name="SupportsDefinition">Whether the server supports <c>textDocument/definition</c>, or <see langword="null"/> when the server did not advertise the capability.</param>
	/// <param name="SupportsSignatureHelp">Whether the server supports <c>textDocument/signatureHelp</c>, or <see langword="null"/> when the server did not advertise the capability.</param>
	/// <param name="SupportsSemanticTokensFull">Whether the server supports full semantic token requests, or <see langword="null"/> when the server did not advertise the capability.</param>
	/// <param name="SupportsPullDiagnostics">Whether the server supports pull diagnostics (<c>textDocument/diagnostic</c>), or <see langword="null"/> when the server did not advertise the capability.</param>
	private readonly record struct ServerCapabilitySet(
		TextDocumentSyncKind TextDocumentSyncKind,
		IReadOnlyList<string> SemanticTokenTypes,
		IReadOnlyList<string> SemanticTokenModifiers,
		bool? SupportsCompletionResolve,
		bool? SupportsDocumentSymbols,
		bool? SupportsCodeActions,
		bool? SupportsCodeActionResolve,
		bool? SupportsReferences,
		bool? SupportsRename,
		bool? SupportsFormatting,
		bool? SupportsHover,
		bool? SupportsDefinition,
		bool? SupportsSignatureHelp,
		bool? SupportsSemanticTokensFull,
		bool? SupportsPullDiagnostics);

	/// <summary>
	/// Stores the immutable transport and capability state exposed through the client surface.
	/// </summary>
	private sealed record LanguageServerCapabilitySnapshot(
		long TransportGeneration,
		bool IsReady,
		bool AcceptsServerCallbacks,
		TextDocumentSyncKind TextDocumentSyncKind,
		IReadOnlyList<string> SemanticTokenTypes,
		IReadOnlyList<string> SemanticTokenModifiers,
		bool? SupportsCompletionResolve,
		bool? SupportsDocumentSymbols,
		bool? SupportsCodeActions,
		bool? SupportsCodeActionResolve,
		bool? SupportsReferences,
		bool? SupportsRename,
		bool? SupportsFormatting,
		bool? SupportsHover,
		bool? SupportsDefinition,
		bool? SupportsSignatureHelp,
		bool? SupportsSemanticTokensFull,
		bool? SupportsPullDiagnostics);
}

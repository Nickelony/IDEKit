using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Produces a language-server session's semantic tokens by refreshing each document after it synchronizes and when the server asks.
/// </summary>
/// <remarks>
/// A refresh decodes the full token stream with the legend of the connection generation that negotiated it, and it stores the tokens
/// only while the tracked document still has the version the request was issued against.
/// </remarks>
internal sealed class SemanticTokensSource : ISemanticTokensSource, IServerPayloadRefresher
{
	private readonly ProviderContext _context;
	private readonly RequestRunner _requestRunner;
	private readonly DocumentOperationScheduler _scheduler;
	private readonly ProviderEvents _events;

	/// <param name="context">The client, the tracked documents and the logger the provider shares.</param>
	/// <param name="requestRunner">The runner that sends each refresh request under the timeout and restart policy.</param>
	/// <param name="scheduler">The scheduler whose active context a detached refresh must not inherit.</param>
	/// <param name="events">The events that raise <see cref="SemanticTokensUpdated"/>.</param>
	internal SemanticTokensSource(ProviderContext context, RequestRunner requestRunner, DocumentOperationScheduler scheduler, ProviderEvents events)
	{
		_context = context;
		_requestRunner = requestRunner;
		_scheduler = scheduler;
		_events = events;

		Requests = new PerDocumentRequestCoordinator(
			documents: context.Documents,
			disposal: context.Disposal,
			maxConcurrentRequests: PerDocumentRequestCoordinator.DefaultMaxConcurrentRequests,
			requestKind: "semantic tokens",
			cachedNoun: "tokens",
			providerDisplayName: context.ProviderDisplayName,
			options: context.Options,
			logger: context.Logger
		);
	}

	/// <inheritdoc/>
	public event EventHandler<SemanticTokensUpdatedEventArgs>? SemanticTokensUpdated
	{
		add => _events.SemanticTokensUpdated += value;
		remove => _events.SemanticTokensUpdated -= value;
	}

	/// <summary>The runner of the per-document refreshes, for tests that drive its admission.</summary>
	internal PerDocumentRequestCoordinator Requests { get; }

	/// <inheritdoc/>
	public IReadOnlyList<SemanticToken> GetSemanticTokens(string documentId)
	{
		if (_context.Disposal.IsDisposed)
			return [];

		if (!_context.TryNormalizeDocumentEntryPath(documentId, "Semantic-token read", out string? normalizedFilePath))
			return [];

		return _context.Documents.GetSemanticTokens(normalizedFilePath);
	}

	/// <inheritdoc/>
	public void Trigger(DocumentSnapshot document)
	{
		_scheduler.RunDetachedFromActiveContext(() =>
			_context.ObserveBackgroundTask(RefreshAsync(document, CancellationToken.None), "Semantic tokens refresh")
		);
	}

	/// <summary>Refreshes the tokens of every open document after the server invalidated its cached token colors.</summary>
	/// <param name="sender">The client that raised the event.</param>
	/// <param name="e">The event arguments.</param>
	/// <remarks>
	/// A server sends <c>workspace/semanticTokens/refresh</c> when a change invalidates its cached token colors. The fan-out mirrors
	/// the document-sync trigger but starts from the open-document snapshot.
	/// </remarks>
	public void HandleRefreshRequested(object? sender, EventArgs e) =>
		_context.ObserveBackgroundTask(RefreshTrackedAsync(CancellationToken.None), "Semantic tokens refresh");

	/// <summary>Raises <see cref="SemanticTokensUpdated"/> with the tokens cached for a document that is renamed to a new path.</summary>
	/// <param name="filePath">The normalized path the document is renamed to.</param>
	public void AnnounceRenamed(string filePath) =>
		_events.RaiseSemanticTokensUpdated(_context.Documents.GetDocumentId(filePath), _context.Documents.GetSemanticTokens(filePath));

	/// <summary>Cancels the in-flight refresh for one document path.</summary>
	/// <param name="filePath">The normalized document path.</param>
	public void Cancel(string filePath) => Requests.Cancel(filePath);

	/// <summary>Cancels every in-flight refresh.</summary>
	public void CancelAll() => Requests.CancelAll();

	private async Task RefreshTrackedAsync(CancellationToken cancellationToken)
	{
		if (_context.Disposal.IsDisposed || _context.Client is null || !SupportsSemanticTokens(_context.Client.NegotiatedCapabilities))
			return;

		await Requests.RunForOpenDocumentsAsync(RefreshAsync, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Refreshes the semantic tokens for a tracked document with a full request, superseding any in-flight request for the same document.
	/// </summary>
	/// <param name="document">The tracked document snapshot to refresh.</param>
	/// <param name="cancellationToken">A token that can cancel the refresh.</param>
	/// <remarks>
	/// A failed round trip keeps the cached tokens: the last known token set is a better fallback than dropping the highlighting until the next
	/// successful refresh.
	/// </remarks>
	private async Task RefreshAsync(DocumentSnapshot document, CancellationToken cancellationToken)
	{
		if (_context.Client is not { } client)
			return;

		// One read of the negotiated capabilities decides support and supplies the legend that decodes the response, so the
		// decision and the legend come from the same connection generation.
		NegotiatedServerCapabilities capabilities = client.NegotiatedCapabilities;

		if (!SupportsSemanticTokens(capabilities))
			return;

		await Requests
			.RunAsync(
				document,
				effectiveToken => RequestAndStoreAsync(document, client, capabilities, cancellationToken: effectiveToken),
				cancellationToken
			)
			.ConfigureAwait(false);
	}

	// Reports whether the document is settled: false when the server answered no token stream (a rejected or timed-out request answers the
	// fallback null), so the coordinator asks again while the document keeps its version.
	private async Task<bool> RequestAndStoreAsync(
		DocumentSnapshot document,
		ILanguageServerClient client,
		NegotiatedServerCapabilities capabilities,
		CancellationToken cancellationToken
	)
	{
		SemanticTokensResponse? response = await SendRequestAsync(document, cancellationToken).ConfigureAwait(false);

		if (_context.Disposal.IsDisposed || cancellationToken.IsCancellationRequested)
			return true;

		// RequestRunner retries once across a connection restart, so the response may come from a later generation than
		// the one that negotiated this legend. A restart replays every tracked document, which refreshes its tokens, so a response
		// is dropped, instead of decoded with another generation's legend, unless its snapshot is still the current one.
		if (capabilities != client.NegotiatedCapabilities)
			return true;

		if (response?.Data is not { } data)
		{
			_context.Logger.LogDebug(
				"{DisplayName} semantic tokens response for '{FilePath}' did not contain a token stream; keeping the previously cached tokens.",
				_context.ProviderDisplayName,
				document.FilePath
			);

			return false;
		}

		IReadOnlyList<SemanticToken> semanticTokens = SemanticTokensDecoder.Decode(
			data,
			content: document.Content,
			tokenTypes: capabilities.SemanticTokenTypes,
			tokenModifiers: capabilities.SemanticTokenModifiers
		);

		// Store and announce only when the decoded tokens match the currently tracked document version;
		// otherwise, the payload was decoded against a stale snapshot, and the newer version refreshes on its own.
		if (!_context.Documents.TryStoreSemanticTokens(document.FilePath, document.Version, semanticTokens))
			return true;

		// Deliberate divergence from the diagnostics path: the version-fenced cache snapshots what it stores, so
		// the list raised here is the freshly decoded one rather than the stored instance, whereas the
		// diagnostics source raises the very snapshot the tracker adopts. Each is a stable read-only view
		// for a subscriber, and neither lets a later store mutate what was already raised.
		_events.RaiseSemanticTokensUpdated(document.DocumentId, semanticTokens);
		return true;
	}

	// A server that advertises full semantic tokens without a token-type legend cannot have its token stream decoded.
	private static bool SupportsSemanticTokens(NegotiatedServerCapabilities capabilities) =>
		capabilities.SupportsSemanticTokensFull && capabilities.SemanticTokenTypes.Count > 0;

	private Task<SemanticTokensResponse?> SendRequestAsync(DocumentSnapshot document, CancellationToken cancellationToken)
	{
		return _requestRunner.SendAsync<SemanticTokensResponse?>(
			method: LspMethodNames.SemanticTokensFull,
			parameters: new SemanticTokensParams(new TextDocumentIdentifier(document.Uri)),
			fallbackValue: null,
			cancellationToken
		);
	}
}

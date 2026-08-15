namespace Nickelony.LanguageServer.Provider;

public abstract partial class LanguageServerIntelliSenseProviderBase
{
	// Semantic-token refreshes run concurrently, but bounded: a session with many open documents must not
	// send its whole burst on every refresh request. The cap stays internal - the fan-out is driven by the
	// framework's own refresh triggers and no host has needed to tune it.
	private const int MaxConcurrentSemanticTokenRefreshes = 4;

	// Admission and the per-path source map are read and written under this one lock: a refresh either
	// supersedes the path's previous source or is rejected once disposal has closed admission, with no window
	// in which a refresh can publish a source the drain has already passed.
	private readonly object _semanticTokenRequestGate = new();
	private readonly Dictionary<string, CancellationTokenSource> _semanticTokenRequests = new(LanguageServerPaths.LocalPathComparer);

	private bool _semanticTokenRequestAdmissionClosed;
	private EventHandler<SemanticTokensUpdatedEventArgs>? _semanticTokensUpdated;

	/// <inheritdoc/>
	public event EventHandler<SemanticTokensUpdatedEventArgs>? SemanticTokensUpdated
	{
		add => AddAdmittedCallback(ref _semanticTokensUpdated, value);
		remove => RemoveCallback(ref _semanticTokensUpdated, value);
	}

	/// <inheritdoc/>
	public IReadOnlyList<SemanticToken> GetSemanticTokens(string filePath)
	{
		ArgumentNullException.ThrowIfNull(filePath);

		if (_isDisposed)
			return [];

		if (!TryNormalizeDocumentEntryPath(filePath, "Semantic-token read", out string? normalizedFilePath))
			return [];

		return InvokeContainedHook(() => GetTrackedSemanticTokens(normalizedFilePath), [], "tracked semantic tokens");
	}

	/// <summary>
	/// Queues a full semantic-token refresh for a document that was just synchronized or reopened.
	/// </summary>
	/// <remarks>
	/// The refresh runs detached from the document-sync pipeline: a stalled semantic-tokens round trip must not
	/// delay the next change notification or the replay of the remaining documents after a restart. Per-document
	/// supersession is arbitrated by <see cref="ReplaceSemanticTokenRequest"/>. The work is started through
	/// <see cref="DocumentOperationScheduler.RunDetachedFromActiveContext"/> so it does not inherit the enclosing
	/// scheduler slot's re-entrancy scope: a refresh that fails over to the transport-start path legitimately queues
	/// per-document operations, which the slot-scoped guard would otherwise reject and count as a startup failure.
	/// </remarks>
	/// <param name="document">The synchronized tracked document snapshot.</param>
	private void TriggerSemanticTokenRefresh(DocumentSnapshot document)
		=> _documentScheduler.RunDetachedFromActiveContext(
			() => ObserveBackgroundTask(RefreshSemanticTokensAsync(document, CancellationToken.None), "Semantic tokens refresh"));

	// Servers ask for a semantic-token refresh when a watched configuration file changes; the fan-out mirrors
	// the document-sync trigger but starts from the open-document snapshot.
	private void HandleSemanticTokensRefreshRequested(object? sender, EventArgs e)
		=> ObserveBackgroundTask(RefreshTrackedSemanticTokensAsync(CancellationToken.None), "Semantic tokens refresh");

	private async Task RefreshTrackedSemanticTokensAsync(CancellationToken cancellationToken)
	{
		if (_isDisposed || Client is null || !Client.SupportsSemanticTokensFull || Client.SemanticTokenTypes.Count == 0)
			return;

		IReadOnlyList<DocumentSnapshot> documents = _documents.GetOpenDocuments();

		// Every document's refresh is superseded and canceled independently, so the fan-out runs concurrently
		// through a shared gate instead of serializing N server round trips behind each other or sending an
		// unbounded burst.
		using var concurrencyGate = new SemaphoreSlim(MaxConcurrentSemanticTokenRefreshes);
		var refreshes = new Task[documents.Count];

		for (int i = 0; i < documents.Count; i++)
			refreshes[i] = RefreshGatedSemanticTokensAsync(documents[i], concurrencyGate, cancellationToken);

		await Task.WhenAll(refreshes).ConfigureAwait(false);
	}

	private async Task RefreshGatedSemanticTokensAsync(DocumentSnapshot document, SemaphoreSlim concurrencyGate,
		CancellationToken cancellationToken)
	{
		await concurrencyGate.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			// The fan-out snapshotted the open documents before acquiring the gate; a document that closed
			// while this refresh waited must not be queried, and its result must not be stored for a path that
			// is no longer tracked.
			if (!_documents.IsTracked(document.FilePath))
				return;

			await RefreshSemanticTokensAsync(document, cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			concurrencyGate.Release();
		}
	}

	/// <summary>
	/// Refreshes the semantic tokens for a tracked document with a full request, superseding any in-flight
	/// request for the same document.
	/// </summary>
	/// <remarks>
	/// A failed round trip keeps the previously cached tokens: the last known token set is a better fallback than
	/// dropping the highlighting until the next successful refresh.
	/// </remarks>
	/// <param name="document">The tracked document snapshot to refresh.</param>
	/// <param name="cancellationToken">A token that can cancel the refresh.</param>
	private async Task RefreshSemanticTokensAsync(DocumentSnapshot document, CancellationToken cancellationToken)
	{
		if (Client is null || !Client.SupportsSemanticTokensFull || Client.SemanticTokenTypes.Count == 0)
			return;

		CancellationToken effectiveToken = ReplaceSemanticTokenRequest(document.FilePath, cancellationToken, out CancellationTokenSource? linkedSource);

		try
		{
			SemanticTokensResponsePayload? response = await SendSemanticTokensRequestAsync(document, effectiveToken)
				.ConfigureAwait(false);

			if (_isDisposed || effectiveToken.IsCancellationRequested)
				return;

			if (response?.Data is not { } data)
			{
				Logger.LogDebug("{DisplayName} semantic tokens response for '{FilePath}' did not contain a token stream; keeping the previously cached tokens.",
					ProviderDisplayName, document.FilePath);

				return;
			}

			IReadOnlyList<SemanticToken> semanticTokens = SemanticTokensDecoder.Decode(
				data, document.Content, Client.SemanticTokenTypes, Client.SemanticTokenModifiers);

			// Store and announce only when the decoded tokens match the currently tracked document version;
			// otherwise, the payload was decoded against a stale snapshot.
			if (!InvokeContainedHook(() => TryStoreSemanticTokens(document.FilePath, document.Version, semanticTokens), false, "semantic-token store"))
				return;

			RaiseSemanticTokensUpdated(document.FilePath, semanticTokens);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			// Provider-owned cancellation: a newer request superseded this one, the document closed, or the
			// provider was disposed; a request timeout surfaces as the dispatcher's fallback, not as this
			// exception. The refresh returns quietly and keeps the previously cached tokens either way.
		}
		catch (OperationCanceledException)
		{
			// Caller-owned cancellation rethrows without reaching the warning path below.
			throw;
		}
		catch (IOException exception)
		{
			// Defense in depth: the request dispatcher already converts transport failures to the fallback
			// value; these catches keep the refresh resilient if that conversion contracts.
			Logger.LogDebug(exception, "{DisplayName} semantic tokens request failed for '{FilePath}' due to a transport error; keeping the previously cached tokens.",
				ProviderDisplayName, document.FilePath);
		}
		catch (ObjectDisposedException)
		{
			// Defense in depth: the dispatcher already absorbs a torn-down client.
		}
		catch (Exception exception)
		{
			Logger.LogWarning(exception, "{DisplayName} semantic tokens request failed for '{FilePath}'; keeping the previously cached tokens.",
				ProviderDisplayName, document.FilePath);
		}
		finally
		{
			ClearSemanticTokenRequest(document.FilePath, linkedSource);
		}
	}

	private Task<SemanticTokensResponsePayload?> SendSemanticTokensRequestAsync(DocumentSnapshot document, CancellationToken cancellationToken)
		=> _requestDispatcher.SendAsync<SemanticTokensResponsePayload?>(
				LspMethodNames.SemanticTokensFull,
			new SemanticTokensParams(new TextDocumentIdentifier(document.Uri)),
			fallbackValue: null,
			cancellationToken);

	private CancellationToken ReplaceSemanticTokenRequest(string filePath, CancellationToken cancellationToken, out CancellationTokenSource? linkedSource)
	{
		CancellationToken freshToken;
		CancellationTokenSource? previousSource;

		// Admission and the map are read and written under the lock, so the disposal drain cannot interleave
		// between the check and the publish: either this call supersedes the path's source, or admission is
		// already closed and it publishes nothing.
		lock (_semanticTokenRequestGate)
		{
			if (_semanticTokenRequestAdmissionClosed)
			{
				linkedSource = null;
				return new CancellationToken(canceled: true);
			}

			var freshSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

			// The token is captured while the source is still private to this call: once it is in the map a
			// concurrent cancel may dispose it, and reading Token from a disposed source throws.
			freshToken = freshSource.Token;

			_semanticTokenRequests.TryGetValue(filePath, out previousSource);
			_semanticTokenRequests[filePath] = freshSource;
			linkedSource = freshSource;
		}

		// The superseded source is canceled outside the lock and after the fresh source is published, so the
		// caller can always release what this call tracked.
		CancelAndDispose(previousSource);
		return freshToken;
	}

	private void ClearSemanticTokenRequest(string filePath, CancellationTokenSource? linkedSource)
	{
		if (linkedSource is null)
			return;

		lock (_semanticTokenRequestGate)
		{
			// Remove only this call's own source: a superseding refresh has already published a replacement,
			// and dropping that would strand it.
			if (_semanticTokenRequests.TryGetValue(filePath, out CancellationTokenSource? current)
				&& ReferenceEquals(current, linkedSource))
			{
				_semanticTokenRequests.Remove(filePath);
			}
		}

		linkedSource.Dispose();
	}

	private void CancelSemanticTokenRequest(string filePath)
	{
		CancellationTokenSource? source;

		lock (_semanticTokenRequestGate)
		{
			if (!_semanticTokenRequests.Remove(filePath, out source))
				return;
		}

		CancelAndDispose(source);
	}

	private void CancelAllSemanticTokenRequests()
	{
		CancellationTokenSource[] sources;

		lock (_semanticTokenRequestGate)
		{
			// Close admission before draining, so a refresh that runs after the lock is released is rejected
			// instead of publishing a source this drain has already passed.
			_semanticTokenRequestAdmissionClosed = true;

			if (_semanticTokenRequests.Count == 0)
				return;

			sources = [.. _semanticTokenRequests.Values];
			_semanticTokenRequests.Clear();
		}

		foreach (CancellationTokenSource source in sources)
			CancelAndDispose(source);
	}

	private static void CancelAndDispose(CancellationTokenSource? source)
	{
		if (source is null)
			return;

		try
		{
			source.Cancel();
		}
		catch (Exception exception) when (exception is ObjectDisposedException or AggregateException)
		{
			// Cancellation callbacks registered on provider-owned sources must not escape cleanup:
			// AggregateException surfaces a throwing callback, ObjectDisposedException a source the drain
			// already released.
		}

		source.Dispose();
	}

	private void RaiseSemanticTokensUpdated(string filePath, IReadOnlyList<SemanticToken> semanticTokens)
		=> RaiseSubscribers<EventHandler<SemanticTokensUpdatedEventArgs>>(
			() => _semanticTokensUpdated,
			handler => handler(this, new SemanticTokensUpdatedEventArgs(filePath, semanticTokens)),
			"Semantic-token subscriber");
}

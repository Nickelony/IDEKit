using Nickelony.IDEKit.Core.Requests;

namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Coordinates the background requests of a push-model feature per document: a newer request for a path supersedes the previous one.
/// </summary>
/// <remarks>
/// <para>
/// A refresh of every open document runs the per-document requests concurrently up to a fixed bound, so a session with many open documents sends
/// no unbounded burst and no round trip waits behind another one. Each fan-out call creates its own concurrency bound.
/// </para>
/// <para>
/// A request that fails keeps the feature's cached payload: the last known result is a better fallback than clearing the document
/// until the next success. The owner's disposal closes the coordinator through <see cref="CancelAll"/>, which cancels every request without waiting
/// for it and rejects later ones.
/// </para>
/// <para>
/// A request that ends without settling its document, because the server rejected it (a server cancels a pending request with <c>ContentModified</c>
/// when any workspace file changes), answered nothing, or timed out, runs again after a short wait while the document keeps the version
/// it was requested for, once for each wait in <see cref="RetryDelays"/>. Without the retry, nothing would ask again until the document changes,
/// and the cached payload would keep describing older text.
/// </para>
/// </remarks>
internal sealed class PerDocumentRequestCoordinator
{
	/// <summary>The number of per-document requests that run at once in one fan-out unless the owner picks another bound.</summary>
	/// <remarks>
	/// A session with many open documents must not send its whole burst on every refresh request. The bound stays internal:
	/// the framework's own refresh triggers drive the fan-out, and no option exposes it.
	/// </remarks>
	internal const int DefaultMaxConcurrentRequests = 4;

	/// <summary>The waits before each retry of a request that ended without settling its document.</summary>
	/// <remarks>
	/// The waits grow, so a server that keeps rejecting the request while another document changes is asked a few times only.
	/// </remarks>
	internal static IReadOnlyList<TimeSpan> RetryDelays { get; } =
	[TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(900)];

	private readonly KeyedLatestRequestGate<string> _gate;

	private readonly DocumentTracker _documents;

	private readonly ProviderLifetime _disposal;

	private readonly int _maxConcurrentRequests;

	private readonly string _requestKind;
	private readonly string _cachedNoun;

	private readonly string _providerDisplayName;

	private readonly ILogger _logger;

	/// <param name="documents">The tracker whose open documents the fan-out reads and whose tracked state it rechecks before each request.</param>
	/// <param name="disposal">
	/// The disposal of the owning provider, which <see cref="BackgroundFailurePolicy"/> consults to classify a failure.
	/// </param>
	/// <param name="maxConcurrentRequests">The number of per-document requests that run at once in one fan-out.</param>
	/// <param name="requestKind">The request kind named in failure logs, for example <c>diagnostics</c>.</param>
	/// <param name="cachedNoun">The cached payload named in failure logs, for example <c>diagnostics</c>.</param>
	/// <param name="providerDisplayName">The provider display name used in failure logs.</param>
	/// <param name="options">The provider options whose path comparison decides which paths share a request slot.</param>
	/// <param name="logger">The logger that records a failed request.</param>
	internal PerDocumentRequestCoordinator(
		DocumentTracker documents,
		ProviderLifetime disposal,
		int maxConcurrentRequests,
		string requestKind,
		string cachedNoun,
		string providerDisplayName,
		LanguageServerFeatureProviderOptions options,
		ILogger logger
	)
	{
		_gate = new KeyedLatestRequestGate<string>(LanguageServerFeatureProviderOptions.PathComparisonOf(options).Comparer);
		_documents = documents;
		_disposal = disposal;
		_maxConcurrentRequests = maxConcurrentRequests;
		_requestKind = requestKind;
		_cachedNoun = cachedNoun;
		_providerDisplayName = providerDisplayName;
		_logger = logger;
	}

	/// <summary>Runs the request for every open document, concurrently up to the bound, and completes when every request has settled.</summary>
	/// <param name="requestAsync">Runs one document's request with the token that supersession and disposal cancel.</param>
	/// <param name="cancellationToken">A token that can cancel the fan-out.</param>
	/// <remarks>
	/// A document that closed while its request waited for the bound is skipped, so no request runs and no result is stored for a path
	/// that is not tracked.
	/// </remarks>
	internal async Task RunForOpenDocumentsAsync(Func<DocumentSnapshot, CancellationToken, Task> requestAsync, CancellationToken cancellationToken)
	{
		IReadOnlyList<DocumentSnapshot> documents = _documents.GetOpenDocuments();

		using var concurrencyGate = new SemaphoreSlim(_maxConcurrentRequests);
		var runs = new Task[documents.Count];

		for (int i = 0; i < documents.Count; i++)
			runs[i] = RunWithinBoundAsync(document: documents[i], concurrencyGate, requestAsync, cancellationToken);

		await Task.WhenAll(runs).ConfigureAwait(false);
	}

	/// <summary>Runs one document's request, superseding the previous request for the same path.</summary>
	/// <param name="document">The tracked document snapshot the request is for.</param>
	/// <param name="requestAsync">
	/// Sends the request and applies its result, observing the token that supersession and disposal cancel, and reports whether the request
	/// settled the document: <see langword="false"/> when it ended without a result to apply, so the request runs again.
	/// </param>
	/// <param name="cancellationToken">The caller's token; its cancellation propagates, and any other cancellation ends the request quietly.</param>
	/// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled.</exception>
	/// <remarks>
	/// A newer request, a closed document and disposal cancel a request without the caller's token, and the request then returns quietly.
	/// Any other failure is logged and leaves the cached payload in place.
	/// </remarks>
	internal async Task RunAsync(DocumentSnapshot document, Func<CancellationToken, Task<bool>> requestAsync, CancellationToken cancellationToken)
	{
		using KeyedRequestLease<string> lease = _gate.Begin(document.FilePath, cancellationToken);

		try
		{
			for (int retry = 0; !await requestAsync(lease.CancellationToken).ConfigureAwait(false) && retry < RetryDelays.Count; retry++)
			{
				await Task.Delay(RetryDelays[retry], lease.CancellationToken).ConfigureAwait(false);

				// A changed or closed document has its own request coming, which supersedes this one.
				if (_documents.GetDocumentSnapshot(document.FilePath)?.Version != document.Version)
					return;
			}
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			// Provider-owned cancellation: a newer request superseded this one, the document closed, or the
			// provider was disposed; a request timeout surfaces as the fallback of RequestRunner, not as this
			// exception. The request returns quietly and keeps the cached payload either way.
		}
		catch (OperationCanceledException)
		{
			// Caller-owned cancellation rethrows without reaching the warning path below.
			throw;
		}
		catch (Exception exception)
		{
			LogRoundTripFailure(exception, document.FilePath);
		}
	}

	/// <summary>Cancels the in-flight request for one document path, if any.</summary>
	/// <param name="filePath">The normalized document path.</param>
	internal void Cancel(string filePath) => _gate.Cancel(filePath);

	/// <summary>Closes admission and cancels every in-flight request.</summary>
	internal void CancelAll() => _gate.CancelAll();

	private async Task RunWithinBoundAsync(
		DocumentSnapshot document,
		SemaphoreSlim concurrencyGate,
		Func<DocumentSnapshot, CancellationToken, Task> requestAsync,
		CancellationToken cancellationToken
	)
	{
		await concurrencyGate.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			// The fan-out snapshotted the open documents before acquiring the bound; a document that closed
			// while this request waited must not be queried, and its result must not be stored for a path that
			// is not tracked.
			if (!_documents.IsTracked(document.FilePath))
				return;

			await requestAsync(document, cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			concurrencyGate.Release();
		}
	}

	/// <summary>Logs a failed request round trip at the level that <see cref="BackgroundFailurePolicy"/> gives it.</summary>
	/// <param name="exception">The failure to log.</param>
	/// <param name="filePath">The document path the request targeted.</param>
	/// <remarks>
	/// Caller-owned cancellation and provider-owned cancellation are classified before this method runs.
	/// <see cref="RequestRunner"/> turns a connection failure, a server rejection, a timeout and an unprocessable response into its fallback value,
	/// and it absorbs a torn-down client only while the provider is disposing, so a client disposed under a live provider is among the failures
	/// that arrive here.
	/// </remarks>
	private void LogRoundTripFailure(Exception exception, string filePath)
	{
		if (BackgroundFailurePolicy.LevelOf(exception, _disposal) is not { } level)
			return;

		_logger.Log(
			level,
			exception,
			"{DisplayName} {RequestKind} request failed for '{FilePath}'; keeping the previously cached {CachedNoun}.",
			_providerDisplayName,
			_requestKind,
			filePath,
			_cachedNoun
		);
	}
}

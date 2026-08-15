using Nickelony.IDEKit.IntelliSense.Diagnostics;
using System.Collections.Concurrent;

namespace Nickelony.LanguageServer.Provider;

public abstract partial class LanguageServerIntelliSenseProviderBase
{
	// Diagnostic pulls run concurrently, but bounded: a session with many open documents must not send its whole
	// burst on every refresh request. The cap stays internal - the fan-out is driven by the framework's own
	// refresh triggers and no host has needed to tune it.
	private const int MaxConcurrentDiagnosticPulls = 4;

	// Admission and the per-path source map are read and written under this one lock: a pull either supersedes the
	// path's previous source or is rejected once disposal has closed admission, with no window in which a pull can
	// publish a source the drain has already passed.
	private readonly object _diagnosticRequestGate = new();
	private readonly Dictionary<string, CancellationTokenSource> _diagnosticRequests = new(LanguageServerPaths.LocalPathComparer);

	// The last resultId each document's pull report carried, threaded back as previousResultId so a pull-only server
	// can answer with an unchanged report. A missing entry means no report has been applied for that document yet.
	private readonly ConcurrentDictionary<string, string> _diagnosticResultIds = new(LanguageServerPaths.LocalPathComparer);

	private bool _diagnosticRequestAdmissionClosed;

	/// <summary>
	/// Queues a pull-diagnostics request for a document that was just synchronized or reopened.
	/// </summary>
	/// <remarks>
	/// The pull runs detached from the document-sync pipeline: a stalled diagnostics round trip must not delay the
	/// next change notification or the replay of the remaining documents after a restart. Per-document supersession is
	/// arbitrated by <see cref="ReplaceDiagnosticRequest"/>. The work is started through
	/// <see cref="DocumentOperationScheduler.RunDetachedFromActiveContext"/> so it does not inherit the enclosing
	/// scheduler slot's re-entrancy scope: a pull that fails over to the transport-start path legitimately queues
	/// per-document operations, which the slot-scoped guard would otherwise reject and count as a startup failure.
	/// </remarks>
	/// <param name="document">The synchronized tracked document snapshot.</param>
	private void TriggerDiagnosticsPull(DocumentSnapshot document)
	{
		// Only a pull-only server needs the request; a push server would receive a request it never advertised.
		if (Client is not { SupportsPullDiagnostics: true })
			return;

		_documentScheduler.RunDetachedFromActiveContext(
			() => ObserveBackgroundTask(PullDiagnosticsAsync(document, CancellationToken.None), "Diagnostics pull"));
	}

	// A pull-only server asks the client to re-pull when workspace state changed outside a document change; the
	// fan-out mirrors the document-sync trigger but starts from the open-document snapshot.
	private void HandleDiagnosticRefreshRequested(object? sender, EventArgs e)
		=> ObserveBackgroundTask(PullTrackedDiagnosticsAsync(CancellationToken.None), "Diagnostics pull");

	private async Task PullTrackedDiagnosticsAsync(CancellationToken cancellationToken)
	{
		if (_isDisposed || Client is not { SupportsPullDiagnostics: true })
			return;

		IReadOnlyList<DocumentSnapshot> documents = _documents.GetOpenDocuments();

		// Every document's pull is superseded and canceled independently, so the fan-out runs concurrently through
		// a shared gate instead of serializing N server round trips behind each other or sending an unbounded burst.
		using var concurrencyGate = new SemaphoreSlim(MaxConcurrentDiagnosticPulls);
		var pulls = new Task[documents.Count];

		for (int i = 0; i < documents.Count; i++)
			pulls[i] = PullGatedDiagnosticsAsync(documents[i], concurrencyGate, cancellationToken);

		await Task.WhenAll(pulls).ConfigureAwait(false);
	}

	private async Task PullGatedDiagnosticsAsync(DocumentSnapshot document, SemaphoreSlim concurrencyGate,
		CancellationToken cancellationToken)
	{
		await concurrencyGate.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			// The fan-out snapshotted the open documents before acquiring the gate; a document that closed
			// while this pull waited must not be queried, and its result must not be stored for a path that
			// is no longer tracked.
			if (!_documents.IsTracked(document.FilePath))
				return;

			await PullDiagnosticsAsync(document, cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			concurrencyGate.Release();
		}
	}

	/// <summary>
	/// Pulls the diagnostics for a tracked document, superseding any in-flight pull for the same document.
	/// </summary>
	/// <remarks>
	/// A failed round trip keeps the previously cached diagnostics: the last known report is a better fallback than
	/// clearing the document until the next successful pull. An unchanged report also keeps the cache, because the
	/// server reports that nothing changed since the request's <c>previousResultId</c>.
	/// </remarks>
	/// <param name="document">The tracked document snapshot to pull diagnostics for.</param>
	/// <param name="cancellationToken">A token that can cancel the pull.</param>
	private async Task PullDiagnosticsAsync(DocumentSnapshot document, CancellationToken cancellationToken)
	{
		if (Client is not { SupportsPullDiagnostics: true })
			return;

		CancellationToken effectiveToken = ReplaceDiagnosticRequest(document.FilePath, cancellationToken, out CancellationTokenSource? linkedSource);

		try
		{
			DocumentDiagnosticReportPayload? report = await SendDiagnosticsRequestAsync(document, effectiveToken).ConfigureAwait(false);

			if (_isDisposed || effectiveToken.IsCancellationRequested || report is null)
				return;

			ApplyDiagnosticReport(document, report);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			// Provider-owned cancellation: a newer request superseded this one, the document closed, or the
			// provider was disposed; a request timeout surfaces as the dispatcher's fallback, not as this
			// exception. The pull returns quietly and keeps the previously cached diagnostics either way.
		}
		catch (OperationCanceledException)
		{
			// Caller-owned cancellation rethrows without reaching the warning path below.
			throw;
		}
		catch (IOException exception)
		{
			// Defense in depth: the request dispatcher already converts transport failures to the fallback
			// value; these catches keep the pull resilient if that conversion contracts.
			Logger.LogDebug(exception, "{DisplayName} diagnostics request failed for '{FilePath}' due to a transport error; keeping the previously cached diagnostics.",
				ProviderDisplayName, document.FilePath);
		}
		catch (ObjectDisposedException)
		{
			// Defense in depth: the dispatcher already absorbs a torn-down client.
		}
		catch (Exception exception)
		{
			Logger.LogWarning(exception, "{DisplayName} diagnostics request failed for '{FilePath}'; keeping the previously cached diagnostics.",
				ProviderDisplayName, document.FilePath);
		}
		finally
		{
			ClearDiagnosticRequest(document.FilePath, linkedSource);
		}
	}

	private Task<DocumentDiagnosticReportPayload?> SendDiagnosticsRequestAsync(DocumentSnapshot document, CancellationToken cancellationToken)
		=> _requestDispatcher.SendAsync<DocumentDiagnosticReportPayload?>(
			LspMethodNames.DocumentDiagnostic,
			new DocumentDiagnosticParams(new TextDocumentIdentifier(document.Uri), GetDiagnosticResultId(document.FilePath)),
			fallbackValue: null,
			cancellationToken);

	/// <summary>
	/// Applies one pull-diagnostics report to the diagnostics cache and the <see cref="DiagnosticsUpdated"/> event.
	/// </summary>
	/// <param name="document">The tracked document snapshot the report was requested for.</param>
	/// <param name="report">The report the server returned.</param>
	/// <remarks>
	/// A full report is fed through the same <see cref="HandleDiagnosticsPayload"/> hook and
	/// <see cref="RaiseDiagnosticsUpdated"/> path the push notification uses, so a provider implements one
	/// diagnostics path for both delivery models. The report is parsed against the request-time snapshot, so a
	/// document that changed while the request was in flight is fenced out by the same version check the push path
	/// relies on. Related reports name documents the server resolved on this request's behalf; each is applied when
	/// it maps to a tracked document.
	/// </remarks>
	private void ApplyDiagnosticReport(DocumentSnapshot document, DocumentDiagnosticReportPayload report)
	{
		ApplyDiagnosticReportCore(document, report);

		if (report.RelatedDocuments is not { Count: > 0 } relatedDocuments)
			return;

		foreach (KeyValuePair<string, DocumentDiagnosticReportPayload> related in relatedDocuments)
		{
			if (!LanguageServerPaths.TryGetLocalPath(related.Key, out string relatedFilePath))
				continue;

			// An untracked related document has no content snapshot to map offsets against; it is skipped like the
			// push path skips diagnostics for documents that are not tracked locally.
			if (_documents.GetDocumentSnapshot(relatedFilePath) is not { } relatedDocument)
				continue;

			ApplyDiagnosticReportCore(relatedDocument, related.Value);
		}
	}

	private void ApplyDiagnosticReportCore(DocumentSnapshot document, DocumentDiagnosticReportPayload report)
	{
		string filePath = document.FilePath;

		// An unchanged report means the request's previousResultId still describes the document: the cache is
		// already correct, and only the reported id needs to be threaded forward.
		if (report.IsUnchanged)
		{
			StoreDiagnosticResultId(filePath, report.ResultId);
			return;
		}

		if (!report.IsFull)
		{
			Logger.LogDebug("{DisplayName} pull diagnostics report for '{FilePath}' had an unrecognized kind '{Kind}' and was dropped.",
				ProviderDisplayName, filePath, report.Kind);

			return;
		}

		var parameters = new PublishDiagnosticsParams(document.Uri, document.Version, report.Items);

		IReadOnlyList<TextDiagnostic>? diagnostics = InvokeContainedHook<IReadOnlyList<TextDiagnostic>?>(
			() => HandleDiagnosticsPayload(filePath, parameters, document),
			fallbackValue: null,
			"diagnostics payload handling");

		if (_isDisposed || diagnostics is null)
			return;

		StoreDiagnosticResultId(filePath, report.ResultId);
		RaiseDiagnosticsUpdated(filePath, diagnostics);
	}

	private string? GetDiagnosticResultId(string filePath)
		=> _diagnosticResultIds.TryGetValue(filePath, out string? resultId) ? resultId : null;

	private void StoreDiagnosticResultId(string filePath, string? resultId)
	{
		// An absent resultId leaves the previously threaded id in place: without a replacement the next request
		// must still advertise the last id the server acknowledged, not silently drop to a full report.
		if (string.IsNullOrEmpty(resultId))
			return;

		_diagnosticResultIds[filePath] = resultId;
	}

	private CancellationToken ReplaceDiagnosticRequest(string filePath, CancellationToken cancellationToken, out CancellationTokenSource? linkedSource)
	{
		CancellationToken freshToken;
		CancellationTokenSource? previousSource;

		// Admission and the map are read and written under the lock, so the disposal drain cannot interleave
		// between the check and the publish: either this call supersedes the path's source, or admission is
		// already closed and it publishes nothing.
		lock (_diagnosticRequestGate)
		{
			if (_diagnosticRequestAdmissionClosed)
			{
				linkedSource = null;
				return new CancellationToken(canceled: true);
			}

			var freshSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

			// The token is captured while the source is still private to this call: once it is in the map a
			// concurrent cancel may dispose it, and reading Token from a disposed source throws.
			freshToken = freshSource.Token;

			_diagnosticRequests.TryGetValue(filePath, out previousSource);
			_diagnosticRequests[filePath] = freshSource;
			linkedSource = freshSource;
		}

		// The superseded source is canceled outside the lock and after the fresh source is published, so the
		// caller can always release what this call tracked.
		CancelAndDispose(previousSource);
		return freshToken;
	}

	private void ClearDiagnosticRequest(string filePath, CancellationTokenSource? linkedSource)
	{
		if (linkedSource is null)
			return;

		lock (_diagnosticRequestGate)
		{
			// Remove only this call's own source: a superseding pull has already published a replacement, and
			// dropping that would strand it.
			if (_diagnosticRequests.TryGetValue(filePath, out CancellationTokenSource? current)
				&& ReferenceEquals(current, linkedSource))
			{
				_diagnosticRequests.Remove(filePath);
			}
		}

		linkedSource.Dispose();
	}

	private void CancelDiagnosticRequest(string filePath)
	{
		// The document's diagnostics are no longer attributed to a tracked server copy, so the threaded result id
		// is dropped with the in-flight request: a later reopen must not advertise an id the server may have
		// discarded when the document closed.
		_diagnosticResultIds.TryRemove(filePath, out _);

		CancellationTokenSource? source;

		lock (_diagnosticRequestGate)
		{
			if (!_diagnosticRequests.Remove(filePath, out source))
				return;
		}

		CancelAndDispose(source);
	}

	private void CancelAllDiagnosticRequests()
	{
		_diagnosticResultIds.Clear();

		CancellationTokenSource[] sources;

		lock (_diagnosticRequestGate)
		{
			// Close admission before draining, so a pull that runs after the lock is released is rejected
			// instead of publishing a source this drain has already passed.
			_diagnosticRequestAdmissionClosed = true;

			if (_diagnosticRequests.Count == 0)
				return;

			sources = [.. _diagnosticRequests.Values];
			_diagnosticRequests.Clear();
		}

		foreach (CancellationTokenSource source in sources)
			CancelAndDispose(source);
	}
}

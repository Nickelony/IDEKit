using System.Collections.Concurrent;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Pulls the diagnostics of tracked documents from a server that advertises pull diagnostics, and applies the reports it answers with.
/// </summary>
/// <remarks>
/// <para>
/// A pull runs only against a server that advertises pull diagnostics, because a push server would receive a request it never advertised. A new pull
/// for a document supersedes the previous one, and disposal drains them all.
/// </para>
/// <para>
/// Each document keeps the last result id its report carried, so the next pull can answer an unchanged document without resending its diagnostics.
/// The coordinator drops a document's id when its server copy goes away.
/// </para>
/// </remarks>
internal sealed class DiagnosticsPullCoordinator
{
	private readonly ProviderContext _context;
	private readonly RequestRunner _requestRunner;
	private readonly DocumentOperationScheduler _scheduler;
	private readonly Func<string, PublishDiagnosticsParams, DocumentSnapshot, IReadOnlyList<Diagnostic>?> _mapAndStore;
	private readonly Action<string, IReadOnlyList<Diagnostic>> _raiseUpdated;

	// Runs the superseding per-document pulls: a new pull for a path cancels the previous one, and disposal drains
	// them all.
	private readonly PerDocumentRequestCoordinator _requests;

	// The last resultId each document's pull report carried, threaded back as previousResultId so a pull-only server
	// can answer with an unchanged report. A missing entry sends the next pull without a previousResultId, so the server answers with a full report.
	private readonly ConcurrentDictionary<string, string> _resultIds;

	/// <param name="context">The client, the tracked documents and the logger the provider shares.</param>
	/// <param name="requestRunner">The runner that sends each pull request under the timeout and restart policy.</param>
	/// <param name="scheduler">The scheduler whose active context a detached pull must not inherit.</param>
	/// <param name="mapAndStore">
	/// Maps a payload for a tracked document with the provider's policy and stores it, and returns the diagnostics to raise or <see langword="null"/>
	/// when the payload is dropped.
	/// </param>
	/// <param name="raiseUpdated">Raises the update event for a document with its diagnostics.</param>
	internal DiagnosticsPullCoordinator(
		ProviderContext context,
		RequestRunner requestRunner,
		DocumentOperationScheduler scheduler,
		Func<string, PublishDiagnosticsParams, DocumentSnapshot, IReadOnlyList<Diagnostic>?> mapAndStore,
		Action<string, IReadOnlyList<Diagnostic>> raiseUpdated
	)
	{
		_context = context;
		_requestRunner = requestRunner;
		_scheduler = scheduler;
		_mapAndStore = mapAndStore;
		_raiseUpdated = raiseUpdated;
		_resultIds = new ConcurrentDictionary<string, string>(context.Options.PathComparison.Comparer);

		_requests = new PerDocumentRequestCoordinator(
			documents: context.Documents,
			disposal: context.Disposal,
			maxConcurrentRequests: PerDocumentRequestCoordinator.DefaultMaxConcurrentRequests,
			requestKind: "diagnostics",
			cachedNoun: "diagnostics",
			providerDisplayName: context.ProviderDisplayName,
			options: context.Options,
			logger: context.Logger
		);
	}

	/// <summary>Queues a pull-diagnostics request for a document that was synchronized or reopened, when the server is pull-only.</summary>
	/// <param name="document">The synchronized tracked document snapshot.</param>
	/// <remarks>
	/// The pull starts as <see cref="IServerPayloadRefresher.Trigger"/> describes.
	/// </remarks>
	internal void Trigger(DocumentSnapshot document)
	{
		// Only a pull-only server needs the request; a push server would receive a request it never advertised.
		if (_context.Client is not { NegotiatedCapabilities.SupportsPullDiagnostics: true })
			return;

		_scheduler.RunDetachedFromActiveContext(() =>
			_context.ObserveBackgroundTask(PullAsync(document, CancellationToken.None), "Diagnostics pull")
		);
	}

	/// <summary>Pulls the diagnostics of every open document after a pull-only server asks the client to re-pull.</summary>
	/// <remarks>
	/// A server asks for a re-pull when workspace state changed outside a document change. The fan-out mirrors the document-sync trigger but starts
	/// from the open-document snapshot.
	/// </remarks>
	internal void RequestRefresh() => _context.ObserveBackgroundTask(PullTrackedAsync(CancellationToken.None), "Diagnostics pull");

	/// <summary>Cancels the in-flight pull for one document path and drops the result id threaded for it.</summary>
	/// <param name="filePath">The normalized document path.</param>
	/// <remarks>
	/// The document has no tracked server copy the diagnostics belong to, so a later reopen must not advertise an id the server may have discarded
	/// when the document closed.
	/// </remarks>
	internal void Cancel(string filePath)
	{
		_resultIds.TryRemove(filePath, out _);
		_requests.Cancel(filePath);
	}

	/// <summary>Cancels every in-flight pull and drops every threaded result id.</summary>
	internal void CancelAll()
	{
		_resultIds.Clear();
		_requests.CancelAll();
	}

	private async Task PullTrackedAsync(CancellationToken cancellationToken)
	{
		if (_context.Disposal.IsDisposed || _context.Client is not { NegotiatedCapabilities.SupportsPullDiagnostics: true })
			return;

		await _requests.RunForOpenDocumentsAsync(PullAsync, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Pulls the diagnostics for a tracked document, superseding any in-flight pull for the same document.</summary>
	/// <param name="document">The tracked document snapshot to pull diagnostics for.</param>
	/// <param name="cancellationToken">A token that can cancel the pull.</param>
	/// <remarks>
	/// A failed round trip keeps the cached diagnostics: the last known report is a better fallback than clearing
	/// the document until the next successful pull. An unchanged report also keeps the cache, because the server reports that nothing changed
	/// since the request's <c>previousResultId</c>.
	/// </remarks>
	private async Task PullAsync(DocumentSnapshot document, CancellationToken cancellationToken)
	{
		if (_context.Client is not { NegotiatedCapabilities.SupportsPullDiagnostics: true })
			return;

		await _requests
			.RunAsync(
				document,

				async effectiveToken =>
				{
					DocumentDiagnosticReportResponse? report = await SendRequestAsync(document, effectiveToken).ConfigureAwait(false);

					if (_context.Disposal.IsDisposed || effectiveToken.IsCancellationRequested)
						return true;

					// A rejected or timed-out pull answers the fallback null, so the coordinator pulls again while the document keeps its version.
					if (report is null)
						return false;

					ApplyReport(document, report);
					return true;
				},

				cancellationToken
			)
			.ConfigureAwait(false);
	}

	private Task<DocumentDiagnosticReportResponse?> SendRequestAsync(DocumentSnapshot document, CancellationToken cancellationToken)
	{
		return _requestRunner.SendAsync<DocumentDiagnosticReportResponse?>(
			method: LspMethodNames.DocumentDiagnostic,
			parameters: new DocumentDiagnosticParams(new TextDocumentIdentifier(document.Uri), GetResultId(document.FilePath)),
			fallbackValue: null,
			cancellationToken
		);
	}

	/// <summary>Applies one pull-diagnostics report to the diagnostics cache and the update event.</summary>
	/// <param name="document">The tracked document snapshot the report was requested for.</param>
	/// <param name="report">The report the server returned.</param>
	/// <remarks>
	/// A full report is fed through the same mapping and raise path the push notification uses, so push and pull delivery share one mapping
	/// and caching path. The report is parsed against the request-time snapshot, so a document that changed while the request was in flight
	/// is fenced out by the same version check the push path relies on. Related reports name documents the server resolved on this request's behalf;
	/// each is applied when it maps to a tracked document.
	/// </remarks>
	private void ApplyReport(DocumentSnapshot document, DocumentDiagnosticReportResponse report)
	{
		ApplyReportCore(document, report);

		if (report.RelatedDocuments is not { Count: > 0 } relatedDocuments)
			return;

		foreach (KeyValuePair<string, DocumentDiagnosticReportResponse> related in relatedDocuments)
		{
			if (!DocumentUriConversion.TryGetLocalPath(related.Key, out string relatedFilePath))
				continue;

			// An untracked related document has no content snapshot to map offsets against; it is skipped like the
			// push path skips diagnostics for documents that are not tracked locally.
			if (_context.Documents.GetDocumentSnapshot(relatedFilePath) is not { } relatedDocument)
				continue;

			ApplyReportCore(relatedDocument, related.Value);
		}
	}

	/// <summary>Applies one report to its own document without following the related documents.</summary>
	/// <param name="document">The tracked document snapshot the report was requested for.</param>
	/// <param name="report">The report the server returned.</param>
	internal void ApplyReportCore(DocumentSnapshot document, DocumentDiagnosticReportResponse report)
	{
		string filePath = document.FilePath;

		// An unchanged report means the request's previousResultId still describes the document: the cache is
		// already correct, and only the reported id needs to be threaded forward. The id is stored only while the
		// request-time snapshot is still current: a change that landed while the pull was in flight makes the
		// reported id describe older content, and storing it would make the next request send a stale
		// previousResultId. The full-report branch is protected the same way by the tracker's version fence.
		if (report.IsUnchanged)
		{
			if (IsCurrentDocumentSnapshot(document))
				StoreResultId(filePath, report.ResultId);

			return;
		}

		if (!report.IsFull)
		{
			_context.Logger.LogDebug(
				"{DisplayName} pull diagnostics report for '{FilePath}' had an unrecognized kind '{Kind}' and was dropped.",
				_context.ProviderDisplayName,
				filePath,
				report.Kind
			);

			return;
		}

		var parameters = new PublishDiagnosticsParams(document.Uri, document.Version, report.Items);

		IReadOnlyList<Diagnostic>? diagnostics = _mapAndStore(filePath, parameters, document);

		if (_context.Disposal.IsDisposed || diagnostics is null)
			return;

		StoreResultId(filePath, report.ResultId);
		_raiseUpdated(document.DocumentId, diagnostics);
	}

	/// <summary>Determines whether a request-time document snapshot still matches the tracked document.</summary>
	/// <param name="document">The snapshot the in-flight pull was issued against.</param>
	/// <returns>
	/// <see langword="true"/> when the tracked document still has the same synchronization version, meaning no change landed while the pull
	/// was in flight.
	/// </returns>
	private bool IsCurrentDocumentSnapshot(DocumentSnapshot document) =>
		_context.Documents.GetDocumentSnapshot(document.FilePath) is { } current && current.Version == document.Version;

	private string? GetResultId(string filePath) => _resultIds.TryGetValue(filePath, out string? resultId) ? resultId : null;

	private void StoreResultId(string filePath, string? resultId)
	{
		// An absent resultId leaves the last threaded id in place: without a replacement the next request
		// must still advertise the last id the server acknowledged, not silently drop to a full report.
		if (string.IsNullOrEmpty(resultId))
			return;

		_resultIds[filePath] = resultId;
	}
}

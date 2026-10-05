using System.Collections.ObjectModel;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Produces a language-server session's diagnostics from the payloads the server publishes and from the pull requests the provider sends.
/// </summary>
/// <remarks>
/// <para>
/// A published payload and a pulled report share one mapping and caching path, so both reach <see cref="DiagnosticsUpdated"/> through the mapping
/// policy the provider supplies. The pull side is a <see cref="DiagnosticsPullCoordinator"/>, which runs only against a server that advertises
/// pull diagnostics.
/// </para>
/// </remarks>
internal sealed class DiagnosticsSource : IDiagnosticsSource, IServerPayloadRefresher
{
	private readonly ProviderContext _context;
	private readonly ProviderEvents _events;
	private readonly DiagnosticMappingPolicy? _diagnosticMappingPolicy;

	/// <param name="context">The client, the tracked documents and the logger the provider shares.</param>
	/// <param name="requestRunner">The runner that sends each pull request under the timeout and restart policy.</param>
	/// <param name="diagnosticMappingPolicy">
	/// The language-specific mapping rules, or <see langword="null"/> when the profile supplies none and every payload is dropped.
	/// </param>
	/// <param name="scheduler">The scheduler whose active context a detached pull must not inherit.</param>
	/// <param name="events">The events that raise <see cref="DiagnosticsUpdated"/>.</param>
	internal DiagnosticsSource(
		ProviderContext context,
		RequestRunner requestRunner,
		DiagnosticMappingPolicy? diagnosticMappingPolicy,
		DocumentOperationScheduler scheduler,
		ProviderEvents events
	)
	{
		_context = context;
		_events = events;
		_diagnosticMappingPolicy = diagnosticMappingPolicy;

		Pulls = new DiagnosticsPullCoordinator(
			context,
			requestRunner,
			scheduler,
			mapAndStore: HandlePayload,
			raiseUpdated: events.RaiseDiagnosticsUpdated
		);
	}

	/// <summary>The coordinator of the pull side, for tests that apply a report to it directly.</summary>
	internal DiagnosticsPullCoordinator Pulls { get; }

	/// <inheritdoc/>
	public event EventHandler<DiagnosticsUpdatedEventArgs>? DiagnosticsUpdated
	{
		add => _events.DiagnosticsUpdated += value;
		remove => _events.DiagnosticsUpdated -= value;
	}

	/// <inheritdoc/>
	public IReadOnlyList<Diagnostic> GetDiagnostics(string documentId)
	{
		if (_context.Disposal.IsDisposed)
			return [];

		if (!_context.TryNormalizeDocumentEntryPath(documentId, "Diagnostics read", out string? normalizedFilePath))
			return [];

		return _context.Documents.GetDiagnostics(normalizedFilePath);
	}

	/// <summary>Queues a pull-diagnostics request for a document that was synchronized or reopened.</summary>
	/// <param name="document">The synchronized tracked document snapshot.</param>
	public void Trigger(DocumentSnapshot document) => Pulls.Trigger(document);

	/// <summary>Pulls the diagnostics of every open document after a pull-only server asks the client to re-pull.</summary>
	/// <param name="sender">The client that raised the event.</param>
	/// <param name="e">The event arguments.</param>
	public void HandleRefreshRequested(object? sender, EventArgs e) => Pulls.RequestRefresh();

	/// <summary>Raises <see cref="DiagnosticsUpdated"/> with the diagnostics cached for a document that is renamed to a new path.</summary>
	/// <param name="filePath">The normalized path the document is renamed to.</param>
	public void AnnounceRenamed(string filePath) =>
		_events.RaiseDiagnosticsUpdated(_context.Documents.GetDocumentId(filePath), _context.Documents.GetDiagnostics(filePath));

	/// <summary>Cancels the in-flight pull for one document path and drops the result id threaded for it.</summary>
	/// <param name="filePath">The normalized document path.</param>
	public void Cancel(string filePath) => Pulls.Cancel(filePath);

	/// <summary>Cancels every in-flight pull and drops every threaded result id.</summary>
	public void CancelAll() => Pulls.CancelAll();

	/// <summary>Maps and stores the diagnostics a server published, then raises <see cref="DiagnosticsUpdated"/> for the document.</summary>
	/// <param name="sender">The client that raised the event.</param>
	/// <param name="eventArgs">The published payload.</param>
	internal void HandlePublished(object? sender, DiagnosticsPublishedEventArgs eventArgs)
	{
		if (_context.Disposal.IsDisposed)
			return;

		PublishDiagnosticsParams parameters = eventArgs.Parameters;

		if (!DocumentUriConversion.TryGetLocalPath(parameters.Uri, out string filePath))
		{
			_context.Logger.LogDebug(
				"{DisplayName} diagnostics for URI '{Uri}' could not be matched to a local file path.",
				_context.ProviderDisplayName,
				parameters.Uri
			);

			return;
		}

		DocumentSnapshot? document = _context.Documents.GetDocumentSnapshot(filePath);

		// Diagnostics for documents that are not tracked locally are ignored: without a tracked snapshot, there is no
		// synchronized content to map them to document ranges, and reading the file from disk on the LSP read loop only to
		// discard the result is wasteful.
		if (document is null)
			return;

		IReadOnlyList<Diagnostic>? diagnostics = HandlePayload(filePath, parameters, document);

		if (_context.Disposal.IsDisposed || diagnostics is null)
			return;

		_events.RaiseDiagnosticsUpdated(document.DocumentId, diagnostics);
	}

	/// <summary>Maps a payload for a tracked document with the provider's policy and stores it, containing a fault of that policy.</summary>
	/// <param name="normalizedFilePath">The normalized local file path that the payload was matched to.</param>
	/// <param name="parameters">The published or pulled diagnostics payload.</param>
	/// <param name="document">The tracked document snapshot the payload belongs to.</param>
	/// <returns>
	/// The diagnostics to raise, or <see langword="null"/> when the payload is dropped: the provider supplies no policy,
	/// the payload could not be parsed or is stale for the tracked version, the provider is disposed, or a newer payload superseded
	/// it before it was stored.
	/// </returns>
	/// <remarks>
	/// The returned list is the owned immutable snapshot the tracker caches, so it stays valid after the call returns.
	/// </remarks>
	private IReadOnlyList<Diagnostic>? HandlePayload(string normalizedFilePath, PublishDiagnosticsParams parameters, DocumentSnapshot document)
	{
		return CallbackContainment.Invoke<IReadOnlyList<Diagnostic>?>(
			callback: () => MapAndStorePayload(normalizedFilePath, parameters, document),
			fallbackValue: null,

			reportFault: exception =>
				_context.Logger.LogWarning(
					exception,
					"The diagnostics payload handling hook failed for the {DisplayName} provider; the failure is contained.",
					_context.ProviderDisplayName
				)
		);
	}

	private ReadOnlyCollection<Diagnostic>? MapAndStorePayload(
		string normalizedFilePath,
		PublishDiagnosticsParams parameters,
		DocumentSnapshot document
	)
	{
		if (_diagnosticMappingPolicy is not { } policy)
			return null;

		if (
			!DiagnosticsParser.TryParse(
				parameters,
				normalizedFilePath,
				content: document.Content,
				documentVersion: document.Version,
				policy,
				out ParsedDiagnostics? publishedDiagnostics
			)
		)
		{
			_context.Logger.LogDebug(
				"{DisplayName} diagnostics payload for '{FilePath}' was stale for the tracked document version and was dropped.",
				_context.ProviderDisplayName,
				normalizedFilePath
			);

			return null;
		}

		if (_context.Disposal.IsDisposed)
			return null;

		if (!_context.Documents.TryStoreDiagnostics(publishedDiagnostics, document.Version, document.Content))
		{
			_context.Logger.LogDebug(
				"{DisplayName} diagnostics payload for '{FilePath}' was superseded before it could be stored and was dropped.",
				_context.ProviderDisplayName,
				normalizedFilePath
			);

			return null;
		}

		return publishedDiagnostics.Diagnostics;
	}
}

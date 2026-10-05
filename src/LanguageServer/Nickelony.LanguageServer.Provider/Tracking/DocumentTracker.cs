namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Tracks the documents a provider mirrors to its language server: their content, version and leases, and the payloads the server published for them.
/// </summary>
/// <remarks>
/// <para>
/// One lock guards every tracked document, and the tracker never blocks inside it on work that needs another thread to take that lock.
/// The lock is shared with the <see cref="TrackedDocumentSet"/> that holds the records and with the <see cref="DocumentSynchronizationCommitter"/>
/// that commits a synchronization.
/// </para>
/// <para>
/// The cached payload members normalize the supplied path, so a caller may pass a raw or a normalized one.
/// </para>
/// <para>
/// Path arguments are validated strictly: a <see langword="null"/> path throws <see cref="ArgumentNullException"/> and a path that cannot be resolved
/// to a local file path throws <see cref="ArgumentException"/>, <see cref="PathTooLongException"/>, or <see cref="NotSupportedException"/>,
/// as <see cref="LocalPath.Normalize(string)"/> documents. This is deliberately stricter than the provider read
/// surfaces (<c>GetDiagnostics</c>/<c>GetSemanticTokens</c>), which degrade an unusable path to an empty result because a host supplies their input;
/// the tracker's callers pass paths they already own, so an invalid one is a programming error.
/// </para>
/// </remarks>
internal sealed class DocumentTracker
{
	// Tracker state is keyed by normalized file path and guarded by _syncRoot.
	private readonly object _syncRoot = new();

	private readonly LocalPathComparisonPolicy _pathComparison;
	private readonly TrackedDocumentSet _documents;
	private readonly DocumentSynchronizationCommitter _committer;

	/// <param name="options">
	/// The provider options whose path comparison decides which paths identify one document, or <see langword="null"/> for the defaults.
	/// </param>
	internal DocumentTracker(LanguageServerFeatureProviderOptions? options = null)
	{
		_pathComparison = LanguageServerFeatureProviderOptions.PathComparisonOf(options);
		_documents = new TrackedDocumentSet(_syncRoot, _pathComparison);
		_committer = new DocumentSynchronizationCommitter(_syncRoot, _documents);
	}

	/// <summary>Returns the current snapshot for a tracked document.</summary>
	/// <param name="filePath">The local file path of the document.</param>
	/// <returns>The current snapshot, or <see langword="null"/> when the document is not tracked.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="filePath"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="filePath"/> is empty or whitespace-only, or the path is invalid on the current platform.
	/// </exception>
	internal DocumentSnapshot? GetDocumentSnapshot(string filePath) => WithTrackedDocument(filePath, static state => state.CreateSnapshot(), default);

	/// <summary>Returns the id that the provider's events carry for a document.</summary>
	/// <param name="filePath">The local file path of the document.</param>
	/// <returns>The id the host supplied for the tracked document, or <paramref name="filePath"/> when the document is not tracked.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="filePath"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="filePath"/> is empty or whitespace-only, or the path is invalid on the current platform.
	/// </exception>
	internal string GetDocumentId(string filePath) => WithTrackedDocument(filePath, static state => state.DocumentId, filePath);

	/// <summary>Returns the tracked document's current content without building a snapshot.</summary>
	/// <param name="filePath">The local file path of the document.</param>
	/// <param name="content">
	/// When this method returns, contains the tracked document's current content, or <see langword="null"/> when the document is not tracked.
	/// </param>
	/// <returns><see langword="true"/> when the document is tracked; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="filePath"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="filePath"/> is empty or whitespace-only, or the path is invalid on the current platform.
	/// </exception>
	/// <remarks>
	/// A content comparison does not need the path, URI, and version a <see cref="DocumentSnapshot"/> carries, so this predicate avoids the snapshot
	/// allocation a content-only check would otherwise pay on the completion-resolve hot path.
	/// </remarks>
	internal bool TryGetContent(string filePath, out string? content)
	{
		content = WithTrackedDocument<string?>(filePath, static state => state.Content, null);
		return content is not null;
	}

	/// <summary>Indicates whether a document is currently tracked.</summary>
	/// <param name="filePath">The local file path of the document.</param>
	/// <returns><see langword="true"/> when the document is tracked; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="filePath"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="filePath"/> is empty or whitespace-only, or the path is invalid on the current platform.
	/// </exception>
	/// <remarks>
	/// Prefer this predicate over testing <see cref="GetDocumentSnapshot"/> for <see langword="null"/> when the snapshot is not needed:
	/// it performs no snapshot allocation.
	/// </remarks>
	internal bool IsTracked(string filePath) => IsTrackedNormalized(LocalPath.Normalize(filePath));

	/// <summary>Indicates whether a document is currently tracked, using a path the caller has already normalized.</summary>
	/// <param name="normalizedFilePath">The normalized local file path of the document.</param>
	/// <returns><see langword="true"/> when the document is tracked; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// Internal fast path for a caller that already holds a normalized path (the provider normalizes each document path once as it enters
	/// the request pipeline); it skips <see cref="LocalPath.Normalize(string)"/>.
	/// </remarks>
	internal bool IsTrackedNormalized(string normalizedFilePath)
	{
		lock (_syncRoot)
			return _documents.Contains(normalizedFilePath);
	}

	/// <summary>Returns snapshots for all documents that are currently considered open, ordered by file path.</summary>
	/// <returns>The open-document snapshots in ascending ordinal file-path order.</returns>
	/// <remarks>
	/// The diagnostics-pull and semantic-token fan-outs each take one such list per refresh, which is the deliberate cost of snapshotting the open
	/// set while still holding the tracker lock only briefly.
	/// </remarks>
	internal IReadOnlyList<DocumentSnapshot> GetOpenDocuments()
	{
		lock (_syncRoot)
			return _documents.GetOpenSnapshots();
	}

	/// <inheritdoc cref="DocumentSynchronizationCommitter.Synchronize"/>
	internal DocumentSynchronizationDirective? SynchronizeNormalized(
		string normalizedFilePath,
		string content,
		DocumentLeaseKinds leases,
		bool includeChangeRange,
		DocumentRequestLease? requestLease,
		string? documentId = null
	) => _committer.Synchronize(normalizedFilePath, content, leases, includeChangeRange, requestLease, documentId);

	/// <summary>
	/// Releases one temporary request-driven lease acquired through <c>SynchronizeNormalized</c> by identity, so the release also finds the record
	/// after a rename relocated it.
	/// </summary>
	/// <param name="requestLease">The lease bound to the acquisition.</param>
	/// <remarks>
	/// The lease is released exactly once; releasing an unbound lease or releasing the same lease again is a no-op.
	/// The record is not evicted here: idle records are removed by <see cref="Close"/> or <see cref="TrimIdleDocuments"/>.
	/// </remarks>
	internal void ReleaseRequestByLease(DocumentRequestLease requestLease)
	{
		if (!requestLease.TryClaimRelease(out TrackedDocumentState? state))
			return;

		lock (_syncRoot)
		{
			state.Leases.ReleaseRequest();

			if (_documents.TryGet(state.FilePath, out TrackedDocumentState? trackedState) && ReferenceEquals(trackedState, state))
				trackedState.SetLastAccessStamp(_documents.NextAccessStamp());
		}
	}

	/// <summary>Evicts the oldest idle documents until at most <paramref name="maxCount"/> idle documents remain tracked.</summary>
	/// <param name="maxCount">The maximum number of idle documents to keep tracked.</param>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="maxCount"/> is negative.</exception>
	/// <remarks>
	/// Idle means the document has no open or request leases, so idle server-open records are also eligible. The snapshots of open evicted
	/// records are returned so the caller can close the server copies; an evicted record that was already closed is removed without a snapshot.
	/// Deliver each close through the same serialization the host uses for that document path (for example the path's per-document scheduler slot)
	/// and re-check the tracked state when it runs: a concurrent open can recreate the record, and an unguarded closing notification would close
	/// a server document that the client considers open again.
	/// </remarks>
	internal IReadOnlyList<DocumentSnapshot> TrimIdleDocuments(int maxCount)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(maxCount);

		lock (_syncRoot)
			return _documents.TrimIdle(maxCount);
	}

	/// <summary>Releases one open lease for <paramref name="filePath"/> and removes the document from local tracking when no leases remain.</summary>
	/// <param name="filePath">The local file path of the document.</param>
	/// <returns>
	/// <see cref="DocumentCloseOutcome.Closed"/> when the tracked
	/// record was removed; <see cref="DocumentCloseOutcome.StillOpen"/> when other host-open leases remain;
	/// <see cref="DocumentCloseOutcome.BusyWithRequests"/> when only temporary request
	/// leases remain and the close is therefore deferred; otherwise, <see cref="DocumentCloseOutcome.Untracked"/>. The closing snapshot is present
	/// only for a <see cref="DocumentCloseOutcome.Closed"/> outcome whose server copy is open.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="filePath"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="filePath"/> is empty or whitespace-only, or the path is invalid on the current platform.
	/// </exception>
	/// <remarks>
	/// A call made after the state has already been removed is a no-op. Any existing idle
	/// record is treated as explicit cleanup even when it has no open lease, so this method is also the explicit close path for an idle record.
	/// A <see cref="DocumentCloseOutcome.BusyWithRequests"/> outcome means the caller should retry the close once the request leases are released.
	/// </remarks>
	internal DocumentCloseResult Close(string filePath)
	{
		string normalizedFilePath = LocalPath.Normalize(filePath);

		lock (_syncRoot)
		{
			if (!_documents.TryGet(normalizedFilePath, out TrackedDocumentState? state))
				return new(DocumentCloseOutcome.Untracked, ClosingDocument: null);

			state.Leases.ReleaseOpen();

			if (state.Leases.HasOpenLeases)
				return new(DocumentCloseOutcome.StillOpen, ClosingDocument: null);

			if (!state.Leases.IsIdle)
			{
				state.SetLastAccessStamp(_documents.NextAccessStamp());
				return new(DocumentCloseOutcome.BusyWithRequests, ClosingDocument: null);
			}

			DocumentSnapshot? closingDocument = state.IsOpen ? state.CreateSnapshot() : null;

			_documents.Remove(normalizedFilePath);
			return new(DocumentCloseOutcome.Closed, closingDocument);
		}
	}

	/// <summary>Renames a tracked document to a new file path.</summary>
	/// <param name="oldFilePath">The current local file path.</param>
	/// <param name="newFilePath">The replacement local file path.</param>
	/// <param name="content">The latest host content, applied to the renamed record when the rename succeeds.</param>
	/// <param name="newDocumentId">
	/// The id the host renamed the document to, or <see langword="null"/> when it equals the normalized <paramref name="newFilePath"/>.
	/// </param>
	/// <returns>
	/// The rename outcome and, when the rename landed, the request that should be mirrored to the server. Paths that identify the same file
	/// on the current host, an untracked source, and an already-tracked destination each leave every record untouched and carry no request.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="oldFilePath"/> or <paramref name="newFilePath"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="oldFilePath"/> or <paramref name="newFilePath"/> is empty or whitespace-only, or a path is invalid on the current platform.
	/// </exception>
	/// <remarks>
	/// When the old and new paths identify the same file on the current host (for example a case-only rename on a case-insensitive host),
	/// the call is a no-op: the tracked record is neither renamed nor updated, because the server still holds the document under the same URI
	/// and no request could carry the change. Deliver content changes through <see cref="SynchronizeNormalized"/> instead, so the tracked content
	/// stays equal to what the server received and incremental change ranges remain valid.
	/// </remarks>
	internal DocumentRenameResult Rename(string oldFilePath, string newFilePath, string? content = null, string? newDocumentId = null)
	{
		string normalizedOldFilePath = LocalPath.Normalize(oldFilePath);
		string normalizedNewFilePath = LocalPath.Normalize(newFilePath);

		// A same-file rename mirrors nothing to the server. Committing supplied content here would advance the
		// tracked content without any request reaching the server, so a later incremental change would compute its
		// range against text the server never received.
		if (_pathComparison.Comparer.Equals(normalizedOldFilePath, normalizedNewFilePath))
			return new(DocumentRenameOutcome.PathsEquivalent, null);

		lock (_syncRoot)
		{
			if (!_documents.TryGet(normalizedOldFilePath, out TrackedDocumentState? state))
				return new(DocumentRenameOutcome.SourceUntracked, null);

			if (_documents.Contains(normalizedNewFilePath))
				return new(DocumentRenameOutcome.DestinationTracked, null);

			string safeContent = content ?? state.Content;
			bool contentChanged = !string.Equals(state.Content, safeContent, StringComparison.Ordinal);
			DocumentSnapshot? previousDocument = state.IsOpen ? state.CreateSnapshot() : null;

			// The URI conversion is the one step that can throw, so it runs before the entry leaves the set: the
			// invariant that an entry's key always equals its state's path (trimming removes entries by state.FilePath)
			// then holds on every path.
			string newUri = DocumentUriConversion.CreateFileUri(normalizedNewFilePath);

			_documents.Remove(normalizedOldFilePath);
			state.RenameDocument(normalizedNewFilePath, newUri, newDocumentId);

			if (contentChanged)
			{
				state.ReplaceContent(safeContent);

				// The cached payloads were produced for the previous content, so they would describe text that is gone.
				state.ClearPayloadCaches();
			}

			_documents.Set(normalizedNewFilePath, state);

			return new(DocumentRenameOutcome.Renamed, new(previousDocument, state.CreateSnapshot(), previousDocument is not null));
		}
	}

	/// <summary>Marks every tracked document as locally closed before a language-server restart.</summary>
	/// <returns>The snapshots that should be reopened on the next successful start.</returns>
	/// <remarks>
	/// Replay these snapshots through <see cref="ReopenAfterRestart"/> after the restart: a document that was closed or retracked while the restart
	/// was in flight must not be reopened on the server.
	/// </remarks>
	internal IReadOnlyList<DocumentSnapshot> PrepareForRestart()
	{
		lock (_syncRoot)
			return _documents.MarkAllClosedForRestart();
	}

	/// <summary>
	/// Reopens one server-open document captured by <see cref="PrepareForRestart"/> when it is still tracked, still has an open-document lease,
	/// and is not open on the server again yet.
	/// </summary>
	/// <param name="filePath">The local file path of the document to reopen.</param>
	/// <returns>
	/// A synchronization directive for the current tracked content, or <see langword="null"/> when the document is untracked, has no open-document
	/// lease (it was closed while the restart was in flight), or is already open on the server again.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="filePath"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="filePath"/> is empty or whitespace-only, or the path is invalid on the current platform.
	/// </exception>
	/// <remarks>
	/// This is the guarded replay counterpart of <see cref="PrepareForRestart"/>. Unlike <see cref="SynchronizeNormalized"/> it never creates
	/// a new tracked record, so replaying a snapshot for a document the host stopped tracking cannot leave a phantom server-open document behind.
	/// </remarks>
	internal DocumentSynchronizationDirective? ReopenAfterRestart(string filePath)
	{
		string normalizedFilePath = LocalPath.Normalize(filePath);

		lock (_syncRoot)
		{
			if (!_documents.TryGet(normalizedFilePath, out TrackedDocumentState? state))
				return null;

			if (state.IsOpen || !state.Leases.HasOpenLeases)
				return null;

			state.ReopenDocument(state.Content);
			return new(DocumentSynchronizationKind.Open, state.CreateSnapshot());
		}
	}

	/// <summary>Reads one feature's payload cache of a tracked document under the tracker lock.</summary>
	/// <typeparam name="TCache">The cache type of the slot.</typeparam>
	/// <typeparam name="TResult">The type the read returns.</typeparam>
	/// <param name="filePath">The local file path of the document.</param>
	/// <param name="slot">The slot that names the feature's cache.</param>
	/// <param name="read">Reads the cache; it runs under the tracker lock, so it must not call back into the tracker.</param>
	/// <param name="fallbackValue">The value returned when the document is not tracked.</param>
	/// <returns>The value the read returned, or <paramref name="fallbackValue"/> when the document is not tracked.</returns>
	internal TResult ReadPayload<TCache, TResult>(string filePath, PayloadSlot<TCache> slot, Func<TCache, TResult> read, TResult fallbackValue)
		where TCache : class, IPayloadCache, new() => WithTrackedDocument(filePath, state => read(state.GetPayloadCache(slot)), fallbackValue);

	/// <summary>Stores a payload into one feature's cache when it is not stale for the tracked document version.</summary>
	/// <typeparam name="TCache">The cache type of the slot.</typeparam>
	/// <param name="filePath">The local file path of the document.</param>
	/// <param name="slot">The slot that names the feature's cache.</param>
	/// <param name="expectedDocumentVersion">The tracked document version observed when the payload was produced.</param>
	/// <param name="store">Stores the payload into the cache and reports whether the cache accepted it; it runs under the tracker lock.</param>
	/// <returns><see langword="true"/> when the payload was stored; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// The producer compared the payload version against the snapshot it read. Inside the tracked-document lock, the first check
	/// fences the produce-to-store window: the tracked version must still equal the snapshot version, so a payload produced against an older snapshot
	/// cannot overwrite a payload stored for a newer one. The cache then applies its own version rule.
	/// </remarks>
	internal bool TryStorePayload<TCache>(string filePath, PayloadSlot<TCache> slot, int expectedDocumentVersion, Func<TCache, bool> store)
		where TCache : class, IPayloadCache, new()
	{
		return WithTrackedDocument(
			filePath,
			state => DocumentVersionPolicy.IsPayloadCurrent(state.Version, expectedDocumentVersion) && store(state.GetPayloadCache(slot)),
			fallbackValue: false
		);
	}

	/// <summary>Marks the specified document as needing a fresh server-side open/sync before incremental updates can resume.</summary>
	/// <param name="filePath">The local file path of the document.</param>
	/// <returns><see langword="true"/> when the document was found and invalidated; otherwise, <see langword="false"/>.</returns>
	internal bool InvalidateServerSynchronization(string filePath)
	{
		return WithTrackedDocument(
			filePath,

			state =>
			{
				state.MarkDocumentClosed();

				// The server-side synchronization is unknown after the invalidation, so each payload cache drops its version stamp and
				// the next payload of any kind is accepted regardless of the version it reports.
				state.ResetPayloadVersionStamps();

				return true;
			},

			fallbackValue: false
		);
	}

	/// <summary>Executes a callback against a tracked document while holding the tracker lock.</summary>
	/// <typeparam name="TResult">The callback result type.</typeparam>
	/// <param name="filePath">The local file path of the document.</param>
	/// <param name="accessTrackedDocument">The callback to execute when the document exists.</param>
	/// <param name="fallbackValue">The result to return when the document is not tracked.</param>
	/// <returns>The callback result, or <paramref name="fallbackValue"/> when no document is tracked.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="filePath"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="filePath"/> is empty or whitespace-only, or the path is invalid on the current platform.
	/// </exception>
	private TResult WithTrackedDocument<TResult>(string filePath, Func<TrackedDocumentState, TResult> accessTrackedDocument, TResult fallbackValue)
	{
		string normalizedFilePath = LocalPath.Normalize(filePath);

		lock (_syncRoot)
			return _documents.TryGet(normalizedFilePath, out TrackedDocumentState? state) ? accessTrackedDocument(state) : fallbackValue;
	}
}

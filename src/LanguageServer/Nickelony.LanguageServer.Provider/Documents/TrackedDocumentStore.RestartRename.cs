namespace Nickelony.LanguageServer.Provider;

public abstract partial class TrackedDocumentStore
{
	/// <summary>
	/// Rekeys a tracked document to a new file path.
	/// </summary>
	/// <param name="oldFilePath">The current local file path.</param>
	/// <param name="newFilePath">The replacement local file path.</param>
	/// <param name="content">The latest host content, applied to the rekeyed record when the rekey succeeds.</param>
	/// <returns>
	/// The rename outcome and, when the record was rekeyed, the request that should be mirrored to the server.
	/// Paths that identify the same file on the current host, an untracked source, and an already-tracked
	/// destination each leave every record untouched and carry no request.
	/// </returns>
	/// <remarks>
	/// When both paths identify the same file on the current host (for example a case-only rename on a
	/// case-insensitive host), the call is a no-op: the tracked record is neither rekeyed nor updated, because the
	/// server still holds the document under the same URI and no request could carry the change. Deliver content
	/// changes through <see cref="Synchronize"/> instead, so the tracked content stays equal to what the server
	/// received and incremental change ranges remain valid.
	/// </remarks>
	/// <exception cref="ArgumentNullException"><paramref name="oldFilePath"/> or <paramref name="newFilePath"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException"><paramref name="oldFilePath"/> or <paramref name="newFilePath"/> is empty or whitespace-only, or a path is invalid on the current platform.</exception>
	public DocumentRenameResult Rename(string oldFilePath, string newFilePath, string? content = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(oldFilePath);
		ArgumentException.ThrowIfNullOrWhiteSpace(newFilePath);

		string normalizedOldFilePath = LanguageServerPaths.NormalizeLocalPath(oldFilePath);
		string normalizedNewFilePath = LanguageServerPaths.NormalizeLocalPath(newFilePath);

		// A same-file rename mirrors nothing to the server. Committing supplied content here would advance the
		// tracked content without any request reaching the server, so a later incremental change would compute its
		// range against text the server never received.
		if (LanguageServerPaths.AreLocalPathsEqual(normalizedOldFilePath, normalizedNewFilePath))
			return new(DocumentRenameOutcome.PathsEquivalent, null);

		lock (_syncRoot)
		{
			if (!_documents.TryGetValue(normalizedOldFilePath, out TrackedDocumentState? state))
				return new(DocumentRenameOutcome.SourceUntracked, null);

			if (_documents.ContainsKey(normalizedNewFilePath))
				return new(DocumentRenameOutcome.DestinationTracked, null);

			string safeContent = content ?? state.Content;
			bool contentChanged = !string.Equals(state.Content, safeContent, StringComparison.Ordinal);
			DocumentSnapshot? previousDocument = state.IsOpen ? state.CreateSnapshot() : null;

			_documents.Remove(normalizedOldFilePath);

			try
			{
				RenameTrackedDocumentState(state, normalizedNewFilePath, LanguageServerPaths.CreateFileUri(normalizedNewFilePath));

				if (contentChanged)
					ReplaceTrackedDocumentContent(state, safeContent);
			}
			catch
			{
				// A throwing mutation must not leave the document unreachable from the store. The derived rename may already
				// have applied, so restore the entry under the state's current path: the store invariant is that the
				// dictionary key always equals state.FilePath (trimming removes entries by state.FilePath).
				_documents[state.FilePath] = state;
				throw;
			}

			_documents[normalizedNewFilePath] = state;

			// The hook runs after the rekey is committed so a throwing override cannot drop the record; the rename
			// itself stays applied and the exception propagates to the caller.
			OnTrackedDocumentRenamed(state, contentChanged);

			return new(
				DocumentRenameOutcome.Renamed,
				new(previousDocument, state.CreateSnapshot(), previousDocument is not null));
		}
	}

	/// <summary>
	/// Marks every tracked document as locally closed before a language-server restart.
	/// </summary>
	/// <returns>The snapshots that should be reopened on the next successful start.</returns>
	/// <remarks>
	/// Replay these snapshots through <see cref="TryReopenTrackedDocument"/> after the restart: a document that was
	/// closed or retracked while the restart was in flight must not be reopened on the server.
	/// </remarks>
	public IReadOnlyList<DocumentSnapshot> PrepareForRestart()
	{
		lock (_syncRoot)
		{
			var documentsToReopen = new List<DocumentSnapshot>();

			foreach (TrackedDocumentState state in _documents.Values)
			{
				MarkTrackedDocumentClosed(state);

				if (state.References.HasOpenReferences)
					documentsToReopen.Add(state.CreateSnapshot());
			}

			return documentsToReopen;
		}
	}

	/// <summary>
	/// Reopens one previously captured server-open document after a restart when it is still tracked, still has an
	/// open-document reference, and is not open on the server again yet.
	/// </summary>
	/// <param name="filePath">The local file path of the document to reopen.</param>
	/// <returns>
	/// An open synchronization request for the current tracked content, or <see langword="null"/> when the document
	/// is untracked, has no open-editor reference (it was closed while the restart was in flight), or is already open
	/// on the server again.
	/// </returns>
	/// <remarks>
	/// This is the guarded replay counterpart of <see cref="PrepareForRestart"/>. Unlike <see cref="Synchronize"/> it
	/// never creates a new tracked record, so replaying a snapshot for a document the host no longer owns cannot
	/// leave a phantom server-open document behind.
	/// </remarks>
	/// <exception cref="ArgumentNullException"><paramref name="filePath"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException"><paramref name="filePath"/> is empty or whitespace-only, or the path is invalid on the current platform.</exception>
	public DocumentSynchronizationRequest? TryReopenTrackedDocument(string filePath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

		string normalizedFilePath = LanguageServerPaths.NormalizeLocalPath(filePath);

		lock (_syncRoot)
		{
			if (!_documents.TryGetValue(normalizedFilePath, out TrackedDocumentState? state))
				return null;

			if (state.IsOpen || !state.References.HasOpenReferences)
				return null;

			ReopenTrackedDocumentState(state, state.Content);
			return new(DocumentSynchronizationKind.Open, state.CreateSnapshot());
		}
	}
}

namespace Nickelony.LanguageServer.Provider;

public abstract partial class TrackedDocumentStore
{
	/// <summary>
	/// Gets the current snapshot for a tracked document.
	/// </summary>
	/// <param name="filePath">The local file path of the document.</param>
	/// <returns>The current snapshot, or <see langword="null"/> when the document is not tracked.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="filePath"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException"><paramref name="filePath"/> is empty or whitespace-only, or the path is invalid on the current platform.</exception>
	public DocumentSnapshot? GetDocumentSnapshot(string filePath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
		return WithTrackedDocument(filePath, static state => state.CreateSnapshot(), default);
	}

	/// <summary>
	/// Gets a value indicating whether a document is currently tracked.
	/// </summary>
	/// <param name="filePath">The local file path of the document.</param>
	/// <returns><see langword="true"/> when the document is tracked; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="filePath"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException"><paramref name="filePath"/> is empty or whitespace-only, or the path is invalid on the current platform.</exception>
	/// <remarks>
	/// Prefer this predicate over testing <see cref="GetDocumentSnapshot"/> for <see langword="null"/> when the
	/// snapshot is not needed: it performs no snapshot allocation.
	/// </remarks>
	public bool IsTracked(string filePath)
		=> IsTrackedNormalized(LanguageServerPaths.NormalizeLocalPath(filePath));

	/// <summary>
	/// Gets a value indicating whether a document is currently tracked, using a path the caller has already normalized.
	/// </summary>
	/// <param name="normalizedFilePath">The normalized local file path of the document.</param>
	/// <returns><see langword="true"/> when the document is tracked; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// Internal fast path for a caller that already holds a normalized path (the provider normalizes each document
	/// path once as it enters the request pipeline); it skips <see cref="LanguageServerPaths.NormalizeLocalPath(string)"/>.
	/// </remarks>
	/// <exception cref="ArgumentNullException"><paramref name="normalizedFilePath"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException"><paramref name="normalizedFilePath"/> is empty or whitespace-only.</exception>
	internal bool IsTrackedNormalized(string normalizedFilePath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(normalizedFilePath);

		lock (_syncRoot)
		{
			return _documents.ContainsKey(normalizedFilePath);
		}
	}

	/// <summary>
	/// Gets snapshots for all documents that are currently considered open, ordered by file path.
	/// </summary>
	/// <returns>The open-document snapshots in ascending ordinal file-path order.</returns>
	public IReadOnlyList<DocumentSnapshot> GetOpenDocuments()
	{
		lock (_syncRoot)
		{
			var documents = new List<DocumentSnapshot>();

			foreach (TrackedDocumentState state in _documents.Values)
			{
				if (state.IsOpen)
					documents.Add(state.CreateSnapshot());
			}

			documents.Sort(static (left, right) => string.CompareOrdinal(left.FilePath, right.FilePath));
			return documents;
		}
	}

	/// <summary>
	/// Executes a callback against a tracked document while holding the store lock.
	/// </summary>
	/// <typeparam name="TResult">The callback result type.</typeparam>
	/// <param name="filePath">The local file path of the document.</param>
	/// <param name="accessTrackedDocument">The callback to execute when the document exists.</param>
	/// <param name="fallbackValue">The result to return when the document is not tracked.</param>
	/// <returns>The callback result, or <paramref name="fallbackValue"/> when no document is tracked.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="filePath"/> or <paramref name="accessTrackedDocument"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException"><paramref name="filePath"/> is empty or whitespace-only, or the path is invalid on the current platform.</exception>
	protected TResult WithTrackedDocument<TResult>(string filePath, Func<TrackedDocumentState, TResult> accessTrackedDocument, TResult fallbackValue)
	{
		ArgumentNullException.ThrowIfNull(filePath);
		ArgumentNullException.ThrowIfNull(accessTrackedDocument);

		string normalizedFilePath = LanguageServerPaths.NormalizeLocalPath(filePath);

		lock (_syncRoot)
		{
			return _documents.TryGetValue(normalizedFilePath, out TrackedDocumentState? state)
				? accessTrackedDocument(state)
				: fallbackValue;
		}
	}

	/// <summary>
	/// Executes a mutating callback against a tracked document while holding the store lock.
	/// </summary>
	/// <param name="filePath">The local file path of the document.</param>
	/// <param name="mutateTrackedDocument">The callback to execute when the document exists.</param>
	/// <exception cref="ArgumentNullException"><paramref name="filePath"/> or <paramref name="mutateTrackedDocument"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException"><paramref name="filePath"/> is empty or whitespace-only.</exception>
	protected void WithTrackedDocument(string filePath, Action<TrackedDocumentState> mutateTrackedDocument)
	{
		ArgumentNullException.ThrowIfNull(filePath);
		ArgumentNullException.ThrowIfNull(mutateTrackedDocument);

		string normalizedFilePath = LanguageServerPaths.NormalizeLocalPath(filePath);

		lock (_syncRoot)
		{
			if (_documents.TryGetValue(normalizedFilePath, out TrackedDocumentState? state))
				mutateTrackedDocument(state);
		}
	}
}

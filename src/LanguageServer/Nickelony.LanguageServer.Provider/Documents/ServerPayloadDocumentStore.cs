using Nickelony.IDEKit.IntelliSense.Diagnostics;

namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// The framework's default tracked-document store: it caches the version-fenced diagnostics and semantic-token
/// payloads a language server sends for a document, alongside the document versions and access stamps the
/// framework's synchronization pipeline maintains.
/// </summary>
/// <remarks>
/// <para>
/// A provider package reaches this type through the framework's <c>InternalsVisibleTo</c> grant; the framework
/// base's <c>documentStore</c> constructor argument stays typed on the public
/// <see cref="TrackedDocumentStore"/>, so the state type is never exposed. A provider that needs different
/// caches supplies its own store instead.
/// </para>
/// <para>
/// Cached reads normalize the supplied path internally, so callers may pass an already-normalized path or a raw
/// one. A null, empty, or platform-invalid path is rejected by that normalization with the same argument
/// exceptions the base store documents, rather than being silently ignored; every in-package caller supplies a
/// valid path.
/// </para>
/// </remarks>
internal sealed class ServerPayloadDocumentStore : TrackedDocumentStore
{
	/// <summary>
	/// Gets the cached diagnostics for the specified file path.
	/// </summary>
	/// <param name="filePath">The local file path of the document.</param>
	/// <returns>
	/// The cached diagnostics, ordered by start offset and then by severity, or an empty list when none are stored.
	/// </returns>
	internal IReadOnlyList<TextDiagnostic> GetDiagnostics(string filePath)
		=> WithPayloadDocument(filePath, static state => (IReadOnlyList<TextDiagnostic>)state.GetDiagnosticsProjection(), fallbackValue: []);

	/// <summary>
	/// Gets the cached diagnostics together with the content snapshot their offsets refer to.
	/// </summary>
	/// <param name="filePath">The local file path of the document.</param>
	/// <returns>
	/// The cached diagnostic entries and the content snapshot the payload was parsed against; an empty list and
	/// <see langword="null"/> when nothing is stored. Consumers that convert the offsets back to positions
	/// must use the returned snapshot, because it can differ from the caller's current document text.
	/// </returns>
	internal (IReadOnlyList<DiagnosticEntry> Diagnostics, string? SourceContent) GetDiagnosticsSnapshot(string filePath)
	{
		return WithPayloadDocument(filePath,
			static state => (state.DiagnosticsCache.Items, state.DiagnosticsCache.SourceContent),
			fallbackValue: ((IReadOnlyList<DiagnosticEntry>)[], null));
	}

	/// <summary>
	/// Gets the cached semantic tokens for the specified file path.
	/// </summary>
	/// <param name="filePath">The local file path of the document.</param>
	/// <returns>The cached semantic tokens, or an empty list when none are stored.</returns>
	internal IReadOnlyList<SemanticToken> GetSemanticTokens(string filePath)
		=> WithPayloadDocument(filePath, static state => state.SemanticTokensCache.Items, fallbackValue: []);

	/// <summary>
	/// Stores a diagnostics payload when it is not stale for the tracked document version.
	/// </summary>
	/// <param name="publishedDiagnostics">The diagnostics payload to cache.</param>
	/// <param name="expectedDocumentVersion">The tracked document version observed when the payload was parsed.</param>
	/// <param name="sourceContent">The content snapshot the diagnostic offsets were resolved against.</param>
	/// <returns><see langword="true"/> when the payload was stored; otherwise, <see langword="false"/>.</returns>
	internal bool TryStoreDiagnostics(PublishedDiagnostics publishedDiagnostics, int expectedDocumentVersion, string sourceContent)
	{
		// The parser compared versions against the snapshot it read; this check runs inside the
		// tracked-document lock to fence the parse-to-store window, so a payload parsed against an
		// older snapshot cannot overwrite diagnostics stored for a newer version in the meantime.
		// A payload with an unknown version (0) is accepted without advancing the cached version; a
		// positive payload must match the tracked version, which is always positive.
		return WithPayloadDocument(
			publishedDiagnostics.FilePath,
			state =>
			{
				if (!DocumentVersionPolicy.IsPayloadCurrent(state.Version, expectedDocumentVersion)
					|| !state.DiagnosticsCache.TryStore(publishedDiagnostics.Version, publishedDiagnostics.Entries, sourceContent))
				{
					return false;
				}

				// Adopt the projection the payload already computed so the cached read and the published payload share
				// one owned snapshot instead of projecting the same entries twice.
				state.AdoptDiagnosticsProjection(publishedDiagnostics.Diagnostics);
				return true;
			},
			fallbackValue: false);
	}

	/// <summary>
	/// Stores semantic tokens when they are not stale for the tracked document version.
	/// </summary>
	/// <param name="filePath">The local file path of the document.</param>
	/// <param name="version">The document version associated with the tokens.</param>
	/// <param name="semanticTokens">The semantic tokens to cache.</param>
	/// <returns><see langword="true"/> when the token set was stored; otherwise, <see langword="false"/>.</returns>
	internal bool TryStoreSemanticTokens(string filePath, int version, IReadOnlyList<SemanticToken> semanticTokens)
	{
		return WithPayloadDocument(
			filePath,
			state => DocumentVersionPolicy.IsPayloadCurrent(state.Version, version)
				&& state.SemanticTokensCache.TryStore(version, semanticTokens),
			fallbackValue: false);
	}

	/// <summary>
	/// Marks the specified document as needing a fresh server-side open/sync before incremental updates can resume.
	/// </summary>
	/// <param name="filePath">The local file path of the document.</param>
	/// <returns><see langword="true"/> when the document was found and invalidated; otherwise, <see langword="false"/>.</returns>
	internal bool InvalidateServerSynchronization(string filePath)
	{
		return WithPayloadDocument(filePath,
			state =>
			{
				MarkTrackedDocumentClosed(state);

				// The server-side synchronization is unknown after the invalidation, so both payload
				// caches drop their version stamps and the next payload of either kind is accepted
				// regardless of the version it reports.
				state.DiagnosticsCache.ResetVersionStamp();
				state.SemanticTokensCache.ResetVersionStamp();
				return true;
			},
			fallbackValue: false);
	}

	/// <inheritdoc/>
	protected override TrackedDocumentState CreateTrackedDocumentState(TrackedDocumentInitialState initialState)
		=> new ServerPayloadDocumentState(initialState);

	/// <inheritdoc/>
	protected override long GetLastAccessStamp(TrackedDocumentState state)
		=> state.LastAccessStamp;

	/// <inheritdoc/>
	protected override void TouchTrackedDocumentState(TrackedDocumentState state, long lastAccessStamp)
		=> ((ServerPayloadDocumentState)state).Touch(lastAccessStamp);

	/// <inheritdoc/>
	protected override void ReopenTrackedDocumentState(TrackedDocumentState state, string content)
		=> ((ServerPayloadDocumentState)state).Reopen(content);

	/// <inheritdoc/>
	protected override string ReplaceTrackedDocumentContent(TrackedDocumentState state, string content)
		=> ((ServerPayloadDocumentState)state).UpdateContent(content);

	/// <inheritdoc/>
	protected override void RenameTrackedDocumentState(TrackedDocumentState state, string filePath, string uri)
		=> ((ServerPayloadDocumentState)state).RenameTo(filePath, uri);

	/// <inheritdoc/>
	protected override void MarkTrackedDocumentClosed(TrackedDocumentState state)
		=> ((ServerPayloadDocumentState)state).MarkClosed();

	/// <inheritdoc/>
	protected override void OnTrackedDocumentRenamed(TrackedDocumentState state, bool contentChanged)
	{
		if (contentChanged)
			ClearCachedState((ServerPayloadDocumentState)state);
	}

	/// <summary>
	/// Executes a callback against the tracked document as the typed payload state while the store holds its lock.
	/// </summary>
	/// <typeparam name="TResult">The callback result type.</typeparam>
	/// <param name="filePath">The local file path of the document.</param>
	/// <param name="accessTrackedDocument">The callback to execute when the document exists.</param>
	/// <param name="fallbackValue">The result to return when the document is not tracked.</param>
	/// <returns>The callback result, or <paramref name="fallbackValue"/> when no document is tracked.</returns>
	private TResult WithPayloadDocument<TResult>(string filePath, Func<ServerPayloadDocumentState, TResult> accessTrackedDocument, TResult fallbackValue)
		=> WithTrackedDocument(filePath, state => accessTrackedDocument((ServerPayloadDocumentState)state), fallbackValue);

	private static void ClearCachedState(ServerPayloadDocumentState state)
	{
		state.DiagnosticsCache.Clear();
		state.SemanticTokensCache.Clear();
	}
}

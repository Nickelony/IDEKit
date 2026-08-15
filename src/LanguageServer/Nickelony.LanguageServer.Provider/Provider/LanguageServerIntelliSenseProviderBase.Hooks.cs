using Nickelony.IDEKit.IntelliSense.Diagnostics;

namespace Nickelony.LanguageServer.Provider;

public abstract partial class LanguageServerIntelliSenseProviderBase
{
	/// <summary>
	/// Gets the provider display name used in log and diagnostic text, for example <c>"Lua"</c>, <c>"C#"</c>, or <c>"Visual Basic"</c>.
	/// </summary>
	/// <remarks>
	/// The name is read on demand by log and diagnostic paths; implement it so it cannot throw.
	/// </remarks>
	protected abstract string ProviderDisplayName { get; }

	/// <summary>
	/// Gets the language identifier sent as the <c>languageId</c> of opened documents.
	/// </summary>
	protected abstract string LanguageId { get; }

	/// <summary>
	/// Reports whether a normalized changed workspace path requires a settings refresh before the watched-files
	/// notification is forwarded to the language server.
	/// </summary>
	/// <param name="normalizedPath">The normalized path of the changed file.</param>
	/// <returns><see langword="true"/> when the path is a configuration file for this language.</returns>
	protected abstract bool IsConfigurationPath(string normalizedPath);

	/// <summary>
	/// Creates the settings payload sent to the language server when a configuration file changes.
	/// </summary>
	/// <returns>The payload for the <c>workspace/didChangeConfiguration</c> notification.</returns>
	protected abstract object CreateSettingsPayload();

	/// <summary>
	/// Creates the message for the startup-failure report raised through <see cref="StartupFailed"/> after the
	/// language server failed to start.
	/// </summary>
	/// <param name="isPersistentFailure">
	/// <see langword="true"/> when the consecutive-failure threshold was reached and the provider entered the
	/// failed state; otherwise, <see langword="false"/> for a transient failure that the provider retries.
	/// </param>
	/// <returns>The message for the host to present or log, or <see langword="null"/> or a blank value to suppress the event.</returns>
	/// <remarks>
	/// The framework combines the returned message with its own retry policy in
	/// <see cref="LanguageServerStartupFailure.IsPersistent"/>, so the reported persistence always matches the
	/// state the provider entered. A throwing implementation is contained and suppresses the event.
	/// </remarks>
	protected abstract string? CreateStartupFailureMessage(bool isPersistentFailure);

	/// <summary>
	/// Creates the message for the persistent startup-failure report raised when no language-server client was configured.
	/// </summary>
	/// <returns>The message for the host to present or log, or <see langword="null"/> or a blank value to suppress the event.</returns>
	/// <remarks>A throwing implementation is contained and suppresses the event.</remarks>
	protected abstract string? CreateMissingClientFailureMessage();

	/// <summary>
	/// Converts a published diagnostics payload for a tracked document and stores it for later retrieval.
	/// </summary>
	/// <param name="filePath">The normalized local file path that the payload was matched to.</param>
	/// <param name="parameters">The published diagnostics payload.</param>
	/// <param name="document">The tracked document snapshot the payload belongs to.</param>
	/// <returns>
	/// The diagnostics to raise through <see cref="DiagnosticsUpdated"/>, or <see langword="null"/> when the
	/// payload should be dropped, for example because it could not be parsed or is stale.
	/// </returns>
	/// <remarks>
	/// <para>
	/// The hook is responsible for storing the payload in the language's caches; the base raises
	/// <see cref="DiagnosticsUpdated"/> when the returned list is not <see langword="null"/> and the provider has
	/// not been disposed in the meantime. The returned list becomes the snapshot the base serves through
	/// <see cref="GetTrackedDiagnostics"/>, so it must be an owned immutable snapshot that is not mutated after
	/// the hook returns.
	/// </para>
	/// <para>
	/// Implementations that cache the result should re-check <see cref="IsDisposed"/> (or the tracked document's
	/// version) before publishing it themselves, because the base raises the event only after the hook returns and
	/// disposal can race the raise.
	/// </para>
	/// <para>
	/// This is an optional feature hook: the default implementation drops the payload and stores nothing, so a
	/// provider that does not expose diagnostics can omit it and <see cref="GetDiagnostics"/> stays empty.
	/// </para>
	/// </remarks>
	protected virtual IReadOnlyList<TextDiagnostic>? HandleDiagnosticsPayload(
		string filePath,
		PublishDiagnosticsParams parameters,
		DocumentSnapshot document)
		=> null;

	/// <summary>
	/// Gets the latest diagnostics cached for a normalized document path.
	/// </summary>
	/// <param name="normalizedFilePath">The normalized local file path of the document.</param>
	/// <returns>The cached diagnostics, or an empty list when none are stored.</returns>
	/// <remarks>
	/// A throwing implementation is contained and treated as an empty result. This is an optional feature hook:
	/// the default implementation returns an empty list, so a provider that does not expose diagnostics can omit it.
	/// </remarks>
	protected virtual IReadOnlyList<TextDiagnostic> GetTrackedDiagnostics(string normalizedFilePath)
		=> [];

	/// <summary>
	/// Gets the cached diagnostics for a document together with the content snapshot their offsets refer to, so a
	/// code-action request can rebuild the server's diagnostic context for the requested range.
	/// </summary>
	/// <param name="filePath">The local file path of the document.</param>
	/// <returns>
	/// The cached diagnostic entries and the content snapshot the payloads were parsed against; an empty list and
	/// <see langword="null"/> when nothing is stored. Consumers that convert the offsets back to positions must use
	/// the returned snapshot, because it can differ from the caller's current document text.
	/// </returns>
	/// <remarks>
	/// This is a framework-internal seam: the member is <see langword="internal"/> and a store-backed provider
	/// package overrides it through the framework's <c>InternalsVisibleTo</c> grant to forward its own cache (the
	/// Lua and Roslyn providers do). The default implementation returns no diagnostics, so a provider that does not
	/// forward a cache sends an empty code-action context. Each entry carries the raw protocol payload the server
	/// sent, which a server derives its quick-fix families from, so the context echoes those fields instead of
	/// rebuilding them.
	/// </remarks>
	internal virtual (IReadOnlyList<DiagnosticEntry> Diagnostics, string? SourceContent) GetDiagnosticsSnapshot(string filePath)
		=> ([], null);

	/// <summary>
	/// Gets the latest semantic tokens cached for a normalized document path.
	/// </summary>
	/// <param name="normalizedFilePath">The normalized local file path of the document.</param>
	/// <returns>The cached semantic tokens, or an empty list when none are stored.</returns>
	/// <remarks>
	/// A throwing implementation is contained and treated as an empty result. This is an optional feature hook:
	/// the default implementation returns an empty list, so a provider that does not expose semantic tokens can
	/// omit it.
	/// </remarks>
	protected virtual IReadOnlyList<SemanticToken> GetTrackedSemanticTokens(string normalizedFilePath)
		=> [];

	/// <summary>
	/// Stores a decoded semantic-token set for a tracked document when it is not stale for the tracked version.
	/// </summary>
	/// <param name="normalizedFilePath">The normalized local file path of the document.</param>
	/// <param name="documentVersion">The tracked document version the token set was decoded for.</param>
	/// <param name="semanticTokens">The decoded semantic tokens to cache.</param>
	/// <returns><see langword="true"/> when the token set was stored; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// The hook must fence the store against the tracked document version, so a token set decoded for an older
	/// snapshot cannot overwrite newer tokens. It runs outside the base's request-supersession lock.
	/// <para>
	/// This is an optional feature hook: the default implementation rejects every store and returns
	/// <see langword="false"/>, so a provider that does not expose semantic tokens can omit it (a decoded token set
	/// is then discarded and <see cref="ILanguageServerSemanticTokensProvider.SemanticTokensUpdated"/> is not raised).
	/// </para>
	/// </remarks>
	protected virtual bool TryStoreSemanticTokens(string normalizedFilePath, int documentVersion, IReadOnlyList<SemanticToken> semanticTokens)
		=> false;

	/// <summary>
	/// Refreshes language-specific state for a document that was just synchronized or reopened.
	/// </summary>
	/// <param name="document">The synchronized tracked document snapshot.</param>
	/// <param name="cancellationToken">A token that can cancel the refresh.</param>
	/// <returns>A task that completes when the refresh has finished.</returns>
	/// <remarks>
	/// <para>
	/// Invoked after a successful open or change synchronization that requested a refresh, and after each tracked
	/// document is reopened when the provider restarts. The default implementation does nothing; request-driven
	/// synchronization deliberately does not invoke it. A change that the negotiated synchronization mode could
	/// not express (see <see cref="TextDocumentSyncKind.None"/>) does not invoke the hook either, because the
	/// server still holds the previous content and a refresh computed from it would describe stale state.
	/// </para>
	/// <para>
	/// The hook runs while the document's per-document scheduler slot is still held, and during a restart replay
	/// it also runs while the provider's startup serialization is held. Implementations must not call provider
	/// document or request APIs from it (the scheduler rejects work queued inside its own operation, and awaiting
	/// startup stalls the replay) and should detach long-running work through <see cref="ObserveBackgroundTask"/>
	/// instead of awaiting it here.
	/// </para>
	/// </remarks>
	protected virtual Task OnDocumentSynchronizedAsync(DocumentSnapshot document, CancellationToken cancellationToken)
		=> Task.CompletedTask;

	/// <summary>
	/// Marks a tracked document as no longer mirrored to the server.
	/// </summary>
	/// <param name="filePath">The normalized local file path of the document.</param>
	/// <remarks>
	/// Invoked while the document's per-document scheduler slot may still be held, directly after a transport
	/// failure invalidated its server copy, and when a rename needs the destination to be reopened lazily.
	/// Implementations must mark the record so the next synchronization treats it as not open on the server,
	/// typically by forwarding to the tracked-document store's invalidation helper; the restart replay skips
	/// records that still claim an open server copy. The implementation must only touch tracked state (no scheduler
	/// work) and must not throw for unknown documents.
	/// </remarks>
	protected abstract void InvalidateTrackedDocumentSynchronization(string filePath);

	/// <summary>
	/// Clears language-specific per-document work for a document whose tracked provider-side state was invalidated.
	/// </summary>
	/// <param name="filePath">The normalized local file path of the document.</param>
	/// <remarks>
	/// <para>
	/// Invoked when the document's provider-side state no longer supports the language-specific work in flight for
	/// it:
	/// </para>
	/// <list type="bullet">
	/// <item><description>after the document is closed by its last open reference, now that no consumer path will display its results;</description></item>
	/// <item><description>for both paths when a tracked document is moved and rekeyed;</description></item>
	/// <item><description>for each idle document that is trimmed and closed on the server after its last request reference was released;</description></item>
	/// <item><description>while the document's per-document scheduler slot is still held, directly after its server synchronization was dropped following a transport failure.</description></item>
	/// </list>
	/// <para>
	/// Implementations should cancel language-specific in-flight work for the document, such as pending cache
	/// requests. The default implementation does nothing.
	/// </para>
	/// </remarks>
	protected virtual void OnTrackedDocumentInvalidated(string filePath)
	{ }

	/// <summary>
	/// Refreshes language-specific state after a tracked document was moved successfully.
	/// </summary>
	/// <param name="filePath">The normalized local file path of the moved document.</param>
	/// <remarks>
	/// Invoked after the base raised <see cref="DiagnosticsUpdated"/> for the moved document; a throwing
	/// implementation is contained. The default implementation does nothing.
	/// </remarks>
	protected virtual void OnDocumentMoved(string filePath)
	{ }

	/// <summary>
	/// Detaches language-specific state when the provider is disposed.
	/// </summary>
	/// <remarks>
	/// Invoked once during disposal (<see cref="Dispose"/> or <see cref="IAsyncDisposable.DisposeAsync"/>), after
	/// callback admission closed and before the owned language-server client is unsubscribed and disposed.
	/// Implementations should detach their own client event subscriptions and cancel language-specific work here.
	/// The default implementation does nothing.
	/// </remarks>
	protected virtual void OnDisposing()
	{ }
}

using Nickelony.IDEKit.IntelliSense.Completion;
using Nickelony.IDEKit.IntelliSense.Diagnostics;
using Nickelony.IDEKit.IntelliSense.Hover;
using Nickelony.IDEKit.IntelliSense.Navigation;
using Nickelony.IDEKit.IntelliSense.Signatures;

namespace Nickelony.LanguageServer.Abstractions;

/// <summary>
/// Defines the language-neutral provider contract used by text editors to obtain IntelliSense features.
/// </summary>
/// <remarks>
/// <para>
/// This interface contains the document lifecycle operations and the IntelliSense features that are not specific to
/// any single language. Language-specific concerns are layered on through narrower interfaces. Semantic tokens are
/// a sibling contract that this interface also exposes, so a host holding the provider contract can read semantic
/// tokens without downcasting to a narrower interface.
/// </para>
/// <para>
/// Document identity is a local file path: every member takes the absolute path of a document on the local file
/// system, and a path that cannot be normalized to one makes the affected member a no-op or returns its documented
/// fallback value. The package README states the identity scope in full.
/// </para>
/// <para>
/// Implementations may raise callbacks from background threads; handlers for one invocation run serially on the
/// raising thread, and a failing handler is isolated from later handlers. Consumers that touch thread-affine state
/// marshal those callbacks to the owning thread themselves. Disposal is idempotent, shares one teardown between
/// <see cref="IDisposable.Dispose"/> and <see cref="IAsyncDisposable.DisposeAsync"/> (the asynchronous form awaits
/// the teardown instead of blocking), and closes callback admission before releasing provider-owned resources, so a
/// running handler may finish but delivery can stop before a notification's later handlers run. Caller cancellation
/// propagates as <see cref="OperationCanceledException"/> and never becomes an ordinary empty or
/// <see langword="null"/> result; provider disposal, provider-enforced timeouts, and unsupported capabilities use
/// each member's documented fallback value instead. Completion, hover, definition, and signature help are always
/// attempted; references, rename, formatting, document symbols, and code actions are capability-gated and expose a
/// <c>Supports*</c> flag that is <see langword="false"/> before the first successful lazy start.
/// </para>
/// <para>
/// The lifecycle, threading, document-reference, and cancellation contract is described end to end in the
/// repository's consumer integration guide (<c>docs/ConsumerIntegration.md</c>).
/// </para>
/// </remarks>
public interface ILanguageServerIntelliSenseProvider : IDisposable, IAsyncDisposable,
	ILanguageServerDocumentSymbolProvider, ILanguageServerCodeActionProvider, ILanguageServerFormattingProvider,
	ILanguageServerRenameProvider, ILanguageServerReferencesProvider, ILanguageServerSemanticTokensProvider
{
	/// <summary>
	/// Gets a value indicating whether the provider has a ready language-server session and reports its capabilities.
	/// </summary>
	/// <remarks>
	/// This value is <see langword="false"/> before lazy startup, while the provider is starting or restarting,
	/// when no usable language-server session is available, and after disposal. It reflects the readiness the
	/// provider uses to serve requests and can be stricter than a state check during transport-invalidating
	/// transitions, so treat <see cref="State"/> as the lifecycle classification and this property as the
	/// availability answer. A request may transition the provider from an unavailable state to a ready state when
	/// startup succeeds.
	/// </remarks>
	bool IsAvailable { get; }

	/// <summary>
	/// Gets the current provider lifecycle state.
	/// </summary>
	LanguageServerProviderState State { get; }

	/// <summary>
	/// Raised when the provider's cached diagnostics for a document are updated.
	/// </summary>
	/// <remarks>
	/// The provider is the sender. The diagnostics list is an owned immutable snapshot that remains valid
	/// after the callback returns.
	/// </remarks>
	event EventHandler<DiagnosticsUpdatedEventArgs>? DiagnosticsUpdated;

	/// <summary>
	/// Raised when lazy startup, restart, or transport loss may have changed the session's capabilities.
	/// </summary>
	/// <remarks>
	/// Consumers should reread <see cref="IsAvailable"/> and capability properties after this event rather than caching
	/// capability values.
	/// </remarks>
	event EventHandler? CapabilitiesChanged;

	/// <summary>
	/// Raised when the provider reports a language-server startup failure.
	/// </summary>
	/// <remarks>
	/// The provider is the sender. The event is raised at most once per failure episode; a successful start resets
	/// the notification state, and a terminal failure is reported at most once.
	/// </remarks>
	event EventHandler<StartupFailedEventArgs>? StartupFailed;

	/// <summary>
	/// Raised when the workspace file watcher cannot be started or recovered and external changes may no longer be forwarded.
	/// </summary>
	/// <remarks>
	/// <para>
	/// When a running watcher fails, automatic recovery is attempted first, and a successful recovery does not
	/// raise this event. An unresolved startup or recovery failure raises it once until a later successful watcher
	/// recovery resets the notification state.
	/// </para>
	/// <para>
	/// An initial watcher creation or activation failure is reported directly because there is no existing watcher
	/// to recover. A missing workspace root is treated as temporarily unavailable, and the payload carries the
	/// affected workspace root directory path when the failure is tied to one. Workspace watching is optional,
	/// so providers without an external workspace never raise this event. The provider is the sender.
	/// </para>
	/// </remarks>
	event EventHandler<WorkspaceWatcherFailedEventArgs>? WorkspaceWatcherFailed;

	/// <summary>
	/// Gets the latest diagnostics known for a document.
	/// </summary>
	/// <remarks>
	/// This is a passive cache read: it never starts the language server, and it returns an empty list before
	/// the first diagnostics arrive and after disposal.
	/// </remarks>
	/// <param name="filePath">The local file path of the document.</param>
	/// <returns>An owned immutable snapshot of the cached diagnostics, or an empty list when no diagnostics are cached.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="filePath"/> is <see langword="null"/>.</exception>
	IReadOnlyList<TextDiagnostic> GetDiagnostics(string filePath);

	/// <summary>
	/// Opens a document in the provider, starts tracking its contents, and synchronizes it with the underlying language server when available.
	/// </summary>
	/// <remarks>
	/// Each call acquires one open reference. Repeated opens for the same path require matching calls to
	/// <see cref="CloseDocument"/> before the document is fully cleaned up. The provider normalizes the path and
	/// serializes operations for that document. An open that arrives while the language server is starting or
	/// recovering is retried while that flow settles, up to two retries; when the transport keeps failing, the
	/// open stays unapplied like every other failed-startup operation, and this member cannot report that
	/// outcome to the caller.
	/// </remarks>
	/// <param name="filePath">The local file path of the document.</param>
	/// <param name="documentText">The initial document content.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="filePath"/> or <paramref name="documentText"/> is <see langword="null"/>.
	/// </exception>
	void OpenDocument(string filePath, string documentText);

	/// <summary>
	/// Synchronizes updated content for a document so the underlying language server can stay synchronized.
	/// </summary>
	/// <remarks>
	/// This operation does not acquire an open reference, so a document tracked only by an update can be cleaned up
	/// automatically. When the language server is available, an update may cause the provider to track a document
	/// even when no consumer has opened it. Updates after a close or move are serialized against the affected path
	/// and can reopen or update the resulting tracked document.
	/// </remarks>
	/// <param name="filePath">The local file path of the document.</param>
	/// <param name="documentText">The updated document content.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="filePath"/> or <paramref name="documentText"/> is <see langword="null"/>.
	/// </exception>
	void UpdateDocument(string filePath, string documentText);

	/// <summary>
	/// Releases an open reference for a tracked document and cleans up provider-side state when no references remain.
	/// </summary>
	/// <remarks>
	/// Each call releases one open reference when one exists. A repeated call after the document is no longer
	/// tracked is a no-op; a document tracked only by an update may also be cleaned up by close. The document remains
	/// tracked while an IntelliSense request is using it; otherwise, the final close releases the provider-side state
	/// and notifies the language server once no request is using the document.
	/// </remarks>
	/// <param name="filePath">The local file path of the document.</param>
	/// <exception cref="ArgumentNullException"><paramref name="filePath"/> is <see langword="null"/>.</exception>
	void CloseDocument(string filePath);

	/// <summary>
	/// Rekeys a tracked document after its file was renamed or moved, preserving any provider-side state that still applies.
	/// </summary>
	/// <remarks>
	/// Unknown source paths, equivalent paths, and destination paths that are already tracked are no-ops; they do
	/// not create or move destination state. A successful move preserves references and uses the supplied content.
	/// Cached results for unchanged content remain valid across the move; a content change invalidates them.
	/// </remarks>
	/// <param name="oldFilePath">The previous local file path.</param>
	/// <param name="newFilePath">The new local file path.</param>
	/// <param name="documentText">The current document content.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="oldFilePath"/>, <paramref name="newFilePath"/>, or <paramref name="documentText"/> is
	/// <see langword="null"/>.
	/// </exception>
	void MoveDocument(string oldFilePath, string newFilePath, string documentText);

	/// <summary>
	/// Requests completion items for a position within a document.
	/// </summary>
	/// <param name="request">The document, position, and completion trigger state.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>
	/// An owned snapshot of the available completion items, or an empty list when completion is unavailable.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	Task<IReadOnlyList<TextCompletionItem>> GetCompletionItemsAsync(LanguageServerCompletionRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Requests hover information for a position within a document.
	/// </summary>
	/// <param name="request">The document and position to inspect.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>The hover information for the requested position, or <see langword="null"/> when unavailable.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	Task<TextHoverInfo?> GetHoverAsync(LanguageServerHoverRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Requests the definition location for a symbol at a position within a document.
	/// </summary>
	/// <remarks>
	/// The returned location names the definition's document in its <see cref="TextDefinitionLocation.DocumentId"/>
	/// member with the local file path; this provider family resolves definitions to local file paths, including
	/// definitions inside the requested document, so the shared "a null identifier means the requested document"
	/// convention does not apply to results from this family.
	/// </remarks>
	/// <param name="request">The document and symbol position to resolve.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>The resolved definition location, or <see langword="null"/> when no definition is available.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	Task<TextDefinitionLocation?> GetDefinitionAsync(LanguageServerDefinitionRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Requests signature help for a function call at a position within a document.
	/// </summary>
	/// <remarks>
	/// The request's trigger context describes how the request was triggered and carries the currently shown
	/// payload; providers forward it to the language server, so it can keep the selected overload
	/// stable across retriggers.
	/// </remarks>
	/// <param name="request">The document, position, and optional trigger context.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>The signature help information, or <see langword="null"/> when unavailable.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	Task<TextSignatureHelp?> GetSignatureHelpAsync(LanguageServerSignatureHelpRequest request,
		CancellationToken cancellationToken = default);
}

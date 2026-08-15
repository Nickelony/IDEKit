namespace Nickelony.LanguageServer.Abstractions;

/// <summary>
/// Defines the language-neutral semantic-token contract a language-server provider exposes so a text editor
/// can render semantic highlighting.
/// </summary>
/// <remarks>
/// <para>
/// This is the semantic-token counterpart of the diagnostics contract on
/// <see cref="ILanguageServerIntelliSenseProvider"/>: the provider is the sender of
/// <see cref="SemanticTokensUpdated"/>, and <see cref="GetSemanticTokens"/> is the passive cache read of the
/// same data. The provider framework supplies both over the language's own token cache, so a language package
/// supplies only its storage hooks and, optionally, its own narrower interface for language-specific concerns.
/// </para>
/// <para>
/// Semantic tokens are capability-gated: a provider whose current session does not support full semantic
/// tokens never raises the event and <see cref="GetSemanticTokens"/> returns an empty list. A provider that
/// does support them refreshes a tracked document's tokens after each successful open or change
/// synchronization and after a restart reopen, and re-requests the full token set when the server asks for a
/// semantic-token refresh.
/// </para>
/// <para>
/// The threading, handler-isolation, and disposal rules of callback delivery are defined by the provider base;
/// handlers run serially on the raising thread and are isolated from later handlers.
/// </para>
/// </remarks>
public interface ILanguageServerSemanticTokensProvider
{
	/// <summary>
	/// Raised when the semantic tokens that are current for a document have changed.
	/// </summary>
	/// <remarks>
	/// The provider is the sender. The event is raised when a refreshed token set is stored, when a rename
	/// re-keys a document's cached tokens, and with an empty list when a content-changing rename clears them.
	/// A failed refresh keeps the previously cached tokens and raises no event.
	/// </remarks>
	event EventHandler<SemanticTokensUpdatedEventArgs>? SemanticTokensUpdated;

	/// <summary>
	/// Gets the most recently cached semantic tokens for a document.
	/// </summary>
	/// <param name="filePath">The local file path of the document.</param>
	/// <returns>
	/// A read-only snapshot of the semantic tokens currently cached for the document; an empty list when the
	/// provider is disposed, the path cannot be resolved to a local file path, or the document has no cached tokens.
	/// </returns>
	/// <remarks>
	/// The tokens reflect the document version they were decoded for and may therefore be stale relative to the
	/// content the consumer is editing. The <see cref="SemanticTokensUpdated"/> event announces each new token set.
	/// </remarks>
	/// <exception cref="ArgumentNullException"><paramref name="filePath"/> is <see langword="null"/>.</exception>
	IReadOnlyList<SemanticToken> GetSemanticTokens(string filePath);
}

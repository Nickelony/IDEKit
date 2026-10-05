namespace Nickelony.IDEKit.LanguageFeatures.SemanticTokens;

/// <summary>Produces the semantic tokens of documents without a request, so an editor can render semantic highlighting.</summary>
/// <remarks>
/// A source announces each change with <see cref="SemanticTokensUpdated"/> and holds the latest tokens
/// of each document for <see cref="GetSemanticTokens"/>. Handlers run serially on the thread that raises the event.
/// </remarks>
public interface ISemanticTokensSource
{
	/// <summary>Raised when the semantic tokens that are current for a document have changed.</summary>
	event EventHandler<SemanticTokensUpdatedEventArgs>? SemanticTokensUpdated;

	/// <summary>Returns the most recently produced semantic tokens for a document.</summary>
	/// <param name="documentId">The identifier of the document.</param>
	/// <returns>A read-only snapshot of the current semantic tokens, or an empty list when the source holds none for the document.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="documentId"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// The tokens reflect the document version they were produced for, so they may be stale relative to the content the consumer is editing.
	/// </remarks>
	IReadOnlyList<SemanticToken> GetSemanticTokens(string documentId);
}

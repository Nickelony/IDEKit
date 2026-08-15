namespace Nickelony.LanguageServer.Abstractions;

/// <summary>
/// Provides the data for the <see cref="ILanguageServerSemanticTokensProvider.SemanticTokensUpdated"/> event.
/// </summary>
/// <remarks>
/// The constructor copies the supplied sequence, so the event payload is owned by this instance: the list
/// remains valid after the callback returns and cannot be mutated through the source collection, and each
/// token's modifier list is a read-only snapshot. The list may be empty: the provider raises the event with
/// no tokens when a content-changing rename clears a document's cached tokens.
/// </remarks>
public sealed class SemanticTokensUpdatedEventArgs : EventArgs
{
	private static readonly IReadOnlyList<SemanticToken> s_emptyTokens = Array.AsReadOnly<SemanticToken>([]);

	/// <summary>
	/// Initializes a new instance of the <see cref="SemanticTokensUpdatedEventArgs"/> class.
	/// </summary>
	/// <param name="filePath">The local file path of the document whose semantic tokens are updated.</param>
	/// <param name="semanticTokens">The updated semantic tokens for the document.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="filePath"/> or <paramref name="semanticTokens"/> is <see langword="null"/>.
	/// </exception>
	public SemanticTokensUpdatedEventArgs(string filePath, IReadOnlyList<SemanticToken> semanticTokens)
	{
		ArgumentNullException.ThrowIfNull(filePath);
		ArgumentNullException.ThrowIfNull(semanticTokens);

		FilePath = filePath;
		SemanticTokens = semanticTokens.Count == 0 ? s_emptyTokens : Array.AsReadOnly([.. semanticTokens]);
	}

	/// <summary>
	/// Gets the local file path of the document whose semantic tokens are updated.
	/// </summary>
	public string FilePath { get; }

	/// <summary>
	/// Gets the updated semantic tokens for the document.
	/// </summary>
	public IReadOnlyList<SemanticToken> SemanticTokens { get; }
}

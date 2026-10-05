namespace Nickelony.IDEKit.LanguageFeatures.SemanticTokens;

/// <summary>Provides the data for the <see cref="ISemanticTokensSource.SemanticTokensUpdated"/> event.</summary>
/// <remarks>
/// The constructor copies the supplied sequence, so the event payload is owned by this instance: the list remains valid after the callback returns
/// and cannot be mutated through the source collection, and each token's modifier list is a read-only snapshot. The list may be empty.
/// </remarks>
public sealed class SemanticTokensUpdatedEventArgs : EventArgs
{
	/// <param name="documentId">The identifier of the document the tokens belong to.</param>
	/// <param name="semanticTokens">The document's current semantic tokens.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="documentId"/> or <paramref name="semanticTokens"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="semanticTokens"/> contains a <see langword="null"/> element.</exception>
	public SemanticTokensUpdatedEventArgs(string documentId, IReadOnlyList<SemanticToken> semanticTokens)
	{
		ArgumentNullException.ThrowIfNull(documentId);
		ArgumentNullException.ThrowIfNull(semanticTokens);

		DocumentId = documentId;
		SemanticTokens = CollectionSnapshots.Capture(semanticTokens, "semantic tokens", nameof(semanticTokens));
	}

	/// <summary>The identifier of the document the tokens belong to.</summary>
	public string DocumentId { get; }

	/// <summary>The document's current semantic tokens, an owned immutable snapshot.</summary>
	public IReadOnlyList<SemanticToken> SemanticTokens { get; }
}

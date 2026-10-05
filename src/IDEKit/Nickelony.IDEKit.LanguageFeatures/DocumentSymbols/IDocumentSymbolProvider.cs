namespace Nickelony.IDEKit.LanguageFeatures.DocumentSymbols;

/// <summary>Provides the document symbols (outline entries) for a document.</summary>
/// <remarks>
/// Implementations must be safe to call from any thread: the request is an immutable snapshot and no UI state may be touched. The request carries
/// the complete document text, and the call observes the supplied cancellation token. A <see langword="null"/> request is rejected
/// with <see cref="ArgumentNullException"/>.
/// </remarks>
public interface IDocumentSymbolProvider
{
	/// <summary>Returns the document symbols for the supplied request.</summary>
	/// <param name="request">The document-symbol request.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>The matching document symbols in provider order, or an empty list when none apply; never <see langword="null"/>.</returns>
	Task<IReadOnlyList<DocumentSymbol>> GetDocumentSymbolsAsync(DocumentSymbolRequest request, CancellationToken cancellationToken = default);
}

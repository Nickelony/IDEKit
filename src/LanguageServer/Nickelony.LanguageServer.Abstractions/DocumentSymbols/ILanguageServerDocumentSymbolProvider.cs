using Nickelony.IDEKit.IntelliSense.DocumentSymbols;

namespace Nickelony.LanguageServer.Abstractions;

/// <summary>
/// Produces document symbols (outline entries) for a document.
/// </summary>
public interface ILanguageServerDocumentSymbolProvider
{
	/// <summary>
	/// Gets a value indicating whether document-symbol requests are supported by the current ready session.
	/// </summary>
	/// <remarks>
	/// This flag is <see langword="false"/> until a language-server session is ready and supports document
	/// symbols; the lazy-startup and capability-gating contract is on
	/// <see cref="ILanguageServerIntelliSenseProvider"/>.
	/// </remarks>
	bool SupportsDocumentSymbols { get; }

	/// <summary>
	/// Requests the document symbols (outline entries) for a document.
	/// </summary>
	/// <remarks>
	/// Document symbols are capability-gated: the request is skipped with an empty result when the current session
	/// does not support them (see <see cref="SupportsDocumentSymbols"/>). The returned list is an owned snapshot
	/// with the provider's entry order preserved.
	/// </remarks>
	/// <param name="request">The document to outline.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>The document symbols for the document, or an empty list when unavailable.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	Task<IReadOnlyList<TextDocumentSymbol>> GetDocumentSymbolsAsync(LanguageServerDocumentSymbolRequest request,
		CancellationToken cancellationToken = default);
}

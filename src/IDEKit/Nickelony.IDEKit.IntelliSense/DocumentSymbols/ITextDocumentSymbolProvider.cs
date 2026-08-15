namespace Nickelony.IDEKit.IntelliSense.DocumentSymbols;

/// <summary>
/// Provides the document symbols (outline entries) for a document.
/// </summary>
/// <remarks>
/// <para>
/// Implementations must be safe to call from any thread: the request is an immutable snapshot and
/// no UI state may be touched. The contract is synchronous and carries the complete document text
/// in each request; implementations that need background or cancellable projection schedule that
/// work themselves.
/// </para>
/// <para>
/// Implementations must reject a <see langword="null"/> request with
/// <see cref="ArgumentNullException"/>.
/// </para>
/// </remarks>
public interface ITextDocumentSymbolProvider
{
	/// <summary>
	/// Gets the document symbols for the supplied request.
	/// </summary>
	/// <param name="request">The document-symbol request.</param>
	/// <returns>
	/// The matching document symbols, or an empty list when none apply. The return value is never
	/// <see langword="null"/>.
	/// </returns>
	IReadOnlyList<TextDocumentSymbol> GetSymbols(TextDocumentSymbolRequest request);
}

namespace Nickelony.IDEKit.IntelliSense.DocumentSymbols;

/// <summary>
/// Describes a document-symbol request against an immutable document snapshot.
/// </summary>
/// <remarks>
/// The filter text is passed through unchanged; the matching semantics (case sensitivity,
/// whitespace handling, and whether an empty filter keeps all symbols) are defined by the provider
/// that evaluates the request.
/// </remarks>
public sealed record TextDocumentSymbolRequest
{
	/// <summary>
	/// Initializes a new instance of the <see cref="TextDocumentSymbolRequest"/> record.
	/// </summary>
	/// <param name="documentText">The current document snapshot text.</param>
	/// <param name="filterText">
	/// The filter text used to narrow symbols; matching semantics are provider-defined.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="documentText"/> or <paramref name="filterText"/> is <see langword="null"/>.
	/// </exception>
	public TextDocumentSymbolRequest(string documentText, string filterText)
	{
		ArgumentNullException.ThrowIfNull(documentText);
		ArgumentNullException.ThrowIfNull(filterText);

		DocumentText = documentText;
		FilterText = filterText;
	}

	/// <summary>
	/// Gets the current document snapshot text.
	/// </summary>
	public string DocumentText { get; }

	/// <summary>
	/// Gets the filter text used to narrow symbols. Matching semantics are provider-defined.
	/// </summary>
	public string FilterText { get; }
}

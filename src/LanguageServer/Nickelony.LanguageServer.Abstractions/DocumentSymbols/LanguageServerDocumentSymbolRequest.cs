namespace Nickelony.LanguageServer.Abstractions;

/// <summary>
/// Describes a request for the document symbols (outline entries) of a document.
/// </summary>
public sealed record LanguageServerDocumentSymbolRequest
{
	/// <summary>
	/// Initializes a new instance of the <see cref="LanguageServerDocumentSymbolRequest"/> record.
	/// </summary>
	/// <param name="filePath">The current document file path.</param>
	/// <param name="documentText">The current document content.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="filePath"/> or <paramref name="documentText"/> is <see langword="null"/>.
	/// </exception>
	public LanguageServerDocumentSymbolRequest(string filePath, string documentText)
	{
		ArgumentNullException.ThrowIfNull(filePath);
		ArgumentNullException.ThrowIfNull(documentText);

		FilePath = filePath;
		DocumentText = documentText;
	}

	/// <summary>
	/// Gets the current document file path.
	/// </summary>
	public string FilePath { get; }

	/// <summary>
	/// Gets the current document content.
	/// </summary>
	public string DocumentText { get; }
}

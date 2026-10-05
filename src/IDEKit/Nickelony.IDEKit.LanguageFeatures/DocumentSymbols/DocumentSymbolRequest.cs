namespace Nickelony.IDEKit.LanguageFeatures.DocumentSymbols;

/// <summary>Describes a document-symbol request against an immutable document snapshot.</summary>
/// <remarks>
/// The filter text narrows the symbols by the rule <see cref="DocumentSymbolFilter.FilterByName"/> applies: a blank filter keeps every symbol,
/// a symbol matches when its name contains the filter without regard to case, and a symbol whose descendant matches stays visible.
/// A provider that serves the request applies that helper, or documents its own rule.
/// </remarks>
public sealed record DocumentSymbolRequest
{
	/// <param name="documentId">
	/// The identifier of the document the text belongs to. A language-server provider maps it to the document's file URI, and a local provider
	/// may ignore it.
	/// </param>
	/// <param name="documentText">The current document snapshot text.</param>
	/// <param name="filterText">The filter text that narrows symbols; a blank value keeps every symbol.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="documentId"/>, <paramref name="documentText"/> or <paramref name="filterText"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="documentId"/> is blank.</exception>
	public DocumentSymbolRequest(string documentId, string documentText, string filterText)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(documentId);

		ArgumentNullException.ThrowIfNull(documentText);
		ArgumentNullException.ThrowIfNull(filterText);

		DocumentId = documentId;
		DocumentText = documentText;
		FilterText = filterText;
	}

	/// <summary>The identifier of the document the text belongs to.</summary>
	public string DocumentId { get; }

	/// <summary>The current document snapshot text.</summary>
	public string DocumentText { get; }

	/// <summary>The filter text that narrows the symbols.</summary>
	public string FilterText { get; }
}

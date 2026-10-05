namespace Nickelony.IDEKit.LanguageFeatures.Formatting;

/// <summary>Describes a request to format a whole document.</summary>
public sealed record FormattingRequest
{
	/// <param name="documentId">
	/// The identifier of the document the text belongs to. A language-server provider maps it to the document's file URI, and a local provider
	/// may ignore it.
	/// </param>
	/// <param name="documentText">The current document snapshot text.</param>
	/// <param name="options">The formatting options.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="documentId"/>, <paramref name="documentText"/> or <paramref name="options"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="documentId"/> is blank.</exception>
	public FormattingRequest(string documentId, string documentText, FormattingOptions options)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(documentId);

		ArgumentNullException.ThrowIfNull(documentText);
		ArgumentNullException.ThrowIfNull(options);

		DocumentId = documentId;
		DocumentText = documentText;
		Options = options;
	}

	/// <summary>The identifier of the document the text belongs to.</summary>
	public string DocumentId { get; }

	/// <summary>The current document snapshot text.</summary>
	public string DocumentText { get; }

	/// <summary>The formatting options.</summary>
	public FormattingOptions Options { get; }
}

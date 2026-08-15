namespace Nickelony.LanguageServer.Abstractions;

/// <summary>
/// Describes a formatting request against the current document.
/// </summary>
public sealed record LanguageServerFormattingRequest
{
	/// <summary>
	/// Initializes a new instance of the <see cref="LanguageServerFormattingRequest"/> record.
	/// </summary>
	/// <param name="filePath">The current document file path.</param>
	/// <param name="documentText">The current document content.</param>
	/// <param name="options">The formatting options to apply.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="filePath"/>, <paramref name="documentText"/>, or <paramref name="options"/> is
	/// <see langword="null"/>.
	/// </exception>
	public LanguageServerFormattingRequest(string filePath, string documentText, TextFormattingOptions options)
	{
		ArgumentNullException.ThrowIfNull(filePath);
		ArgumentNullException.ThrowIfNull(documentText);
		ArgumentNullException.ThrowIfNull(options);

		FilePath = filePath;
		DocumentText = documentText;
		Options = options;
	}

	/// <summary>
	/// Gets the current document file path.
	/// </summary>
	public string FilePath { get; }

	/// <summary>
	/// Gets the current document content.
	/// </summary>
	public string DocumentText { get; }

	/// <summary>
	/// Gets the formatting options.
	/// </summary>
	public TextFormattingOptions Options { get; }
}

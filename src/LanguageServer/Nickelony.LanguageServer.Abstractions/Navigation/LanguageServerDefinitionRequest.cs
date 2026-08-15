using Nickelony.IDEKit.Core.Text;

namespace Nickelony.LanguageServer.Abstractions;

/// <summary>
/// Describes a request to resolve the definition location for a symbol at a document position.
/// </summary>
/// <remarks>
/// Returned <see cref="Nickelony.IDEKit.IntelliSense.Navigation.TextDefinitionLocation"/> values name the
/// definition's document in their
/// <see cref="Nickelony.IDEKit.IntelliSense.Navigation.TextDefinitionLocation.DocumentId"/> member with the local
/// file path, so the request position uses the same zero-based units the result does.
/// </remarks>
public sealed record LanguageServerDefinitionRequest
{
	/// <summary>
	/// Initializes a new instance of the <see cref="LanguageServerDefinitionRequest"/> record.
	/// </summary>
	/// <param name="filePath">The current document file path.</param>
	/// <param name="documentText">The current document content.</param>
	/// <param name="position">The zero-based symbol position. Negative values are changed to zero.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="filePath"/> or <paramref name="documentText"/> is <see langword="null"/>.
	/// </exception>
	public LanguageServerDefinitionRequest(string filePath, string documentText, TextPosition position)
	{
		ArgumentNullException.ThrowIfNull(filePath);
		ArgumentNullException.ThrowIfNull(documentText);

		FilePath = filePath;
		DocumentText = documentText;
		Position = position.ClampNegativeToZero();
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
	/// Gets the zero-based position of the symbol.
	/// </summary>
	public TextPosition Position { get; }
}

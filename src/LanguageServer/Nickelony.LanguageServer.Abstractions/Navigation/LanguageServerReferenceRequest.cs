using Nickelony.IDEKit.Core.Text;

namespace Nickelony.LanguageServer.Abstractions;

/// <summary>
/// Describes a request to find symbol references at a document position.
/// </summary>
/// <remarks>
/// Returned <see cref="TextReferenceLocation"/> values use the same zero-based position units as the request
/// position.
/// </remarks>
public sealed record LanguageServerReferenceRequest
{
	/// <summary>
	/// Initializes a new instance of the <see cref="LanguageServerReferenceRequest"/> record.
	/// </summary>
	/// <param name="filePath">The current document file path.</param>
	/// <param name="documentText">The current document content.</param>
	/// <param name="position">The zero-based symbol position. Negative values are changed to zero.</param>
	/// <param name="includeDeclaration"><see langword="true"/> to include the symbol declaration when available.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="filePath"/> or <paramref name="documentText"/> is <see langword="null"/>.
	/// </exception>
	public LanguageServerReferenceRequest(string filePath, string documentText, TextPosition position, bool includeDeclaration = true)
	{
		ArgumentNullException.ThrowIfNull(filePath);
		ArgumentNullException.ThrowIfNull(documentText);

		FilePath = filePath;
		DocumentText = documentText;
		Position = position.ClampNegativeToZero();
		IncludeDeclaration = includeDeclaration;
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

	/// <summary>
	/// Gets a value indicating whether the declaration should be included when available.
	/// </summary>
	public bool IncludeDeclaration { get; }
}

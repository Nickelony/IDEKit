using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.LanguageFeatures.Navigation;

/// <summary>Describes a request to find the references of the symbol at a document offset.</summary>
public sealed record ReferenceRequest
{
	/// <param name="documentId">
	/// The identifier of the document the text belongs to. A language-server provider maps it to the document's file URI, and a local provider
	/// may ignore it.
	/// </param>
	/// <param name="documentText">The current document snapshot text.</param>
	/// <param name="symbolOffset">The zero-based UTF-16 offset of the symbol within <paramref name="documentText"/>.</param>
	/// <param name="includeDeclaration"><see langword="true"/> to include the symbol declaration when available.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="documentId"/> or <paramref name="documentText"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="documentId"/> is blank.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="symbolOffset"/> is negative or greater than the length of <paramref name="documentText"/>.
	/// </exception>
	public ReferenceRequest(string documentId, string documentText, int symbolOffset, bool includeDeclaration = true)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
		ArgumentNullException.ThrowIfNull(documentText);
		TextOffsetValidation.ValidateOffset(documentText, offset: symbolOffset, parameterName: nameof(symbolOffset), offsetDescription: "symbol");

		DocumentId = documentId;
		DocumentText = documentText;
		SymbolOffset = symbolOffset;
		IncludeDeclaration = includeDeclaration;
	}

	/// <summary>The identifier of the document the text belongs to.</summary>
	public string DocumentId { get; }

	/// <summary>The current document snapshot text.</summary>
	public string DocumentText { get; }

	/// <summary>The zero-based UTF-16 offset of the symbol within <see cref="DocumentText"/>.</summary>
	public int SymbolOffset { get; }

	/// <summary>Indicates whether the symbol declaration is part of the result when the provider has it.</summary>
	public bool IncludeDeclaration { get; }
}

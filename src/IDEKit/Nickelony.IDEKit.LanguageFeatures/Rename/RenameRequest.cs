using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.LanguageFeatures.Rename;

/// <summary>Describes a request to rename the symbol at a document offset.</summary>
public sealed record RenameRequest
{
	/// <param name="documentId">
	/// The identifier of the document the text belongs to. A language-server provider maps it to the document's file URI, and a local provider
	/// may ignore it.
	/// </param>
	/// <param name="documentText">The current document snapshot text.</param>
	/// <param name="symbolOffset">The zero-based UTF-16 offset of the symbol within <paramref name="documentText"/>.</param>
	/// <param name="newName">The requested replacement name.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="documentId"/>, <paramref name="documentText"/> or <paramref name="newName"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="documentId"/> is blank.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="symbolOffset"/> is negative or greater than the length of <paramref name="documentText"/>.
	/// </exception>
	public RenameRequest(string documentId, string documentText, int symbolOffset, string newName)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(documentId);

		ArgumentNullException.ThrowIfNull(documentText);
		ArgumentNullException.ThrowIfNull(newName);

		TextOffsetValidation.ValidateOffset(documentText, offset: symbolOffset, parameterName: nameof(symbolOffset), offsetDescription: "symbol");

		DocumentId = documentId;
		DocumentText = documentText;
		SymbolOffset = symbolOffset;
		NewName = newName;
	}

	/// <summary>The identifier of the document the text belongs to.</summary>
	public string DocumentId { get; }

	/// <summary>The current document snapshot text.</summary>
	public string DocumentText { get; }

	/// <summary>The zero-based UTF-16 offset of the symbol within <see cref="DocumentText"/>.</summary>
	public int SymbolOffset { get; }

	/// <summary>The requested replacement name.</summary>
	public string NewName { get; }
}

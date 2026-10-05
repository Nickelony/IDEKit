using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.LanguageFeatures.Navigation;

/// <summary>Describes a definition lookup request against an immutable document snapshot.</summary>
/// <remarks>
/// <para>
/// The request names the target symbol and, when the caller knows it, the offset of the symbol in the document. A name-catalog provider resolves
/// by <see cref="SymbolName"/> and ignores <see cref="SymbolOffset"/>, whereas a language server resolves by position and returns its fallback
/// for a request without one. The name-only entry points of the navigation helpers have no offset to supply.
/// </para>
/// <para>
/// <see cref="DocumentText"/> is the provider's document context, which a provider that resolves by name alone may ignore.
/// </para>
/// </remarks>
public sealed record DefinitionRequest
{
	/// <param name="documentId">
	/// The identifier of the document the text belongs to. A language-server provider maps it to the document's file URI, and a local provider
	/// may ignore it.
	/// </param>
	/// <param name="documentText">The current document snapshot text.</param>
	/// <param name="symbolName">
	/// The target symbol or object name; the value is trimmed, so a blank value becomes an empty string, which names no symbol and resolves
	/// to no definition.
	/// </param>
	/// <param name="discriminator">
	/// An optional language-specific discriminator that disambiguates the target, for example one carried by hover info or an outline node.
	/// </param>
	/// <param name="symbolOffset">
	/// The zero-based UTF-16 offset of the symbol within <paramref name="documentText"/>, or <see langword="null"/> when the caller has none.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="documentId"/>, <paramref name="documentText"/> or <paramref name="symbolName"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="documentId"/> is blank.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="symbolOffset"/> is negative or greater than the length of <paramref name="documentText"/>.
	/// </exception>
	public DefinitionRequest(
		string documentId,
		string documentText,
		string symbolName,
		DefinitionDiscriminator? discriminator = null,
		int? symbolOffset = null
	)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(documentId);

		ArgumentNullException.ThrowIfNull(documentText);
		ArgumentNullException.ThrowIfNull(symbolName);

		if (symbolOffset is { } offset)
			TextOffsetValidation.ValidateOffset(documentText, offset, parameterName: nameof(symbolOffset), offsetDescription: "symbol");

		DocumentId = documentId;
		DocumentText = documentText;
		SymbolName = symbolName.Trim();
		Discriminator = discriminator;
		SymbolOffset = symbolOffset;
	}

	/// <summary>The identifier of the document the text belongs to.</summary>
	public string DocumentId { get; }

	/// <summary>The current document snapshot text.</summary>
	public string DocumentText { get; }

	/// <summary>The trimmed target symbol or object name.</summary>
	public string SymbolName { get; }

	/// <summary>
	/// The optional language-specific discriminator that disambiguates the target;
	/// see <see cref="DefinitionDiscriminator"/> for the recognition contract.
	/// </summary>
	public DefinitionDiscriminator? Discriminator { get; }

	/// <summary>
	/// The zero-based UTF-16 offset of the symbol within <see cref="DocumentText"/>, or <see langword="null"/> when the caller has none.
	/// </summary>
	public int? SymbolOffset { get; }
}

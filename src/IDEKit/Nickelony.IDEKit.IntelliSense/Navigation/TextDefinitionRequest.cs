namespace Nickelony.IDEKit.IntelliSense.Navigation;

/// <summary>
/// Describes a definition lookup request against an immutable document snapshot.
/// </summary>
/// <remarks>
/// The request is a symbol-name lookup: a host that navigates from hover tooltip content or from
/// an outline node supplies the resolved symbol name and the optional language-specific
/// discriminator. It intentionally carries no caret offset because name-catalog providers resolve
/// definitions by symbol name; position-based definition lookups are served by the asynchronous
/// provider contract (<c>ILanguageServerIntelliSenseProvider</c> in
/// <c>Nickelony.LanguageServer.Abstractions</c>), which addresses a document position directly.
/// The snapshot text is the provider's document context: a catalog provider can scope the lookup
/// to the requested document, while a provider that resolves by name alone may ignore it.
/// Symbol names are trimmed before storage; the empty name is a valid request state that
/// represents the absence of a symbol (see <see cref="ITextDefinitionProvider"/>).
/// </remarks>
public sealed record TextDefinitionRequest
{
	/// <summary>
	/// Initializes a new instance of the <see cref="TextDefinitionRequest"/> record.
	/// </summary>
	/// <param name="documentText">The current document snapshot text.</param>
	/// <param name="symbolName">
	/// The target symbol or object name; the value is trimmed, so a blank value becomes an empty
	/// string, which represents the absence of a symbol at the requested position and resolves to
	/// no definition.
	/// </param>
	/// <param name="discriminator">
	/// An optional language-specific discriminator that disambiguates the target, for example one
	/// carried by hover info or an outline node.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="documentText"/> or <paramref name="symbolName"/> is <see langword="null"/>.
	/// </exception>
	public TextDefinitionRequest(string documentText, string symbolName, TextDefinitionDiscriminator? discriminator = null)
	{
		ArgumentNullException.ThrowIfNull(documentText);
		ArgumentNullException.ThrowIfNull(symbolName);

		DocumentText = documentText;
		SymbolName = symbolName.Trim();
		Discriminator = discriminator;
	}

	/// <summary>
	/// Gets the current document snapshot text.
	/// </summary>
	public string DocumentText { get; }

	/// <summary>
	/// Gets the target symbol or object name.
	/// </summary>
	public string SymbolName { get; }

	/// <summary>
	/// Gets the optional language-specific discriminator used to disambiguate the target; see
	/// <see cref="TextDefinitionDiscriminator"/> for the recognition contract.
	/// </summary>
	public TextDefinitionDiscriminator? Discriminator { get; }
}

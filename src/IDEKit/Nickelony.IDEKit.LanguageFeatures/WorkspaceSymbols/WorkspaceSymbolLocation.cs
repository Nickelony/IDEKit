using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.LanguageFeatures.WorkspaceSymbols;

/// <summary>Identifies where a workspace symbol is declared: a document and, once it is known, a range inside it.</summary>
/// <remarks>
/// <see cref="Range"/> uses the line and character model of <see cref="TextPosition"/>; range values are stored as supplied and are not validated
/// against a document.
/// </remarks>
public sealed record WorkspaceSymbolLocation
{
	/// <param name="documentId">The identifier of the document that declares the symbol.</param>
	/// <param name="range">The zero-based range of the symbol, or <see langword="null"/> while the provider has not computed it.</param>
	/// <exception cref="ArgumentNullException"><paramref name="documentId"/> is <see langword="null"/>.</exception>
	public WorkspaceSymbolLocation(string documentId, TextPositionRange? range = null)
	{
		ArgumentNullException.ThrowIfNull(documentId);

		DocumentId = documentId;
		Range = range;
	}

	/// <summary>The identifier of the document that declares the symbol.</summary>
	public string DocumentId { get; }

	/// <summary>The zero-based range of the symbol, or <see langword="null"/> while the provider has not computed it.</summary>
	/// <remarks>
	/// A host that opens the symbol resolves a symbol without a range through <see cref="IWorkspaceSymbolResolver"/> first.
	/// </remarks>
	public TextPositionRange? Range { get; }
}

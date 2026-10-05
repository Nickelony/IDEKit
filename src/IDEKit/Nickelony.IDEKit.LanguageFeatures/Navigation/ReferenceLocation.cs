using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.LanguageFeatures.Navigation;

/// <summary>Identifies a symbol reference in a source file.</summary>
/// <remarks>
/// <see cref="Range"/> uses the line and character model of <see cref="TextPosition"/>; range values are stored as supplied and are not validated
/// against a document.
/// </remarks>
public sealed record ReferenceLocation
{
	/// <param name="documentId">The identifier of the document containing the reference.</param>
	/// <param name="range">The zero-based range of the reference.</param>
	/// <exception cref="ArgumentNullException"><paramref name="documentId"/> is <see langword="null"/>.</exception>
	public ReferenceLocation(string documentId, TextPositionRange range)
	{
		ArgumentNullException.ThrowIfNull(documentId);

		DocumentId = documentId;
		Range = range;
	}

	/// <summary>The identifier of the document containing the reference.</summary>
	public string DocumentId { get; }

	/// <summary>The zero-based range of the reference.</summary>
	public TextPositionRange Range { get; }
}

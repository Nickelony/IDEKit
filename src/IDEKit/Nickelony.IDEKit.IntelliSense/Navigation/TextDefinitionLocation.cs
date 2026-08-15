using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.Infrastructure;

namespace Nickelony.IDEKit.IntelliSense.Navigation;

/// <summary>
/// Identifies a navigable text definition target.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="TargetRange"/> and <see cref="SelectionRange"/> use the line/character unit model
/// documented on <see cref="TextPosition"/>: zero-based positions whose line counts line breaks and
/// whose character counts UTF-16 code units within the line. This is the one position model in the
/// package that is not an offset, because a definition can name a document the provider has not
/// materialized; hosts convert the positions to their own coordinate model when applying the
/// location.
/// </para>
/// <para>
/// <see cref="DocumentId"/> is an opaque provider-defined document identifier (for example a path,
/// a URI, or a logical key). A <see langword="null"/> or blank value means the definition is
/// located in the requested document; a non-empty value names another document whose opening is
/// owned by the host. A blank value is normalized to <see langword="null"/> and other values are
/// trimmed.
/// </para>
/// <para>
/// <see cref="TargetRange"/> covers the whole definition (for example a function declaration).
/// <see cref="SelectionRange"/>, when present, identifies the definition's name and is expected to
/// be contained in the target range; hosts navigate to its start, or to the start of
/// <see cref="TargetRange"/> when it is <see langword="null"/>. Range values are stored as supplied
/// and their containment is not validated, because providers may supply partial range data.
/// </para>
/// </remarks>
public sealed record TextDefinitionLocation
{
	/// <summary>
	/// Initializes a new instance of the <see cref="TextDefinitionLocation"/> record.
	/// </summary>
	/// <param name="targetRange">The zero-based range covering the whole definition.</param>
	/// <param name="documentId">
	/// The opaque identifier of the document that contains the definition, or <see langword="null"/>
	/// for a definition in the requested document. A blank value is normalized to
	/// <see langword="null"/> and other values are trimmed.
	/// </param>
	/// <param name="selectionRange">
	/// The optional zero-based range identifying the definition's name, or <see langword="null"/>
	/// when the target range also identifies the selection.
	/// </param>
	public TextDefinitionLocation(
		TextPositionRange targetRange,
		string? documentId = null,
		TextPositionRange? selectionRange = null)
	{
		TargetRange = targetRange;
		DocumentId = OptionalText.Normalize(documentId);
		SelectionRange = selectionRange;
	}

	/// <summary>
	/// Gets the zero-based range covering the whole definition.
	/// </summary>
	public TextPositionRange TargetRange { get; }

	/// <summary>
	/// Gets the opaque identifier of the document that contains the definition, or
	/// <see langword="null"/> when the definition is located in the requested document.
	/// </summary>
	public string? DocumentId { get; }

	/// <summary>
	/// Gets the optional zero-based range identifying the definition's name, or
	/// <see langword="null"/> when the target range also identifies the selection.
	/// </summary>
	public TextPositionRange? SelectionRange { get; }

	/// <summary>
	/// Gets the zero-based position a host navigates to: the start of <see cref="SelectionRange"/> when it
	/// is present, otherwise the start of <see cref="TargetRange"/>.
	/// </summary>
	/// <remarks>
	/// The property applies the type's documented navigation rule so every host shares it; the position uses
	/// the same line and character model as the ranges it is derived from.
	/// </remarks>
	public TextPosition NavigationStart => (SelectionRange ?? TargetRange).Start;
}

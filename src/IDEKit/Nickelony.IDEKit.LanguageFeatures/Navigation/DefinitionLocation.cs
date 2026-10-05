using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.LanguageFeatures.Navigation;

/// <summary>Identifies a navigable definition target.</summary>
/// <remarks>
/// <see cref="TargetRange"/> and <see cref="SelectionRange"/> use the line and character model of <see cref="TextPosition"/> instead of offsets,
/// because a definition can name a document the provider has not materialized. <see cref="DocumentId"/> is an opaque provider-defined identifier
/// for that other document; range values are stored as supplied and their containment is not validated.
/// </remarks>
public sealed record DefinitionLocation
{
	/// <param name="targetRange">The zero-based range covering the whole definition.</param>
	/// <param name="documentId">
	/// The opaque identifier of the document that contains the definition, or <see langword="null"/> for a definition in the requested document.
	/// A blank value is normalized to <see langword="null"/> and other values are trimmed.
	/// </param>
	/// <param name="selectionRange">
	/// The optional zero-based range identifying the definition's name, or <see langword="null"/> when the target range also identifies
	/// the selection.
	/// </param>
	public DefinitionLocation(TextPositionRange targetRange, string? documentId = null, TextPositionRange? selectionRange = null)
	{
		TargetRange = targetRange;
		DocumentId = OptionalTextNormalizer.Normalize(documentId);
		SelectionRange = selectionRange;
	}

	/// <summary>The zero-based range covering the whole definition.</summary>
	public TextPositionRange TargetRange { get; }

	/// <summary>
	/// The opaque identifier of the document that contains the definition, or <see langword="null"/> when the definition is located
	/// in the requested document.
	/// </summary>
	public string? DocumentId { get; }

	/// <summary>
	/// The optional zero-based range identifying the definition's name, or <see langword="null"/> when the target range also identifies
	/// the selection.
	/// </summary>
	public TextPositionRange? SelectionRange { get; }

	/// <summary>
	/// Returns the zero-based position a host navigates to: the start of <see cref="SelectionRange"/> when it is present, otherwise the start
	/// of <see cref="TargetRange"/>.
	/// </summary>
	public TextPosition NavigationStart => (SelectionRange ?? TargetRange).Start;
}

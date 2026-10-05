using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Navigation;

/// <summary>Identifies a navigable position in an editor document and the preferred line for scroll restoration.</summary>
/// <param name="DocumentId">The identifier of the document the location belongs to, or <see langword="null"/> when the document has none.</param>
/// <param name="CaretOffset">The zero-based caret offset within the document.</param>
/// <param name="Selection">The selected range; an empty range is a caret without a selection.</param>
/// <param name="PreferredDocumentLine">The one-based document line the host should prefer when restoring the view, when known.</param>
/// <remarks>
/// Equality compares the navigation identity - document identifier ordinally, caret offset,
/// and selection - and ignores <see cref="PreferredDocumentLine"/>, which is view state for scroll restoration rather than part of the position.
/// Two locations without a document identifier are equivalent under <see cref="IsEquivalentTo(NavigationLocation, StringComparison)"/>
/// when their offsets match, keeping identity stable for an untitled document.
/// </remarks>
public readonly record struct NavigationLocation(string? DocumentId, int CaretOffset, TextRange Selection, int? PreferredDocumentLine)
{
	/// <summary>Determines whether this location describes the same logical position as <paramref name="other"/>.</summary>
	/// <remarks>
	/// This overload compares document identifiers ordinally, so a host whose document identities are case-insensitive uses
	/// <see cref="IsEquivalentTo(NavigationLocation, StringComparison)"/> instead.
	/// </remarks>
	public bool Equals(NavigationLocation other) => IsEquivalentTo(other, StringComparison.Ordinal);

	/// <inheritdoc/>
	public override int GetHashCode() => HashCode.Combine(value1: DocumentId, value2: CaretOffset, value3: Selection);

	/// <summary>
	/// Determines whether this location represents the same logical position as <paramref name="other"/>, ignoring the preferred document line.
	/// </summary>
	/// <remarks>
	/// A host whose document identities are case-insensitive passes <see cref="StringComparison.OrdinalIgnoreCase"/>,
	/// or the <see cref="Paths.LocalPathComparisonPolicy.Comparison"/> of the path-identity policy it applies elsewhere, so every identifier
	/// comparison uses one policy.
	/// </remarks>
	public bool IsEquivalentTo(NavigationLocation other, StringComparison idComparison = StringComparison.Ordinal) =>
		string.Equals(DocumentId, other.DocumentId, idComparison) && CaretOffset == other.CaretOffset && Selection == other.Selection;
}

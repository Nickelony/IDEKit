namespace Nickelony.IDEKit.Core.Navigation;

/// <summary>
/// Identifies a navigable position in an editor document: the logical file, the caret offset, the
/// selection range, and an optional preferred document line for scroll restoration.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="FilePath"/> is <see langword="null"/> when the document is not associated with a file,
/// for example an unsaved document. Two locations without a file path are equivalent under
/// <see cref="IsEquivalentTo(NavigationLocation, StringComparison)"/> when their offsets match, which
/// keeps navigation identity stable while a document is untitled.
/// </para>
/// <para>
/// Equality compares the navigation identity - file path (ordinally), caret offset, selection start, and
/// selection length - and ignores <see cref="PreferredDocumentLine"/>, which is view state carried alongside the
/// identity for scroll restoration rather than part of the position itself.
/// <see cref="IsEquivalentTo(NavigationLocation, StringComparison)"/> offers the same comparison with a
/// configurable path comparison for hosts whose document identities are case-insensitive.
/// </para>
/// </remarks>
/// <param name="FilePath">
/// The logical path of the document the location belongs to, or <see langword="null"/> when the
/// document has no file path.
/// </param>
/// <param name="CaretOffset">The zero-based caret offset within the document.</param>
/// <param name="SelectionStart">The zero-based start offset of the selection.</param>
/// <param name="SelectionLength">The length of the selection in UTF-16 code units.</param>
/// <param name="PreferredDocumentLine">
/// The one-based document line the host should prefer when restoring the view, when known. This is
/// scroll restoration state carried alongside the location, not part of its navigation identity (see
/// <see cref="Equals(NavigationLocation)"/>).
/// </param>
public readonly record struct NavigationLocation(
	string? FilePath,
	int CaretOffset,
	int SelectionStart,
	int SelectionLength,
	int? PreferredDocumentLine)
{
	/// <summary>
	/// Determines whether this location describes the same logical position as <paramref name="other"/>.
	/// </summary>
	/// <remarks>
	/// The compared members and the excluded view state are described on the type; this overload
	/// compares file paths ordinally, so hosts whose document identities are case-insensitive use
	/// <see cref="IsEquivalentTo(NavigationLocation, StringComparison)"/> instead.
	/// </remarks>
	/// <param name="other">The location to compare with.</param>
	/// <returns><see langword="true"/> when both locations describe the same logical position.</returns>
	public bool Equals(NavigationLocation other)
		=> IsEquivalentTo(other, StringComparison.Ordinal);

	/// <inheritdoc/>
	public override int GetHashCode()
		=> HashCode.Combine(FilePath, CaretOffset, SelectionStart, SelectionLength);

	/// <summary>
	/// Determines whether this location represents the same logical position as
	/// <paramref name="other"/>, ignoring the preferred document line.
	/// </summary>
	/// <remarks>
	/// File paths compare ordinally by default. Hosts whose document identities are case-insensitive
	/// pass <see cref="StringComparison.OrdinalIgnoreCase"/>, or the
	/// <see cref="Pathing.LocalPathComparisonPolicy.Comparison"/> of the path-identity policy the
	/// host applies elsewhere, so every path comparison in the host uses one policy.
	/// </remarks>
	/// <param name="other">The location to compare with.</param>
	/// <param name="pathComparison">The comparison used for the file paths.</param>
	/// <returns><see langword="true"/> when both locations describe the same logical position.</returns>
	public bool IsEquivalentTo(NavigationLocation other, StringComparison pathComparison = StringComparison.Ordinal)
	{
		return string.Equals(FilePath, other.FilePath, pathComparison)
			&& CaretOffset == other.CaretOffset
			&& SelectionStart == other.SelectionStart
			&& SelectionLength == other.SelectionLength;
	}
}

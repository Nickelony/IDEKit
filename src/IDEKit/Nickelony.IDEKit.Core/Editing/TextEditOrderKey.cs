using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Editing;

/// <summary>The shared ordering key for edit operations: ascending start offset, then ascending end offset, then ascending edit index.</summary>
/// <remarks>
/// The edit-index tie-break keeps the order total while edit indexes are unique per batch, so diagnostics are deterministic; a hand-built batch
/// that reuses an edit index leaves the tied operations unordered.
/// </remarks>
internal readonly record struct TextEditOrderKey(TextRange Range, int EditIndex) : IComparable<TextEditOrderKey>
{
	internal static TextEditOrderKey From(TextEditOperation operation) => new(operation.Range, operation.EditIndex);

	/// <inheritdoc/>
	public int CompareTo(TextEditOrderKey other)
	{
		int startComparison = Range.Offset.CompareTo(other.Range.Offset);

		if (startComparison != 0)
			return startComparison;

		int endComparison = Range.EndOffset.CompareTo(other.Range.EndOffset);

		return endComparison != 0 ? endComparison : EditIndex.CompareTo(other.EditIndex);
	}
}

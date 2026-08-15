using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Editing;

/// <summary>
/// The shared ordering key for edit operations: ascending start offset, then ascending end offset,
/// then ascending edit index. The edit-index tie-break keeps the order total while edit indexes
/// are unique per batch, so diagnostics are deterministic; a hand-built batch that reuses an edit
/// index has no defined relative order for the tied operations.
/// </summary>
/// <remarks>
/// The preparation kernel and the conflict detector sort their candidates through this key, so a
/// batch is sorted and diagnosed under one ordering contract.
/// </remarks>
/// <param name="StartOffset">The operation's source start offset.</param>
/// <param name="EndOffset">The operation's source end offset.</param>
/// <param name="EditIndex">The operation's edit index (its position in the caller's batch).</param>
internal readonly record struct TextEditOrderKey(int StartOffset, int EndOffset, int EditIndex)
	: IComparable<TextEditOrderKey>
{
	/// <summary>
	/// Creates the order key of an operation.
	/// </summary>
	/// <param name="operation">The operation to key.</param>
	/// <returns>The operation's order key.</returns>
	internal static TextEditOrderKey From(TextEditOperation operation)
		=> new(operation.StartOffset, operation.EndOffset, operation.EditIndex);

	/// <inheritdoc/>
	public int CompareTo(TextEditOrderKey other)
	{
		int startComparison = StartOffset.CompareTo(other.StartOffset);

		if (startComparison != 0)
			return startComparison;

		int endComparison = EndOffset.CompareTo(other.EndOffset);

		return endComparison != 0
			? endComparison
			: EditIndex.CompareTo(other.EditIndex);
	}
}

namespace Nickelony.IDEKit.Core.Text;

/// <summary>
/// Describes one text replacement using zero-based UTF-16 offsets.
/// </summary>
/// <remarks>
/// The validating constructor is the only way to produce a value, and the properties are get-only,
/// so no object initializer or <c>with</c> expression can bypass validation. The constructor rejects
/// a <see langword="null"/> replacement text, a negative start offset, and an end offset that
/// precedes the start offset, so <see cref="Length"/> is never negative. Batch carriers such as
/// <see cref="Nickelony.IDEKit.Core.Editing.PreparedTextEdits"/> additionally reject overlapping
/// operations and conflicting ordering.
/// </remarks>
public sealed record TextEditOperation
{
	/// <summary>
	/// Initializes a new instance of the <see cref="TextEditOperation"/> class.
	/// </summary>
	/// <param name="startOffset">The zero-based inclusive source offset.</param>
	/// <param name="endOffset">The zero-based exclusive source offset.</param>
	/// <param name="newText">The replacement text. An empty string deletes the range.</param>
	/// <param name="editIndex">
	/// The index of the edit within the caller's batch, used for deterministic ordering and diagnostics.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="newText"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="startOffset"/> is negative, or <paramref name="endOffset"/> is less than
	/// <paramref name="startOffset"/>.
	/// </exception>
	public TextEditOperation(int startOffset, int endOffset, string newText, int editIndex)
	{
		ArgumentNullException.ThrowIfNull(newText);
		ArgumentOutOfRangeException.ThrowIfNegative(startOffset);
		ArgumentOutOfRangeException.ThrowIfLessThan(endOffset, startOffset);

		StartOffset = startOffset;
		EndOffset = endOffset;
		NewText = newText;
		EditIndex = editIndex;
	}

	/// <summary>
	/// Gets the zero-based inclusive source offset.
	/// </summary>
	public int StartOffset { get; }

	/// <summary>
	/// Gets the zero-based exclusive source offset.
	/// </summary>
	public int EndOffset { get; }

	/// <summary>
	/// Gets the replacement text; an empty string deletes the range.
	/// </summary>
	public string NewText { get; }

	/// <summary>
	/// Gets the index of the edit within the caller's batch, used for deterministic ordering and diagnostics.
	/// </summary>
	public int EditIndex { get; }

	/// <summary>
	/// Gets the number of source UTF-16 code units replaced by the operation.
	/// </summary>
	public int Length => EndOffset - StartOffset;

	/// <summary>
	/// Gets a value indicating whether the operation changes nothing: it replaces an empty range
	/// with an empty string.
	/// </summary>
	/// <remarks>
	/// A no-op operation cannot conflict with another operation or violate batch ordering, so the
	/// preparation kernel and the batch carriers skip it.
	/// </remarks>
	public bool IsNoOp => Length == 0 && NewText.Length == 0;

	/// <summary>
	/// Deconstructs the operation into its source range, replacement text, and edit index.
	/// </summary>
	/// <param name="startOffset">The zero-based inclusive source offset.</param>
	/// <param name="endOffset">The zero-based exclusive source offset.</param>
	/// <param name="newText">The replacement text.</param>
	/// <param name="editIndex">
	/// The index of the edit within the caller's batch, used for deterministic ordering and diagnostics.
	/// </param>
	public void Deconstruct(out int startOffset, out int endOffset, out string newText, out int editIndex)
	{
		startOffset = StartOffset;
		endOffset = EndOffset;
		newText = NewText;
		editIndex = EditIndex;
	}
}

namespace Nickelony.IDEKit.Core.Editing;

/// <summary>
/// Describes a single text edit: a replaced range, its replacement text, and an optional caret offset.
/// </summary>
/// <remarks>
/// <para>
/// The request keeps the edited range, the replacement text, and the optional caret offset in one value,
/// so a call site names each part instead of relying on the order of a start offset, a length, and a
/// caret offset. A request with a zero <see cref="Length"/> inserts <see cref="NewText"/> at
/// <see cref="StartOffset"/>; a request with a non-zero length replaces that range.
/// </para>
/// <para>
/// The range is validated when the request is constructed: the start offset and length are non-negative
/// and the range end cannot exceed <see cref="int.MaxValue"/>. An editor binding applies the request to
/// its document through its own edit-application helper.
/// </para>
/// </remarks>
public readonly record struct TextEditRequest
{
	/// <summary>
	/// Initializes a new instance of the <see cref="TextEditRequest"/> record struct.
	/// </summary>
	/// <param name="startOffset">The zero-based start offset of the edited range.</param>
	/// <param name="length">The length of the replaced range; zero for a pure insertion.</param>
	/// <param name="newText">The replacement text.</param>
	/// <exception cref="ArgumentNullException"><paramref name="newText"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="startOffset"/> or <paramref name="length"/> is negative, or the range end would
	/// exceed <see cref="int.MaxValue"/>.
	/// </exception>
	public TextEditRequest(int startOffset, int length, string newText)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(startOffset);
		ArgumentOutOfRangeException.ThrowIfNegative(length);

		// The range end is stored as startOffset + length, so reject a range whose end cannot be represented.
		if (startOffset > int.MaxValue - length)
			throw new ArgumentOutOfRangeException(nameof(startOffset), startOffset, "The range end must not exceed Int32.MaxValue.");

		ArgumentNullException.ThrowIfNull(newText);

		StartOffset = startOffset;
		Length = length;
		NewText = newText;
	}

	/// <summary>Gets the zero-based start offset of the edited range.</summary>
	public int StartOffset { get; }

	/// <summary>Gets the length of the replaced range; zero for a pure insertion.</summary>
	public int Length { get; }

	/// <summary>Gets the replacement text.</summary>
	public string NewText { get; }

	/// <summary>
	/// Gets the desired zero-based caret offset after the edit, or <see langword="null"/> to place the
	/// caret just after <see cref="NewText"/>. Either way the offset is clamped to the target document's
	/// current length when the request is applied.
	/// </summary>
	public int? CaretOffsetAfterEdit { get; init; }
}

namespace Nickelony.IDEKit.Core.Text;

/// <summary>
/// Represents a contiguous range of text identified by a zero-based UTF-16 offset and length.
/// </summary>
/// <remarks>
/// This is the offset-first range every internal consumer uses: a zero-based UTF-16 offset is the canonical
/// address of this assembly, and a line/character pair (<see cref="TextPositionRange"/>) exists only at a
/// protocol boundary, where it is converted to an offset or from one.
/// </remarks>
public readonly record struct TextRange
{
	/// <summary>
	/// Gets the zero-based UTF-16 offset of the start of the range.
	/// </summary>
	public int Offset { get; }

	/// <summary>
	/// Gets the length of the range in UTF-16 code units.
	/// </summary>
	public int Length { get; }

	/// <summary>
	/// Gets the zero-based UTF-16 offset of the first character after the range.
	/// </summary>
	/// <remarks>
	/// The value never exceeds <see cref="int.MaxValue"/>: the constructor rejects a negative offset
	/// or length and any range whose end cannot be represented.
	/// </remarks>
	public int EndOffset => Offset + Length;

	/// <summary>
	/// Gets a value indicating whether this range is empty.
	/// </summary>
	public bool IsEmpty => Length == 0;

	/// <summary>
	/// Initializes a new instance of the <see cref="TextRange"/> struct.
	/// </summary>
	/// <param name="offset">The zero-based UTF-16 start offset.</param>
	/// <param name="length">The length in UTF-16 code units.</param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// An argument is negative, or the resulting end offset exceeds <see cref="int.MaxValue"/>.
	/// </exception>
	public TextRange(int offset, int length)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(offset);
		ArgumentOutOfRangeException.ThrowIfNegative(length);

		// EndOffset is computed as offset + length, so reject a range whose end cannot be represented.
		if (offset > int.MaxValue - length)
			throw new ArgumentOutOfRangeException(nameof(length), "The range end exceeds the maximum supported offset.");

		Offset = offset;
		Length = length;
	}

	/// <summary>
	/// Determines whether this range fits within a text of the supplied length: the range offset is
	/// inside the text (or at its very end) and the range does not extend past it.
	/// </summary>
	/// <remarks>
	/// This is the one "does this range fit the text" predicate of the package, so an edit
	/// preparation, a commit applier, and a clamp site share it while each keeps its own reaction to
	/// a range that does not fit (a preparation issue, a skipped entry, or a clamped range).
	/// </remarks>
	/// <param name="textLength">The length of the text this range refers to.</param>
	/// <returns><see langword="true"/> when the range fits; otherwise, <see langword="false"/>.</returns>
	public bool FitsWithin(int textLength)
		=> TextBounds.Fits(textLength, Offset, Length);

	/// <summary>
	/// Normalizes an offset range into a non-empty range within a text of the supplied length.
	/// </summary>
	/// <remarks>
	/// For a non-empty text, empty or reversed ranges become a range of length <c>1</c> that starts
	/// at the clamped start offset: the start is clamped to the last character, and an end offset at
	/// or before the start is raised to one character later. A caller that filters a range before
	/// drawing or reporting it (for example a render pass that discards invisible ranges) normalizes
	/// through this method so the filter uses the offsets the range is drawn for.
	/// </remarks>
	/// <param name="textLength">The length of the text the offsets refer to.</param>
	/// <param name="startOffset">The zero-based inclusive start offset before clamping.</param>
	/// <param name="endOffset">The zero-based exclusive end offset before clamping.</param>
	/// <returns>
	/// The normalized range, or <see langword="null"/> when the text is empty.
	/// </returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="textLength"/> is negative.</exception>
	public static TextRange? Normalize(int textLength, int startOffset, int endOffset)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(textLength);

		if (textLength == 0)
			return null;

		int normalizedStartOffset = Math.Max(0, Math.Min(startOffset, textLength - 1));
		int normalizedEndOffset = Math.Max(normalizedStartOffset + 1, Math.Min(endOffset, textLength));

		return new TextRange(normalizedStartOffset, normalizedEndOffset - normalizedStartOffset);
	}

	/// <summary>
	/// Returns the text represented by this range from the given text.
	/// </summary>
	/// <param name="text">The text to slice.</param>
	/// <returns>The text within this range.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The range extends beyond the end of <paramref name="text"/>; the message identifies the
	/// offending range component.
	/// </exception>
	public string GetText(string text)
	{
		ArgumentNullException.ThrowIfNull(text);

		// The text is the argument that cannot satisfy this range, so the exception names it; each
		// message identifies the offending range component and its value.
		if (!TextBounds.ContainsOffset(text.Length, Offset))
			throw new ArgumentOutOfRangeException(nameof(text), $"The range offset ({Offset}) is beyond the end of the text (length {text.Length}).");

		if (Length > text.Length - Offset)
			throw new ArgumentOutOfRangeException(nameof(text), $"The range end ({EndOffset}) is beyond the end of the text (length {text.Length}).");

		return text.Substring(Offset, Length);
	}

	/// <summary>
	/// Returns the text represented by this range from the given snapshot.
	/// </summary>
	/// <param name="snapshot">The snapshot to slice.</param>
	/// <returns>The text within this range.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="snapshot"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The range extends beyond the end of the snapshot text; the message identifies the offending
	/// range component.
	/// </exception>
	public string GetTextFrom(ITextSnapshot snapshot)
	{
		ArgumentNullException.ThrowIfNull(snapshot);

		if (!TextBounds.ContainsOffset(snapshot.TextLength, Offset))
			throw new ArgumentOutOfRangeException(nameof(snapshot), $"The range offset ({Offset}) is beyond the end of the snapshot text (length {snapshot.TextLength}).");

		if (Length > snapshot.TextLength - Offset)
			throw new ArgumentOutOfRangeException(nameof(snapshot), $"The range end ({EndOffset}) is beyond the end of the snapshot text (length {snapshot.TextLength}).");

		return snapshot.GetText(Offset, Length);
	}

	/// <summary>
	/// Returns the range in half-open interval notation, for example <c>[4..10)</c>.
	/// </summary>
	/// <returns>A string in the form <c>[Offset..EndOffset)</c>.</returns>
	public override string ToString()
		=> $"[{Offset}..{EndOffset})";
}

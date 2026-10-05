namespace Nickelony.IDEKit.Core.Text;

/// <summary>Represents a contiguous range of text identified by a zero-based UTF-16 offset and length.</summary>
/// <remarks>
/// The range is addressed by zero-based UTF-16 offsets. <see cref="TextPositionRange"/> addresses a range by line and character instead.
/// </remarks>
public readonly record struct TextRange
{
	/// <summary>The zero-based UTF-16 offset of the start of the range.</summary>
	public int Offset { get; }

	/// <summary>The length of the range in UTF-16 code units.</summary>
	public int Length { get; }

	/// <summary>The zero-based UTF-16 offset of the first character after the range.</summary>
	/// <remarks>
	/// The value never exceeds <see cref="int.MaxValue"/>: the constructor rejects a negative offset or length and any range whose end
	/// cannot be represented.
	/// </remarks>
	public int EndOffset => Offset + Length;

	public bool IsEmpty => Length == 0;

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

	/// <summary>Creates a range from its start offset up to its end offset.</summary>
	/// <param name="startOffset">The zero-based UTF-16 start offset.</param>
	/// <param name="endOffset">The zero-based UTF-16 offset of the first character after the range.</param>
	/// <returns>The range that starts at <paramref name="startOffset"/> and ends at <paramref name="endOffset"/>.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="startOffset"/> is negative, or <paramref name="endOffset"/> is before <paramref name="startOffset"/>.
	/// </exception>
	public static TextRange FromBounds(int startOffset, int endOffset)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(startOffset);
		ArgumentOutOfRangeException.ThrowIfLessThan(endOffset, startOffset);

		return new TextRange(startOffset, endOffset - startOffset);
	}

	/// <summary>
	/// Determines whether this range fits within a text of the supplied length: the range offset is inside the text (or at its very end)
	/// and the range does not extend past it.
	/// </summary>
	/// <param name="textLength">The length of the text this range refers to.</param>
	/// <returns><see langword="true"/> when the range fits; otherwise, <see langword="false"/>.</returns>
	public bool FitsWithin(int textLength) => TextBounds.Fits(textLength, Offset, Length);

	/// <summary>Determines whether this range and the other range share a position.</summary>
	/// <param name="other">The range to compare with.</param>
	/// <returns><see langword="true"/> when the ranges intersect; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// The rule is closed: ranges that touch intersect, and an empty range intersects a range that contains its offset or has that offset
	/// at either boundary, so a caret at the end of a span still reaches it. Two empty ranges intersect when they share an offset.
	/// </remarks>
	public bool Intersects(TextRange other) => Offset <= other.EndOffset && other.Offset <= EndOffset;

	/// <summary>Normalizes a range into a non-empty range within a text of the supplied length.</summary>
	/// <param name="range">The range to normalize.</param>
	/// <param name="textLength">The length of the text the range refers to.</param>
	/// <returns>The normalized range, or <see langword="null"/> when the text is empty.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="textLength"/> is negative.</exception>
	/// <remarks>
	/// For a non-empty text, the start is clamped to the last character and the end to the text length, and an empty range becomes a range of length
	/// <c>1</c> that starts at the clamped start offset.
	/// </remarks>
	public static TextRange? Normalize(TextRange range, int textLength)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(textLength);

		if (textLength == 0)
			return null;

		int normalizedStartOffset = Math.Min(range.Offset, textLength - 1);
		int normalizedEndOffset = Math.Max(normalizedStartOffset + 1, Math.Min(range.EndOffset, textLength));

		return FromBounds(normalizedStartOffset, normalizedEndOffset);
	}

	/// <summary>Returns the text represented by this range from the given text.</summary>
	/// <param name="text">The text to slice.</param>
	/// <returns>The text within this range.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The range extends beyond the end of <paramref name="text"/>; the message identifies the offending range component.
	/// </exception>
	public string GetText(string text)
	{
		ArgumentNullException.ThrowIfNull(text);

		// The text is the argument that cannot satisfy this range, so the exception names it; each
		// message identifies the offending range component and its value.
		if (!TextBounds.ContainsOffset(text.Length, Offset))
			throw new ArgumentOutOfRangeException(nameof(text), $"The range offset ({Offset}) is beyond the end of the text (length {text.Length}).");

		if (!FitsWithin(text.Length))
			throw new ArgumentOutOfRangeException(nameof(text), $"The range end ({EndOffset}) is beyond the end of the text (length {text.Length}).");

		return text.Substring(Offset, Length);
	}

	/// <summary>Returns the text represented by this range from the given snapshot.</summary>
	/// <param name="snapshot">The snapshot to slice.</param>
	/// <returns>The text within this range.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="snapshot"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The range extends beyond the end of the snapshot text; the message identifies the offending range component.
	/// </exception>
	public string GetTextFrom(ITextSnapshot snapshot)
	{
		ArgumentNullException.ThrowIfNull(snapshot);

		if (!TextBounds.ContainsOffset(snapshot.TextLength, Offset))
		{
			throw new ArgumentOutOfRangeException(
				nameof(snapshot),
				$"The range offset ({Offset}) is beyond the end of the snapshot text (length {snapshot.TextLength})."
			);
		}

		if (!FitsWithin(snapshot.TextLength))
		{
			throw new ArgumentOutOfRangeException(
				nameof(snapshot),
				$"The range end ({EndOffset}) is beyond the end of the snapshot text (length {snapshot.TextLength})."
			);
		}

		return snapshot.GetText(Offset, Length);
	}

	/// <summary>Returns the range in half-open interval notation, for example <c>[4..10)</c>.</summary>
	/// <returns>A string in the form <c>[Offset..EndOffset)</c>.</returns>
	public override string ToString() => $"[{Offset}..{EndOffset})";
}

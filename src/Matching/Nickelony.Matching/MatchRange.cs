namespace Nickelony.Matching;

/// <summary>A run of consecutive matched characters of a candidate: a zero-based UTF-16 offset and a length.</summary>
public readonly record struct MatchRange
{
	/// <param name="offset">The zero-based UTF-16 offset of the first matched character.</param>
	/// <param name="length">The number of matched characters, in UTF-16 code units.</param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="offset"/> or <paramref name="length"/> is negative, or the end of the range exceeds <see cref="int.MaxValue"/>.
	/// </exception>
	public MatchRange(int offset, int length)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(offset);
		ArgumentOutOfRangeException.ThrowIfNegative(length);

		if (offset > int.MaxValue - length)
			throw new ArgumentOutOfRangeException(nameof(length), "The end of the range exceeds the largest offset.");

		Offset = offset;
		Length = length;
	}

	/// <summary>The zero-based UTF-16 offset of the first matched character.</summary>
	public int Offset { get; }

	/// <summary>The number of matched characters, in UTF-16 code units.</summary>
	public int Length { get; }

	/// <summary>The zero-based UTF-16 offset of the first character after the range.</summary>
	public int EndOffset => Offset + Length;
}

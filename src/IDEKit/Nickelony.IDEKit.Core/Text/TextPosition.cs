namespace Nickelony.IDEKit.Core.Text;

/// <summary>Represents a zero-based line and character position in text.</summary>
/// <param name="Line">The zero-based line index.</param>
/// <param name="Character">The zero-based character index within the line.</param>
/// <remarks>
/// No bounds check is performed; a position can arrive from an untrusted protocol boundary with negative or out-of-document coordinates,
/// so the conversions on <see cref="TextLineMap"/> clamp out-of-document values, and <see cref="ClampNegativeToZero"/> zeroes a negative coordinate.
/// A line index counts line breaks and a character index counts UTF-16 code units within its line; a tab is one code unit and is not expanded
/// to a tab stop. Positions order by line and then by character, whatever their values.
/// </remarks>
public readonly record struct TextPosition(int Line, int Character) : IComparable<TextPosition>
{
	/// <summary>Determines whether the left position is before the right position.</summary>
	/// <param name="left">The first position.</param>
	/// <param name="right">The second position.</param>
	/// <returns><see langword="true"/> when <paramref name="left"/> is on an earlier line, or on the same line at an earlier character.</returns>
	public static bool operator <(TextPosition left, TextPosition right) => left.CompareTo(right) < 0;

	/// <summary>Determines whether the left position is after the right position.</summary>
	/// <param name="left">The first position.</param>
	/// <param name="right">The second position.</param>
	/// <returns><see langword="true"/> when <paramref name="left"/> is on a later line, or on the same line at a later character.</returns>
	public static bool operator >(TextPosition left, TextPosition right) => left.CompareTo(right) > 0;

	/// <summary>Determines whether the left position is before the right position or equal to it.</summary>
	/// <param name="left">The first position.</param>
	/// <param name="right">The second position.</param>
	/// <returns><see langword="true"/> when <paramref name="left"/> is not after <paramref name="right"/>.</returns>
	public static bool operator <=(TextPosition left, TextPosition right) => left.CompareTo(right) <= 0;

	/// <summary>Determines whether the left position is after the right position or equal to it.</summary>
	/// <param name="left">The first position.</param>
	/// <param name="right">The second position.</param>
	/// <returns><see langword="true"/> when <paramref name="left"/> is not before <paramref name="right"/>.</returns>
	public static bool operator >=(TextPosition left, TextPosition right) => left.CompareTo(right) >= 0;

	/// <summary>Compares this position with another by line and then by character.</summary>
	/// <param name="other">The position to compare with.</param>
	/// <returns>
	/// A negative value when this position is before <paramref name="other"/>, zero when they are equal, and a positive value otherwise.
	/// </returns>
	public int CompareTo(TextPosition other)
	{
		int byLine = Line.CompareTo(other.Line);
		return byLine != 0 ? byLine : Character.CompareTo(other.Character);
	}

	/// <summary>Returns this position with a negative line or character value changed to zero.</summary>
	/// <remarks>
	/// A coordinate supplied by a caller can be negative when it was not validated; a non-negative value is returned unchanged.
	/// </remarks>
	public TextPosition ClampNegativeToZero() => new(Math.Max(0, Line), Math.Max(0, Character));
}

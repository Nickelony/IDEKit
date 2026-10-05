using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Represents a zero-based protocol position within a text document.</summary>
/// <param name="Line">The zero-based line index.</param>
/// <param name="Character">The zero-based character index on the line, measured in UTF-16 code units.</param>
/// <remarks>
/// Positions order by line and then by character, whatever their values, so a payload that arrives with a negative coordinate still has a place
/// in the order.
/// </remarks>
public readonly record struct PositionPayload([property: JsonPropertyName("line")] int Line, [property: JsonPropertyName("character")] int Character)
	: IComparable<PositionPayload>
{
	/// <summary>Determines whether the left position is before the right position.</summary>
	/// <param name="left">The first position.</param>
	/// <param name="right">The second position.</param>
	/// <returns><see langword="true"/> when <paramref name="left"/> is on an earlier line, or on the same line at an earlier character.</returns>
	public static bool operator <(PositionPayload left, PositionPayload right) => left.CompareTo(right) < 0;

	/// <summary>Determines whether the left position is after the right position.</summary>
	/// <param name="left">The first position.</param>
	/// <param name="right">The second position.</param>
	/// <returns><see langword="true"/> when <paramref name="left"/> is on a later line, or on the same line at a later character.</returns>
	public static bool operator >(PositionPayload left, PositionPayload right) => left.CompareTo(right) > 0;

	/// <summary>Determines whether the left position is before the right position or equal to it.</summary>
	/// <param name="left">The first position.</param>
	/// <param name="right">The second position.</param>
	/// <returns><see langword="true"/> when <paramref name="left"/> is not after <paramref name="right"/>.</returns>
	public static bool operator <=(PositionPayload left, PositionPayload right) => left.CompareTo(right) <= 0;

	/// <summary>Determines whether the left position is after the right position or equal to it.</summary>
	/// <param name="left">The first position.</param>
	/// <param name="right">The second position.</param>
	/// <returns><see langword="true"/> when <paramref name="left"/> is not before <paramref name="right"/>.</returns>
	public static bool operator >=(PositionPayload left, PositionPayload right) => left.CompareTo(right) >= 0;

	/// <summary>Compares this position with another by line and then by character.</summary>
	/// <param name="other">The position to compare with.</param>
	/// <returns>
	/// A negative value when this position is before <paramref name="other"/>, zero when they are equal, and a positive value otherwise.
	/// </returns>
	public int CompareTo(PositionPayload other)
	{
		int byLine = Line.CompareTo(other.Line);
		return byLine != 0 ? byLine : Character.CompareTo(other.Character);
	}
}

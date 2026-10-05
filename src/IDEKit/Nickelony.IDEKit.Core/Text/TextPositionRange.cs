namespace Nickelony.IDEKit.Core.Text;

/// <summary>Represents a text range using zero-based line and character positions.</summary>
/// <param name="Start">The zero-based position where the range starts; the endpoint is inclusive when the range is ordered.</param>
/// <param name="End">The zero-based position where the range ends; the endpoint is exclusive when the range is ordered.</param>
/// <remarks>
/// The pair is stored as supplied: no bounds check is performed and no ordering is enforced, and each endpoint uses the unit
/// model of <see cref="TextPosition"/>. Callers interpret the range as half-open - <see cref="Start"/> included, <see cref="End"/> excluded -
/// and <see cref="TextLineMap.TryGetOffsets"/> clamps out-of-document endpoints and fails for a reversed range.
/// </remarks>
public readonly record struct TextPositionRange(TextPosition Start, TextPosition End)
{
	/// <summary>Indicates whether the range is empty (its endpoints are equal).</summary>
	/// <remarks>
	/// A reversed range is not empty; it contains no positions but still spans two endpoints.
	/// </remarks>
	public bool IsEmpty => Start == End;

	/// <summary>Indicates whether the range ends before it starts.</summary>
	/// <remarks>
	/// A reversed range identifies no content, so a caller that converts or applies a range rejects it instead of reading it as the range
	/// between its endpoints.
	/// </remarks>
	public bool IsReversed => End < Start;

	/// <summary>Determines whether the range contains the supplied position.</summary>
	/// <param name="position">The position to test.</param>
	/// <returns><see langword="true"/> when the position lies within the range.</returns>
	/// <remarks>
	/// Positions are compared by line and then by character. The range is half-open (start inclusive, end exclusive);
	/// because no ordering is enforced, an empty or reversed range contains no position.
	/// </remarks>
	public bool Contains(TextPosition position) => Start <= position && position < End;
}

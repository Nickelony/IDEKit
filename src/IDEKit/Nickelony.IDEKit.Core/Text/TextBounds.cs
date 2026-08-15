namespace Nickelony.IDEKit.Core.Text;

/// <summary>
/// Owns the bounds arithmetic that the range-validation sites share, so "does this range fit in a
/// text of this length" is expressed in one place.
/// </summary>
internal static class TextBounds
{
	/// <summary>
	/// Determines whether the zero-based offset addresses a position within a text of the supplied
	/// length. The end position (<paramref name="textLength"/> itself) is included.
	/// </summary>
	internal static bool ContainsOffset(int textLength, int offset)
		=> (uint)offset <= (uint)textLength;

	/// <summary>
	/// Determines whether the range fits within a text of the supplied length: its offset is inside
	/// the text (or at its end), and its length does not extend past the end.
	/// </summary>
	internal static bool Fits(int textLength, int offset, int length)
		=> ContainsOffset(textLength, offset) && (uint)length <= (uint)(textLength - offset);
}

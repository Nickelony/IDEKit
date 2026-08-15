namespace Nickelony.IDEKit.Core.Text;

/// <summary>
/// Represents a zero-based line and character position in text.
/// </summary>
/// <remarks>
/// <para>
/// This is a plain coordinate pair: no bounds check is performed, and conversions on
/// <see cref="TextLineMap"/> clamp values that fall outside the document. The line and character are
/// zero-based indexes; a line index counts line breaks, and a character index counts UTF-16 code
/// units within its line. A tab counts as one code unit and is not expanded to a tab stop. This is
/// the normative statement of the unit model shared by <see cref="TextPositionRange"/> and the
/// line/character payloads that reference it. One-based line numbers are exposed as
/// <see cref="ITextLine.LineNumber"/>.
/// </para>
/// <para>
/// The type is deliberately lenient where <see cref="TextRange"/> validates. An offset range is the
/// canonical, always-well-formed address of this assembly, so <see cref="TextRange"/> rejects a
/// negative offset or length up front; a line/character position instead arrives from an untrusted
/// protocol boundary and may carry negative or out-of-document coordinates, so validation is deferred
/// to the point where the position becomes an offset: <see cref="ClampNegativeToZero"/> zeroes negative
/// coordinates, and the conversions on <see cref="TextLineMap"/> clamp a position outside the document.
/// </para>
/// </remarks>
/// <param name="Line">The zero-based line index.</param>
/// <param name="Character">The zero-based character index within the line.</param>
public readonly record struct TextPosition(int Line, int Character)
{
	/// <summary>
	/// Gets this position with a negative line or character value changed to zero.
	/// </summary>
	/// <remarks>
	/// Coordinates supplied by a caller (for example a request payload) can carry negative values
	/// when they were not validated; the clamped pair is the position every consumer accepts.
	/// Non-negative values are returned unchanged.
	/// </remarks>
	/// <returns>The position with negative coordinates clamped to zero.</returns>
	public TextPosition ClampNegativeToZero()
		=> new(Math.Max(0, Line), Math.Max(0, Character));
}

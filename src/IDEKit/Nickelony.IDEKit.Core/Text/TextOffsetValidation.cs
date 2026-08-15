namespace Nickelony.IDEKit.Core.Text;

/// <summary>
/// Validates offsets and ranges against a document text, so the shared "does this coordinate fit the
/// document" rule and its exception messages live in one place.
/// </summary>
/// <remarks>
/// <para>
/// The helpers are the throwing counterpart of the boolean checks on
/// <see cref="Nickelony.IDEKit.Core.Text.TextRange"/> and <see cref="TextPosition"/>: a record that
/// validates a coordinate when it is constructed rejects an out-of-range value at the offending
/// call instead of letting it surface later in a provider or a controller.
/// </para>
/// <para>
/// The end-of-text position (<c>textLength</c> itself) is valid for an offset and for a range's end,
/// matching <see cref="ITextSnapshot.GetText(int, int)"/> and the text primitives generally.
/// </para>
/// </remarks>
public static class TextOffsetValidation
{
	/// <summary>
	/// Validates that the offset addresses a position within the supplied document text.
	/// </summary>
	/// <param name="documentText">The document text the offset refers to.</param>
	/// <param name="offset">The zero-based offset to validate; the text length itself is a valid position.</param>
	/// <param name="parameterName">The parameter name reported by the exception.</param>
	/// <param name="offsetDescription">
	/// A short description used in the exception message (for example <c>caret</c> or <c>hovered</c>).
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="documentText"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="offset"/> is negative or greater than the document text length.
	/// </exception>
	public static void ValidateOffset(string documentText, int offset, string parameterName, string offsetDescription)
	{
		ArgumentNullException.ThrowIfNull(documentText);

		ValidateOffset(documentText.Length, offset, parameterName, offsetDescription);
	}

	/// <summary>
	/// Validates that the offset addresses a position within a document of the supplied length. Use
	/// this overload when the document text is not materialized, so the rule and its message stay in
	/// one place.
	/// </summary>
	/// <param name="textLength">The length of the document text the offset refers to.</param>
	/// <param name="offset">The zero-based offset to validate; the text length itself is a valid position.</param>
	/// <param name="parameterName">The parameter name reported by the exception.</param>
	/// <param name="offsetDescription">
	/// A short description used in the exception message (for example <c>caret</c> or <c>hovered</c>).
	/// </param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="offset"/> is negative or greater than the document text length.
	/// </exception>
	public static void ValidateOffset(int textLength, int offset, string parameterName, string offsetDescription)
	{
		if (offset < 0 || offset > textLength)
		{
			throw new ArgumentOutOfRangeException(
				parameterName,
				offset,
				$"The {offsetDescription} offset ({offset}) is outside the document text (length {textLength}).");
		}
	}

	/// <summary>
	/// Validates that a range lies within the document text.
	/// </summary>
	/// <param name="textLength">The length of the document text the range refers to.</param>
	/// <param name="range">The range to validate.</param>
	/// <param name="parameterName">The parameter name reported by the exception.</param>
	/// <param name="rangeDescription">
	/// A short description used in the exception message (for example <c>word span</c>).
	/// </param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The range extends beyond the end of the document text.
	/// </exception>
	public static void ValidateRange(int textLength, TextRange range, string parameterName, string rangeDescription)
	{
		if (range.Offset > textLength || range.EndOffset > textLength)
		{
			throw new ArgumentOutOfRangeException(
				parameterName,
				range,
				$"The {rangeDescription} range ({range.Offset}..{range.EndOffset}) is outside the document text (length {textLength}).");
		}
	}

	/// <summary>
	/// Validates that an ordered range's start offset is not after its end offset.
	/// </summary>
	/// <param name="startOffset">The zero-based start offset of the range.</param>
	/// <param name="endOffset">The zero-based end offset of the range.</param>
	/// <param name="startOffsetParameterName">The start-offset parameter name reported by the exception.</param>
	/// <param name="rangeDescription">
	/// A short description of the range used in the exception message (for example <c>selection</c>).
	/// </param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="startOffset"/> is greater than <paramref name="endOffset"/>.
	/// </exception>
	public static void ValidateOrderedRange(int startOffset, int endOffset, string startOffsetParameterName, string rangeDescription)
	{
		if (startOffset > endOffset)
		{
			throw new ArgumentOutOfRangeException(
				startOffsetParameterName,
				startOffset,
				$"The {rangeDescription} start offset must not be after its end offset.");
		}
	}
}

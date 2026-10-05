namespace Nickelony.IDEKit.Core.Text;

/// <summary>
/// Validates offsets and ranges against a document text, so the shared "does this coordinate fit the document" rule and its exception messages live
/// in one place.
/// </summary>
/// <remarks>
/// The end-of-text position, <c>textLength</c> itself, is valid for an offset and for a range's end,
/// matching <see cref="ITextSnapshot.GetText(int, int)"/>.
/// </remarks>
public static class TextOffsetValidation
{
	/// <summary>Validates that the offset addresses a position within the supplied document text.</summary>
	/// <param name="documentText">The document text the offset refers to.</param>
	/// <param name="offset">The zero-based offset to validate; the text length itself is a valid position.</param>
	/// <param name="parameterName">The parameter name reported by the exception.</param>
	/// <param name="offsetDescription">A short description used in the exception message (for example <c>caret</c> or <c>hovered</c>).</param>
	/// <exception cref="ArgumentNullException"><paramref name="documentText"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="offset"/> is negative or greater than the document text length.</exception>
	public static void ValidateOffset(string documentText, int offset, string parameterName, string offsetDescription)
	{
		ArgumentNullException.ThrowIfNull(documentText);
		ValidateOffset(textLength: documentText.Length, offset, parameterName, offsetDescription);
	}

	/// <summary>Validates that the offset addresses a position within a document of the supplied length.</summary>
	/// <param name="textLength">The length of the document text the offset refers to.</param>
	/// <param name="offset">The zero-based offset to validate; the text length itself is a valid position.</param>
	/// <param name="parameterName">The parameter name reported by the exception.</param>
	/// <param name="offsetDescription">A short description used in the exception message (for example <c>caret</c> or <c>hovered</c>).</param>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="offset"/> is negative or greater than the document text length.</exception>
	/// <remarks>
	/// Use this overload when the document text is not materialized.
	/// </remarks>
	public static void ValidateOffset(int textLength, int offset, string parameterName, string offsetDescription)
	{
		if (!TextBounds.ContainsOffset(textLength, offset))
		{
			throw new ArgumentOutOfRangeException(
				parameterName,
				offset,
				$"The {offsetDescription} offset ({offset}) is outside the document text (length {textLength})."
			);
		}
	}

	/// <summary>Validates that a range lies within the document text.</summary>
	/// <param name="textLength">The length of the document text the range refers to.</param>
	/// <param name="range">The range to validate.</param>
	/// <param name="parameterName">The parameter name reported by the exception.</param>
	/// <param name="rangeDescription">A short description used in the exception message (for example <c>word span</c>).</param>
	/// <exception cref="ArgumentOutOfRangeException">The range extends beyond the end of the document text.</exception>
	public static void ValidateRange(int textLength, TextRange range, string parameterName, string rangeDescription)
	{
		if (!range.FitsWithin(textLength))
		{
			throw new ArgumentOutOfRangeException(
				parameterName,
				range,
				$"The {rangeDescription} range ({range.Offset}..{range.EndOffset}) is outside the document text (length {textLength})."
			);
		}
	}
}

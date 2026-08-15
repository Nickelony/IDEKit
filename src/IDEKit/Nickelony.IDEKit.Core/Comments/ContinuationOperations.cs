namespace Nickelony.IDEKit.Core.Comments;

/// <summary>
/// Provides continuation-marker utilities for languages that use a single-character
/// or multi-character continuation marker at the end of a line (for example <c>_</c> in
/// Visual Basic or <c>...</c> in MATLAB).
/// </summary>
/// <remarks>
/// Marker detection is a comment-aware operation: it strips the trailing comment (using
/// <see cref="CommentOperations.GetCodeEnd"/>) before testing the marker, so a marker inside a
/// comment never counts and a marker followed by a comment still does.
/// </remarks>
public static class ContinuationOperations
{
	/// <summary>
	/// Determines whether the code portion of the line ends with a single-character continuation
	/// marker, ignoring trailing comments and whitespace.
	/// </summary>
	/// <inheritdoc cref="EndsWithContinuationMarker(ReadOnlySpan{char}, CommentSyntax, ReadOnlySpan{char}, bool)"/>
	/// <remarks>
	/// The single-character shortcut always uses the default marker rule, which only checks that the code
	/// portion ends with the marker. Call the sequence overload when the marker must be preceded by
	/// whitespace, as Visual Basic requires of <c>_</c>.
	/// </remarks>
	public static bool EndsWithContinuationMarker(
		ReadOnlySpan<char> text,
		CommentSyntax syntax,
		char continuationMarker)
	{
		return EndsWithContinuationMarker(text, syntax, new ReadOnlySpan<char>(in continuationMarker));
	}

	/// <summary>
	/// Determines whether the code portion of the line ends with a continuation marker, which may be
	/// a single character or a multi-character sequence, ignoring trailing comments and whitespace.
	/// </summary>
	/// <param name="text">The line text, potentially including a trailing comment.</param>
	/// <param name="syntax">The comment syntax of the language.</param>
	/// <param name="continuationMarker">
	/// The continuation marker character or sequence, for example <c>'_'</c> for Visual Basic or
	/// <c>"..."</c> for MATLAB.
	/// </param>
	/// <param name="markerMustBePrecededByWhitespace">
	/// Whether the marker must be preceded by whitespace to count as a continuation marker. The
	/// default is <see langword="false"/>, which checks only that the code portion ends with the
	/// marker. Visual Basic needs <see langword="true"/>, because an identifier may itself end with
	/// the marker character.
	/// </param>
	/// <returns>
	/// <see langword="true"/> when the code portion of the line ends with the continuation marker
	/// (ignoring trailing whitespace and comments); otherwise, <see langword="false"/>.
	/// </returns>
	public static bool EndsWithContinuationMarker(
		ReadOnlySpan<char> text,
		CommentSyntax syntax,
		ReadOnlySpan<char> continuationMarker,
		bool markerMustBePrecededByWhitespace = false)
	{
		int codeEnd = CommentOperations.GetCodeEnd(text, syntax);

		if (continuationMarker.Length == 0
			|| !text[..codeEnd].EndsWith(continuationMarker, StringComparison.Ordinal))
		{
			return false;
		}

		if (!markerMustBePrecededByWhitespace)
			return true;

		int markerStart = codeEnd - continuationMarker.Length;

		return markerStart > 0 && char.IsWhiteSpace(text[markerStart - 1]);
	}
}

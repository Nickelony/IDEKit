using System.Text;
using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Scanning;

/// <summary>Finds, removes, and masks comments in text for languages with line comments, block comments, or both.</summary>
/// <remarks>
/// Comment spans follow the rules on <see cref="CommentSpan"/>; removal drops the whole span, and masking replaces every character of the span
/// with a space except a line-terminator character, which stays, so the length and the line structure are kept. Removing or masking only block
/// comments still scans line comments, so a block opener written inside a line comment starts no block comment. The query methods do not allocate,
/// and a string-overload transform returns its input instance when no relevant comment exists (a span overload returns a new string); an empty input
/// yields <see cref="string.Empty"/>.
/// </remarks>
public static class CommentOperations
{
	/// <summary>Returns the first comment (line or block) in the text.</summary>
	/// <param name="text">The text to search. May contain multiple lines.</param>
	/// <param name="syntax">The comment syntax of the language.</param>
	/// <returns>The first comment span, or <see langword="null"/> if no comment is found.</returns>
	public static CommentSpan? FindComment(ReadOnlySpan<char> text, CommentSyntax syntax)
	{
		var enumerator = new CommentSpanEnumerator(text, syntax);
		return enumerator.MoveNext() ? enumerator.Current : null;
	}

	/// <summary>Enumerates all comments in the text in a single forward pass.</summary>
	/// <param name="text">The text to scan. May contain multiple lines.</param>
	/// <param name="syntax">The comment syntax of the language.</param>
	/// <returns>An enumerator over each comment span in the text.</returns>
	public static CommentSpanEnumerator EnumerateComments(ReadOnlySpan<char> text, CommentSyntax syntax) => new(text, syntax);

	/// <summary>Returns the code range before the first comment.</summary>
	/// <param name="text">The text to evaluate. May contain multiple lines.</param>
	/// <param name="syntax">The comment syntax of the language.</param>
	/// <remarks>
	/// The range ends before the first comment and never extends past it, so code after a later comment is outside the range. For a line comment the
	/// comment's absorbed preceding whitespace and terminator are excluded (see <see cref="CommentSpan"/>); for a block comment the range ends at
	/// the opener. With no comment the range covers the entire text.
	/// </remarks>
	/// <example>
	/// <code>
	/// var syntax = new CommentSyntax(";", null, StringLiteralStyles.None);
	/// TextRange code = CommentOperations.GetCodeRange("Legend= 42 ; comment", syntax);
	/// // code is [0..10): "Legend= 42"
	///
	/// var blockSyntax = new CommentSyntax(null, new BlockCommentSyntax("/*", "*/"), StringLiteralStyles.None);
	/// TextRange block = CommentOperations.GetCodeRange("code /* comment */", blockSyntax);
	/// // block is [0..5): "code " including the trailing space
	///
	/// TextRange commentOnlyLine = CommentOperations.GetCodeRange("code\r\n; comment", syntax);
	/// // commentOnlyLine is [0..4): the comment-only line absorbed its preceding CRLF
	/// </code>
	/// </example>
	public static TextRange GetCodeRange(ReadOnlySpan<char> text, CommentSyntax syntax)
	{
		if (FindComment(text, syntax) is { } comment)
			return new(0, comment.Range.Offset);

		return new(0, text.Length);
	}

	/// <summary>Finds the end of the code portion of the text, ignoring trailing whitespace.</summary>
	/// <param name="text">The text to evaluate. Typically a single line.</param>
	/// <param name="syntax">The comment syntax of the language.</param>
	/// <returns>The zero-based offset one past the last non-whitespace code character, or <c>0</c> when the text contains no code.</returns>
	/// <remarks>
	/// The result is the offset one past the last non-whitespace code character. Comment text and string content are skipped, while the closing
	/// quote of a double-quoted, single-quoted, backtick, or verbatim string counts as code, so a string that ends the line keeps its end visible
	/// to continuation-marker detection (<see cref="ContinuationMarkerProbe"/>). Raw (triple-quoted) and long-bracket strings are skipped in full,
	/// including their closing delimiters.
	/// </remarks>
	public static int GetCodeEnd(ReadOnlySpan<char> text, CommentSyntax syntax)
	{
		var scanner = new CommentScanner(text, syntax);
		int codeEnd = 0;

		while (scanner.MoveNext())
		{
			if (scanner.IsInCode && !char.IsWhiteSpace(text[scanner.CurrentIndex]))
				codeEnd = scanner.CurrentIndex + 1;
		}

		return codeEnd;
	}

	/// <summary>Whether the supplied line is blank or, after its leading indentation, starts with the line-comment delimiter of the syntax.</summary>
	/// <param name="lineText">The line text to inspect, or <see langword="null"/>, which is treated as blank.</param>
	/// <param name="syntax">The comment syntax of the language.</param>
	/// <remarks>
	/// Block comments are not recognized, so a line that holds
	/// only a block comment is neither blank nor a line comment. The blank test and the leading scan use the same space/tab indentation rule
	/// the transforms apply (<see cref="LineTextOperations.GetLeadingWhitespaceLength(string)"/>), so a line whose only whitespace is a non-space,
	/// non-tab character is neither blank nor a comment, and every line counted as commented can be uncommented.
	/// </remarks>
	public static bool IsBlankOrStartsWithLineComment(string? lineText, CommentSyntax syntax)
	{
		if (lineText is null)
			return true;

		// The indentation scan is the space/tab rule of the transforms; a line that is nothing but
		// indentation is blank, so both tests share one alphabet.
		int contentStart = LineTextOperations.GetLeadingWhitespaceLength(lineText);

		if (contentStart == lineText.Length)
			return true;

		string? delimiter = syntax.LineCommentDelimiter;

		if (delimiter is null)
			return false;

		return lineText.AsSpan(contentStart).StartsWith(delimiter, StringComparison.Ordinal);
	}

	/// <summary>Removes all comments from the text.</summary>
	/// <param name="text">The text to process. May contain multiple lines.</param>
	/// <param name="syntax">The comment syntax of the language.</param>
	public static string RemoveComments(ReadOnlySpan<char> text, CommentSyntax syntax) =>
		TransformComments(text, syntax, CommentTransformMode.RemoveAll);

	/// <summary>Removes all comments from the string.</summary>
	/// <inheritdoc cref="RemoveComments(ReadOnlySpan{char}, CommentSyntax)"/>
	/// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
	public static string RemoveComments(string text, CommentSyntax syntax)
	{
		ArgumentNullException.ThrowIfNull(text);
		return TransformComments(text: text.AsSpan(), syntax, mode: CommentTransformMode.RemoveAll, original: text);
	}

	/// <summary>Masks all comments with spaces, preserving the total string length.</summary>
	/// <param name="text">The text to process. May contain multiple lines.</param>
	/// <param name="syntax">The comment syntax of the language.</param>
	/// <remarks>
	/// Uses the masking rule described in the class remarks; a line comment's own line terminator stays outside its span and is preserved.
	/// </remarks>
	public static string MaskComments(ReadOnlySpan<char> text, CommentSyntax syntax) => TransformComments(text, syntax, CommentTransformMode.MaskAll);

	/// <summary>Masks all comments in the string with spaces, preserving the total string length.</summary>
	/// <inheritdoc cref="MaskComments(ReadOnlySpan{char}, CommentSyntax)"/>
	/// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
	public static string MaskComments(string text, CommentSyntax syntax)
	{
		ArgumentNullException.ThrowIfNull(text);
		return TransformComments(text: text.AsSpan(), syntax, mode: CommentTransformMode.MaskAll, original: text);
	}

	/// <summary>Removes all block comments from the text, leaving line comments untouched.</summary>
	/// <param name="text">The text to process. May contain multiple lines.</param>
	/// <param name="syntax">The comment syntax of the language.</param>
	public static string RemoveBlockComments(ReadOnlySpan<char> text, CommentSyntax syntax) =>
		TransformComments(text, syntax, CommentTransformMode.RemoveBlock);

	/// <summary>Removes all block comments from the string, leaving line comments untouched.</summary>
	/// <inheritdoc cref="RemoveBlockComments(ReadOnlySpan{char}, CommentSyntax)"/>
	/// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
	public static string RemoveBlockComments(string text, CommentSyntax syntax)
	{
		ArgumentNullException.ThrowIfNull(text);
		return TransformComments(text: text.AsSpan(), syntax, mode: CommentTransformMode.RemoveBlock, original: text);
	}

	/// <summary>Masks all block comments in the text with spaces, leaving line comments untouched.</summary>
	/// <param name="text">The text to process. May contain multiple lines.</param>
	/// <param name="syntax">The comment syntax of the language.</param>
	/// <remarks>
	/// Uses the masking rule described in the class remarks.
	/// </remarks>
	public static string MaskBlockComments(ReadOnlySpan<char> text, CommentSyntax syntax) =>
		TransformComments(text, syntax, CommentTransformMode.MaskBlock);

	/// <summary>Masks all block comments in the string with spaces, leaving line comments untouched.</summary>
	/// <inheritdoc cref="MaskBlockComments(ReadOnlySpan{char}, CommentSyntax)"/>
	/// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
	public static string MaskBlockComments(string text, CommentSyntax syntax)
	{
		ArgumentNullException.ThrowIfNull(text);
		return TransformComments(text: text.AsSpan(), syntax, mode: CommentTransformMode.MaskBlock, original: text);
	}

	/// <summary>Transforms the text by removing or masking comment spans according to <paramref name="mode"/>.</summary>
	/// <remarks>
	/// Line comments participate in scanning in every mode, so a block opener inside a line comment is not recognized; the block-only modes leave
	/// line comments untouched. When no relevant comment exists, <paramref name="original"/> is returned unchanged rather than rebuilt.
	/// </remarks>
	private static string TransformComments(ReadOnlySpan<char> text, CommentSyntax syntax, CommentTransformMode mode, string? original = null)
	{
		if (text.Length == 0)
			return string.Empty;

		bool maskComments = mode is CommentTransformMode.MaskAll or CommentTransformMode.MaskBlock;
		bool includeLineComments = mode is CommentTransformMode.RemoveAll or CommentTransformMode.MaskAll;

		StringBuilder? result = null;

		int sourceOffset = 0; // The first character that has not been copied or transformed.

		var enumerator = new CommentSpanEnumerator(text, syntax);

		while (enumerator.MoveNext())
		{
			CommentSpan span = enumerator.Current;

			if (!includeLineComments && span.IsLineComment)
				continue;

			result ??= new StringBuilder(text.Length);

			// Copy text before the comment; the comment itself is omitted or masked below.
			result.Append(text[sourceOffset..span.Range.Offset]);

			if (maskComments)
				AppendMasked(result, text.Slice(span.Range.Offset, span.Range.Length));

			sourceOffset = span.Range.EndOffset;
		}

		if (result is null)
			return original ?? text.ToString();

		result.Append(text[sourceOffset..]);
		return result.ToString();
	}

	/// <summary>Appends the masked form of a comment span using the class-level masking rule.</summary>
	private static void AppendMasked(StringBuilder builder, ReadOnlySpan<char> span)
	{
		for (int i = 0; i < span.Length; i++)
			builder.Append(LineTerminators.IsTerminator(span[i]) ? span[i] : ' ');
	}

	/// <summary>Selects which comment kinds a transform acts on and whether it removes or masks them.</summary>
	private enum CommentTransformMode
	{
		/// <summary>Removes line and block comments.</summary>
		RemoveAll,

		/// <summary>Masks line and block comments with spaces.</summary>
		MaskAll,

		/// <summary>Removes block comments and leaves line comments untouched.</summary>
		RemoveBlock,

		/// <summary>Masks block comments with spaces and leaves line comments untouched.</summary>
		MaskBlock,
	}
}

using Nickelony.IDEKit.Core.Text;
using System.Text;

namespace Nickelony.IDEKit.Core.Comments;

/// <summary>
/// Finds, removes, and masks comments in text for languages with line comments, block comments, or both.
/// </summary>
/// <remarks>
/// <para>
/// Comment spans follow the rules documented on <see cref="CommentSpan"/>: a line comment includes
/// the whitespace immediately before the delimiter and, when the line holds only that comment, the
/// single line terminator that ends the preceding line; a block comment runs from the opener through
/// the closer, or to the end of the text when unclosed, and leaves surrounding whitespace untouched.
/// </para>
/// <para>
/// Removal drops the whole span, including an absorbed line terminator when the span absorbed one.
/// A comment-only first line has no preceding terminator to absorb, so removing its comment leaves an
/// empty first line.
/// </para>
/// <para>
/// Masking keeps the string length and the line structure: CR and LF characters inside a span are
/// preserved and every other character becomes a space, so a masked comment-only line stays a blank
/// line of its own instead of merging with the line above it. Masking a comment-only first line
/// leaves a blank first line.
/// </para>
/// <para>
/// Removing or masking only block comments still scans line comments, so a block opener written
/// inside a line comment does not start a block comment.
/// </para>
/// <para>
/// The query methods (<see cref="FindComment"/>, <see cref="EnumerateComments"/>,
/// <see cref="GetCodeRange"/>, <see cref="GetCodeEnd"/>) do not allocate. The transform methods that
/// produce text also have <see cref="string"/> overloads, which return the original instance when no
/// relevant comment is found (an empty input returns <see cref="string.Empty"/>). For the block-only
/// transforms, "no relevant comment" includes text whose only comments are line comments.
/// </para>
/// </remarks>
public static class CommentOperations
{
	/// <summary>
	/// Returns the first comment (line or block) in the text.
	/// </summary>
	/// <param name="text">The text to search. May contain multiple lines.</param>
	/// <param name="syntax">The comment syntax of the language.</param>
	/// <returns>The first comment span, or <see langword="null"/> if no comment is found.</returns>
	public static CommentSpan? FindComment(ReadOnlySpan<char> text, CommentSyntax syntax)
	{
		var enumerator = new CommentSpanEnumerator(text, syntax);
		return enumerator.MoveNext() ? enumerator.Current : null;
	}

	/// <summary>
	/// Enumerates all comments in the text in a single forward pass.
	/// </summary>
	/// <param name="text">The text to scan. May contain multiple lines.</param>
	/// <param name="syntax">The comment syntax of the language.</param>
	/// <returns>An enumerator over each comment span in the text.</returns>
	public static CommentSpanEnumerator EnumerateComments(ReadOnlySpan<char> text, CommentSyntax syntax)
		=> new(text, syntax);

	/// <summary>
	/// Gets the leading code range before the first comment.
	/// </summary>
	/// <remarks>
	/// For a line comment, whitespace immediately before the delimiter belongs to the comment span
	/// and is therefore excluded, as is the single line terminator that a comment-only line absorbs
	/// (see <see cref="CommentSpan"/>). For a block comment, the range ends at the opener. The range
	/// never extends past the first comment, so code that follows it is outside the range even when
	/// further comments exist. When no comment is found, the range covers the entire text.
	/// </remarks>
	/// <param name="text">The text to evaluate. May contain multiple lines.</param>
	/// <param name="syntax">The comment syntax of the language.</param>
	/// <returns>A <see cref="TextRange"/> covering the code portion of the text before the first comment.</returns>
	/// <example>
	/// <code>
	/// var syntax = new CommentSyntax(";", null, StringLiteralStyle.None);
	/// TextRange code = CommentOperations.GetCodeRange("Legend= 42 ; comment", syntax);
	/// // code is [0..10): "Legend= 42"
	///
	/// var blockSyntax = new CommentSyntax(null, new BlockCommentSyntax("/*", "*/"), StringLiteralStyle.None);
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
			return new(0, comment.StartOffset);

		return new(0, text.Length);
	}

	/// <summary>
	/// Finds the end of the code portion of the text, ignoring trailing whitespace.
	/// </summary>
	/// <remarks>
	/// The result is the offset one past the last non-whitespace code character. Comment text and
	/// string content are skipped, while the closing quote of a double-quoted, single-quoted,
	/// backtick, or verbatim string counts as code, so a string that ends the line keeps its end
	/// visible to continuation-marker detection (<see cref="ContinuationOperations"/>). Raw
	/// (triple-quoted) and long-bracket strings are skipped in full, including their closing
	/// delimiters.
	/// </remarks>
	/// <param name="text">The text to evaluate. Typically a single line.</param>
	/// <param name="syntax">The comment syntax of the language.</param>
	/// <returns>
	/// The zero-based offset one past the last non-whitespace code character, or <c>0</c> when the
	/// text contains no code.
	/// </returns>
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

	/// <summary>
	/// Gets a value indicating whether the supplied line is blank or, after its leading indentation,
	/// starts with the line-comment delimiter of the syntax.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Block comments are not recognized; a line that holds only a block comment is neither blank nor
	/// a line comment, so it is not reported.
	/// </para>
	/// <para>
	/// Both the blank test and the leading scan use the space/tab indentation rule of
	/// <see cref="Indentation.IndentationOperations.GetLeadingWhitespaceLength(string)"/>, which is
	/// the same rule the line-comment transforms apply (see <see cref="TextLineCommentPlanner"/>).
	/// Every line this predicate counts as commented is therefore a line the uncomment transform can
	/// change, and a line whose only whitespace is a non-space, non-tab character such as a
	/// non-breaking space is neither blank nor a comment here.
	/// </para>
	/// </remarks>
	/// <param name="lineText">
	/// The line text to inspect, or <see langword="null"/>, which is treated as blank.
	/// </param>
	/// <param name="syntax">The comment syntax of the language.</param>
	/// <returns>
	/// <see langword="true"/> when the line is blank or its first non-indentation content is a
	/// line-comment delimiter; otherwise, <see langword="false"/>.
	/// </returns>
	public static bool IsBlankOrStartsWithLineComment(string? lineText, CommentSyntax syntax)
	{
		if (lineText is null)
			return true;

		// The indentation scan is the space/tab rule of the transforms; a line that is nothing but
		// indentation is blank, so both tests share one alphabet.
		int contentStart = Indentation.IndentationOperations.GetLeadingWhitespaceLength(lineText);

		if (contentStart == lineText.Length)
			return true;

		string? delimiter = syntax.LineCommentDelimiter;

		if (delimiter is null)
			return false;

		return lineText.AsSpan(contentStart).StartsWith(delimiter, StringComparison.Ordinal);
	}

	/// <summary>
	/// Removes all comments from the text.
	/// </summary>
	/// <param name="text">The text to process. May contain multiple lines.</param>
	/// <param name="syntax">The comment syntax of the language.</param>
	/// <returns>The text with all comments removed.</returns>
	public static string RemoveComments(ReadOnlySpan<char> text, CommentSyntax syntax)
		=> TransformComments(text, syntax, CommentTransformMode.RemoveAll);

	/// <summary>
	/// Removes all comments from the string.
	/// </summary>
	/// <inheritdoc cref="RemoveComments(ReadOnlySpan{char}, CommentSyntax)"/>
	/// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
	public static string RemoveComments(string text, CommentSyntax syntax)
	{
		ArgumentNullException.ThrowIfNull(text);
		return TransformComments(text.AsSpan(), syntax, CommentTransformMode.RemoveAll, original: text);
	}

	/// <summary>
	/// Masks all comments with spaces, preserving the total string length.
	/// </summary>
	/// <remarks>
	/// Uses the masking rule described in the class remarks. A line comment's own line terminator
	/// stays outside its span, so it is preserved as well.
	/// </remarks>
	/// <param name="text">The text to process. May contain multiple lines.</param>
	/// <param name="syntax">The comment syntax of the language.</param>
	/// <returns>The text with comment spans replaced by spaces.</returns>
	public static string MaskComments(ReadOnlySpan<char> text, CommentSyntax syntax)
		=> TransformComments(text, syntax, CommentTransformMode.MaskAll);

	/// <summary>
	/// Masks all comments in the string with spaces, preserving the total string length.
	/// </summary>
	/// <inheritdoc cref="MaskComments(ReadOnlySpan{char}, CommentSyntax)"/>
	/// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
	public static string MaskComments(string text, CommentSyntax syntax)
	{
		ArgumentNullException.ThrowIfNull(text);
		return TransformComments(text.AsSpan(), syntax, CommentTransformMode.MaskAll, original: text);
	}

	/// <summary>
	/// Removes all block comments from the text, leaving line comments untouched.
	/// </summary>
	/// <param name="text">The text to process. May contain multiple lines.</param>
	/// <param name="syntax">The comment syntax of the language.</param>
	/// <returns>The text with all block comments removed.</returns>
	public static string RemoveBlockComments(ReadOnlySpan<char> text, CommentSyntax syntax)
		=> TransformComments(text, syntax, CommentTransformMode.RemoveBlock);

	/// <summary>
	/// Removes all block comments from the string, leaving line comments untouched.
	/// </summary>
	/// <inheritdoc cref="RemoveBlockComments(ReadOnlySpan{char}, CommentSyntax)"/>
	/// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
	public static string RemoveBlockComments(string text, CommentSyntax syntax)
	{
		ArgumentNullException.ThrowIfNull(text);
		return TransformComments(text.AsSpan(), syntax, CommentTransformMode.RemoveBlock, original: text);
	}

	/// <summary>
	/// Masks all block comments in the text with spaces, leaving line comments untouched.
	/// </summary>
	/// <remarks>
	/// Uses the masking rule described in the class remarks.
	/// </remarks>
	/// <param name="text">The text to process. May contain multiple lines.</param>
	/// <param name="syntax">The comment syntax of the language.</param>
	/// <returns>The text with all block comments replaced by spaces.</returns>
	public static string MaskBlockComments(ReadOnlySpan<char> text, CommentSyntax syntax)
		=> TransformComments(text, syntax, CommentTransformMode.MaskBlock);

	/// <summary>
	/// Masks all block comments in the string with spaces, leaving line comments untouched.
	/// </summary>
	/// <inheritdoc cref="MaskBlockComments(ReadOnlySpan{char}, CommentSyntax)"/>
	/// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
	public static string MaskBlockComments(string text, CommentSyntax syntax)
	{
		ArgumentNullException.ThrowIfNull(text);
		return TransformComments(text.AsSpan(), syntax, CommentTransformMode.MaskBlock, original: text);
	}

	/// <summary>
	/// Transforms the text by removing or masking comment spans according to
	/// <paramref name="mode"/>.
	/// </summary>
	/// <remarks>
	/// Line comments participate in scanning in every mode, so a block opener inside a line comment
	/// is not recognized; the block-only modes leave line comments untouched. When no relevant
	/// comment exists, <paramref name="original"/> is returned unchanged rather than rebuilt.
	/// </remarks>
	private static string TransformComments(
		ReadOnlySpan<char> text,
		CommentSyntax syntax,
		CommentTransformMode mode,
		string? original = null)
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
			result.Append(text.Slice(sourceOffset, span.StartOffset - sourceOffset));

			if (maskComments)
				AppendMasked(result, text.Slice(span.StartOffset, span.Length));

			sourceOffset = span.EndOffset;
		}

		if (result is null)
			return original ?? text.ToString();

		result.Append(text[sourceOffset..]);
		return result.ToString();
	}

	/// <summary>
	/// Appends the masked form of a comment span using the class-level masking rule.
	/// </summary>
	private static void AppendMasked(StringBuilder builder, ReadOnlySpan<char> span)
	{
		for (int i = 0; i < span.Length; i++)
			builder.Append(LineTerminators.IsTerminator(span[i]) ? span[i] : ' ');
	}

	/// <summary>
	/// Selects which comment kinds a transform acts on and whether it removes or masks them.
	/// </summary>
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

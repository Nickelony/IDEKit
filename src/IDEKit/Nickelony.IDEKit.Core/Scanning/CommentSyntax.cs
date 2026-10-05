namespace Nickelony.IDEKit.Core.Scanning;

/// <summary>
/// Describes the comment syntax of a language: the line-comment delimiter, the optional block-comment delimiters, and which string-literal styles
/// are recognized while scanning.
/// </summary>
/// <remarks>
/// Block-comment delimiters are fixed strings, so parameterized long-bracket forms such as Lua's <c>--[==[</c> cannot be expressed.
/// </remarks>
public readonly record struct CommentSyntax
{
	/// <summary>
	/// The line-comment delimiter, such as <c>"//"</c>, <c>"--"</c>, or <c>";"</c>, or <see langword="null"/> when the language has no line comments
	/// or the delimiter is empty or whitespace-only.
	/// </summary>
	public string? LineCommentDelimiter { get; }

	/// <summary>The block-comment delimiters and nesting rule, or <see langword="null"/> when the language has no block comments.</summary>
	public BlockCommentSyntax? BlockComments { get; }

	/// <summary>
	/// The string-literal styles recognized while scanning, so a delimiter inside string content is not treated as a comment start.
	/// </summary>
	public StringLiteralStyles StringStyle { get; }

	/// <param name="lineCommentDelimiter">The line-comment delimiter, or <see langword="null"/> for none.</param>
	/// <param name="blockComments">The block-comment delimiters, or <see langword="null"/> for none.</param>
	/// <param name="stringStyle">The string-literal styles to recognize while scanning.</param>
	/// <exception cref="ArgumentException">
	/// <paramref name="blockComments"/> is an uninitialized pair whose delimiters are <see langword="null"/> (for example the result
	/// of <c>default(BlockCommentSyntax)</c>).
	/// </exception>
	/// <remarks>
	/// A blank or <see langword="null"/> line delimiter disables line-comment awareness. A non-blank delimiter
	/// is used exactly as supplied, so its surrounding whitespace is significant (for example <c>"REM "</c>). Block comments are enabled exactly
	/// when <paramref name="blockComments"/> carries a pair, which validates that both delimiters are present and non-blank.
	/// </remarks>
	public CommentSyntax(string? lineCommentDelimiter, BlockCommentSyntax? blockComments, StringLiteralStyles stringStyle)
	{
		if (blockComments is { Open: null } or { Close: null })
		{
			throw new ArgumentException(
				"The block-comment syntax must carry an opener and a closer; pass null for a language without block comments.",
				nameof(blockComments)
			);
		}

		// An empty or whitespace-only line delimiter disables line-comment awareness, so a stray
		// whitespace delimiter cannot make every whitespace character a comment opener.
		LineCommentDelimiter = string.IsNullOrWhiteSpace(lineCommentDelimiter) ? null : lineCommentDelimiter;
		BlockComments = blockComments;
		StringStyle = stringStyle;
	}
}

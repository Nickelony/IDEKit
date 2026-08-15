namespace Nickelony.IDEKit.Core.Comments;

/// <summary>
/// Describes the comment syntax of a language: the line-comment delimiter, the optional
/// block-comment delimiters, and which string-literal styles are recognized while scanning.
/// </summary>
/// <remarks>
/// <para>
/// Bundle the delimiters into one value and pass it to <see cref="CommentOperations"/> instead of
/// threading each delimiter as a separate parameter.
/// </para>
/// <para>
/// Block-comment delimiters are fixed strings, so parameterized long-bracket forms such as Lua's
/// <c>--[==[</c> cannot be expressed.
/// </para>
/// </remarks>
public readonly record struct CommentSyntax
{
	/// <summary>
	/// Gets the line-comment delimiter, such as <c>"//"</c>, <c>"--"</c>, or <c>";"</c>,
	/// or <see langword="null"/> when the language has no line comments. A delimiter that is empty or
	/// whitespace-only is normalized to <see langword="null"/> at construction.
	/// </summary>
	public string? LineCommentDelimiter { get; }

	/// <summary>
	/// Gets the block-comment delimiters and nesting rule, or <see langword="null"/> when the
	/// language has no block comments.
	/// </summary>
	public BlockCommentSyntax? BlockComments { get; }

	/// <summary>
	/// Gets the string-literal styles recognized while scanning, so a delimiter inside
	/// string content is not treated as a comment start.
	/// </summary>
	public StringLiteralStyle StringStyle { get; }

	/// <summary>
	/// Initializes a new instance of the <see cref="CommentSyntax"/> struct.
	/// </summary>
	/// <remarks>
	/// An empty, whitespace-only, or <see langword="null"/> line delimiter disables line-comment
	/// awareness. A non-blank delimiter is used exactly as supplied: its surrounding whitespace is
	/// significant (for example <c>"REM "</c>), so pass delimiters without stray whitespace. Block
	/// comments are enabled exactly when <paramref name="blockComments"/> carries a pair; the pair
	/// itself validates that both delimiters are present and non-blank.
	/// </remarks>
	/// <param name="lineCommentDelimiter">The line-comment delimiter, or <see langword="null"/> for none.</param>
	/// <param name="blockComments">The block-comment delimiters, or <see langword="null"/> for none.</param>
	/// <param name="stringStyle">The string-literal styles to recognize while scanning.</param>
	/// <exception cref="ArgumentException">
	/// <paramref name="blockComments"/> is an uninitialized pair whose delimiters are
	/// <see langword="null"/> (for example the result of <c>default(BlockCommentSyntax)</c>);
	/// construct a real pair or pass <see langword="null"/> for a language without block comments.
	/// </exception>
	public CommentSyntax(
		string? lineCommentDelimiter,
		BlockCommentSyntax? blockComments,
		StringLiteralStyle stringStyle)
	{
		if (blockComments is { Open: null } or { Close: null })
		{
			throw new ArgumentException(
				"The block-comment syntax must carry an opener and a closer; pass null for a language without block comments.",
				nameof(blockComments));
		}

		// An empty or whitespace-only line delimiter disables line-comment awareness, so a stray
		// whitespace delimiter cannot make every whitespace character a comment opener.
		LineCommentDelimiter = string.IsNullOrWhiteSpace(lineCommentDelimiter) ? null : lineCommentDelimiter;
		BlockComments = blockComments;
		StringStyle = stringStyle;
	}
}

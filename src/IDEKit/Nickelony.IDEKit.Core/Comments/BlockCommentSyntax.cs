namespace Nickelony.IDEKit.Core.Comments;

/// <summary>
/// Describes a language's block-comment delimiters: the opener, the closer, and whether block
/// comments may nest.
/// </summary>
/// <remarks>
/// The opener and closer travel as one value, so an unpaired delimiter cannot be specified: both
/// are required and must be non-blank. A language without block comments passes
/// <see langword="null"/> for the whole pair on <see cref="CommentSyntax"/>. A
/// <see langword="default"/> instance carries <see langword="null"/> delimiters; constructing a
/// <see cref="CommentSyntax"/> from such a pair throws <see cref="ArgumentException"/>.
/// </remarks>
public readonly record struct BlockCommentSyntax
{
	/// <summary>
	/// Gets the block-comment opener, such as <c>"/*"</c> or <c>"--[["</c>.
	/// </summary>
	public string Open { get; }

	/// <summary>
	/// Gets the block-comment closer, such as <c>"*/"</c> or <c>"]]"</c>.
	/// </summary>
	public string Close { get; }

	/// <summary>
	/// Gets a value indicating whether an opener inside a block comment increases the nesting
	/// depth so the comment closes only after the matching final closer.
	/// </summary>
	/// <remarks>
	/// Defaults to <see langword="false"/>, matching languages such as C where the first closer
	/// ends the comment.
	/// </remarks>
	public bool AllowNesting { get; }

	/// <summary>
	/// Initializes a new instance of the <see cref="BlockCommentSyntax"/> struct.
	/// </summary>
	/// <param name="open">The block-comment opener, such as <c>"/*"</c>.</param>
	/// <param name="close">The block-comment closer, such as <c>"*/"</c>.</param>
	/// <param name="allowNesting">Whether block comments may nest.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="open"/> or <paramref name="close"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="open"/> or <paramref name="close"/> is blank. A blank delimiter would make
	/// the pair behave as if the language had no block comments.
	/// </exception>
	public BlockCommentSyntax(string open, string close, bool allowNesting = false)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(open);
		ArgumentException.ThrowIfNullOrWhiteSpace(close);

		Open = open;
		Close = close;
		AllowNesting = allowNesting;
	}
}

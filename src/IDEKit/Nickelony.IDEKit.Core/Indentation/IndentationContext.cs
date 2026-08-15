namespace Nickelony.IDEKit.Core.Indentation;

/// <summary>
/// Describes the inputs required to compute the desired indentation for one line.
/// </summary>
/// <remarks>
/// The context carries line text only, so a policy derives everything it needs from the same
/// primitives as the consumer's indentation strategy (for example
/// <see cref="IndentationOperations.GetLeadingWhitespace(string)"/>). A <see langword="default"/> instance carries
/// <see langword="null"/> text members despite the annotations; hosts construct the context from
/// live line text.
/// </remarks>
/// <param name="PreviousLineText">The full text of the previous line.</param>
/// <param name="CurrentLineText">The full text of the line being indented, including its leading whitespace.</param>
/// <param name="IndentationUnit">The text appended for one additional indent level.</param>
/// <param name="UseSmartIndent">Whether language-aware smart-indent rules should apply.</param>
public readonly record struct IndentationContext(
	string PreviousLineText,
	string CurrentLineText,
	string IndentationUnit,
	bool UseSmartIndent);

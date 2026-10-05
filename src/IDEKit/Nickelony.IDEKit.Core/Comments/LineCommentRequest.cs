using Nickelony.IDEKit.Core.Scanning;
using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Comments;

/// <summary>Carries the inputs of a line-comment transformation.</summary>
/// <param name="Snapshot">The snapshot of the text containing the selection.</param>
/// <param name="Selection">The zero-based selection range within the snapshot; offsets outside it are clamped.</param>
/// <param name="Syntax">The comment syntax whose line-comment delimiter is applied.</param>
/// <param name="Action">The line-comment transformation to apply.</param>
/// <param name="Spacing">How a commented line follows its delimiter.</param>
/// <remarks>
/// A <see langword="default"/> instance has a <see langword="null"/> snapshot, which the planner rejects, and a zero-length selection at offset zero.
/// </remarks>
public readonly record struct LineCommentRequest(
	ITextSnapshot Snapshot,
	TextRange Selection,
	CommentSyntax Syntax,
	LineCommentActionKind Action,
	LineCommentSpacingStyle Spacing
);

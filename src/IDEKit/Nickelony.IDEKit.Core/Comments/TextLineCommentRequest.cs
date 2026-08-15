using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Comments;

/// <summary>
/// Carries the inputs of a line-comment transformation.
/// </summary>
/// <remarks>
/// A request groups the snapshot, the selection, the syntax, the transformation, and the insertion
/// convention, so <see cref="TextLineCommentPlanner.TryCreateEdit(in TextLineCommentRequest, out TextLineCommentEdit)"/>
/// stays one value wide. A <see langword="default"/> instance carries a <see langword="null"/>
/// snapshot and a zero-length selection, which the planner rejects when the snapshot is
/// <see langword="null"/> and otherwise treats as a selection at offset zero.
/// </remarks>
/// <param name="Snapshot">The snapshot of the text containing the selection.</param>
/// <param name="Selection">The zero-based selection range within the snapshot; offsets outside it are clamped.</param>
/// <param name="Syntax">The comment syntax whose line-comment delimiter is applied.</param>
/// <param name="Action">The line-comment transformation to apply.</param>
/// <param name="InsertSpaceAfterDelimiter">
/// <see langword="true"/> to follow the delimiter with the single space that mainstream desktop
/// editors insert; <see langword="false"/> to insert the bare delimiter. Uncommenting always
/// removes the delimiter plus one following space when present, so both settings round-trip.
/// </param>
public readonly record struct TextLineCommentRequest(
	ITextSnapshot Snapshot,
	TextRange Selection,
	CommentSyntax Syntax,
	TextLineCommentAction Action,
	bool InsertSpaceAfterDelimiter);

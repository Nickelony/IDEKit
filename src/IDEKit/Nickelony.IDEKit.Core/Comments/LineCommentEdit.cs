using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Comments;

/// <summary>Describes the line-comment edit that <see cref="LineCommentPlanner"/> plans and the selection to apply after it.</summary>
/// <param name="ReplaceRange">The range to replace.</param>
/// <param name="NewText">The text that replaces the range.</param>
/// <param name="Selection">The post-edit selection range; the remarks state its exact contract.</param>
/// <remarks>
/// Ranges are zero-based and measured in UTF-16 code units. <see cref="Selection"/> is measured
/// in the post-edit document, starts at <see cref="ReplaceRange"/>'s start, and covers the transformed content without the final line's terminator;
/// it is not clamped. A <see langword="default"/> instance carries a <see langword="null"/> <see cref="NewText"/>; read the value only from an edit
/// the planner produced.
/// </remarks>
public readonly record struct LineCommentEdit(TextRange ReplaceRange, string NewText, TextRange Selection);

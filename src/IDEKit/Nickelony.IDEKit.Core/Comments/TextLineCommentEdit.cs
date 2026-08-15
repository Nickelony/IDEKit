using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Comments;

/// <summary>
/// Describes a line-comment edit and the selection to apply after it.
/// </summary>
/// <remarks>
/// <para>Ranges are zero-based and measured in UTF-16 code units.</para>
/// <para>
/// <see cref="Selection"/> is the post-edit selection range in the document the edit was created
/// for: its offset is measured in the post-edit document and starts at <see cref="ReplaceRange"/>'s
/// start, and its length covers the transformed content without the final line's terminator. It is
/// not clamped against any document, so a caller that applies it elsewhere must clamp it.
/// </para>
/// <para>
/// A <see langword="default"/> instance carries a <see langword="null"/>
/// <see cref="NewText"/> and a zero-length selection despite the annotations; read the value
/// only from an edit that <see cref="TextLineCommentPlanner.TryCreateEdit"/> produced.
/// </para>
/// </remarks>
/// <param name="ReplaceRange">The range to replace.</param>
/// <param name="NewText">The text that replaces the range.</param>
/// <param name="Selection">The post-edit selection range; the remarks state its exact contract.</param>
public readonly record struct TextLineCommentEdit(
	TextRange ReplaceRange,
	string NewText,
	TextRange Selection);

using Nickelony.IDEKit.Core.Comments;
using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Scanning;
using Nickelony.IDEKit.Core.Text;
#if AVALONIAEDIT
using AvaloniaEdit;
using AvaloniaEdit.Document;
#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.Comments;

#else

namespace Nickelony.IDEKit.AvalonEdit.Comments;

#endif

/// <summary>Provides line-comment transformations for an individual editor.</summary>
/// <remarks>
/// The edit computation is editor-neutral and lives in the Core <see cref="LineCommentPlanner"/>,
/// which preserves leading whitespace, leaves whitespace-only lines unchanged, keeps each line's terminator,
/// and gives a final unterminated line no new one. Implementations mutate the editor's document and must run on the editor's thread.
/// <see cref="LineCommentService"/> is the default implementation: it delegates to the planner and keeps no per-call state, so a host can compose
/// one instance per editor and substitute its own.
/// </remarks>
public interface ILineCommentService
{
	/// <summary>Creates an edit for the requested line-comment transformation on the selected lines.</summary>
	/// <param name="document">The editor's <see cref="TextDocument"/> containing the selection.</param>
	/// <param name="selection">The zero-based selection range within the document.</param>
	/// <param name="commentSyntax">The comment syntax whose line-comment delimiter is applied.</param>
	/// <param name="action">The line-comment transformation to apply.</param>
	/// <param name="spacing">How a commented line follows its delimiter; see <see cref="LineCommentSpacingStyle"/>.</param>
	/// <param name="edit">The created edit when the method returns <see langword="true"/>; otherwise, the default edit.</param>
	/// <returns>
	/// <see langword="true"/> when the document contains text and <paramref name="commentSyntax"/> provides
	/// a non-blank line-comment delimiter; otherwise, <see langword="false"/>.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="document"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// The transformation covers every line the selection touches: a non-collapsed selection ending at a line start
	/// excludes that line, while a collapsed selection at a line start includes it. The returned edit can be a no-op when no selected line changes;
	/// <see cref="ApplyEdit"/> skips identical text, and a caller that applies edits directly should too, to avoid a pointless undo entry.
	/// The edit's offsets are valid only for the document state at call time, so a caller that applies it later must re-create it. The method takes
	/// the editor document instead of a <see cref="LineCommentRequest"/>, because the request holds a snapshot that this method captures
	/// from the document; a caller that already holds a snapshot calls <see cref="LineCommentPlanner"/> directly.
	/// </remarks>
	bool TryCreateEdit(
		TextDocument document,
		TextRange selection,
		CommentSyntax commentSyntax,
		LineCommentActionKind action,
		LineCommentSpacingStyle spacing,
		out LineCommentEdit edit
	);

	/// <summary>Applies the requested line-comment transformation to the editor's current selection.</summary>
	/// <param name="editor">The editor whose current selection is transformed.</param>
	/// <param name="commentSyntax">The syntax providing the line-comment delimiter.</param>
	/// <param name="action">The transformation to apply.</param>
	/// <param name="spacing">How a commented line follows its delimiter; see <see cref="LineCommentSpacingStyle"/>.</param>
	/// <param name="editTarget">
	/// The host-owned target to which the edit is applied, or <see langword="null"/> to edit the editor's document directly.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="editor"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">The editor has no document assigned.</exception>
	/// <remarks>
	/// A transformation that would not change the selected text is not applied, so the document and undo stack stay untouched when no selected line
	/// has the delimiter to remove. A supplied target must satisfy the <see cref="ITextEditTarget"/> contract and apply the operation synchronously
	/// within the call, because the recorded post-edit selection is restored right after the target returns; a target that publishes the change later
	/// leaves the selection on stale content.
	/// </remarks>
	void ApplyEdit(
		TextEditor editor,
		CommentSyntax commentSyntax,
		LineCommentActionKind action,
		LineCommentSpacingStyle spacing = LineCommentSpacingStyle.SpaceAfterDelimiter,
		ITextEditTarget? editTarget = null
	);
}

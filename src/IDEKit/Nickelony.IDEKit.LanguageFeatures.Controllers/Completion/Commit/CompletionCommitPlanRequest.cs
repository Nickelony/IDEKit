using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Completion;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Completion.Commit;

/// <summary>
/// The complete input of one completion commit plan: the document snapshot, the replacement range, the insertion text, and the item's edits.
/// </summary>
/// <param name="Snapshot">The snapshot of the document that receives the commit.</param>
/// <param name="CompletionSegment">The live range the completion window supplies as the fallback replacement range.</param>
/// <param name="InsertText">The text that replaces the primary range.</param>
/// <param name="CaretOffsetInInsertText">
/// The caret offset within <paramref name="InsertText"/> after the commit, or <see langword="null"/> to leave the caret where the document changes
/// place it.
/// </param>
/// <param name="AdditionalTextEdits">The secondary edits to apply with the insertion.</param>
/// <param name="PrimaryEdit">
/// The item's primary text edit; its range is the primary replacement range while the range still fits the snapshot,
/// or <see langword="null"/> to use <paramref name="CompletionSegment"/>.
/// </param>
/// <param name="IsOverstrikeMode">Whether the editor is in overstrike mode, which selects the edit's replacement range over its insert range.</param>
/// <param name="CaretOffset">
/// The caret offset in the document, or <see langword="null"/> when it is unknown. It caps how far an item range that ends before it extends
/// toward the end of <paramref name="CompletionSegment"/>, and without it no item range extends.
/// </param>
public readonly record struct CompletionCommitPlanRequest(
	ITextSnapshot Snapshot,
	TextRange CompletionSegment,
	string InsertText,
	int? CaretOffsetInInsertText,
	IReadOnlyList<CompletionTextEdit> AdditionalTextEdits,
	CompletionTextEdit? PrimaryEdit,
	bool IsOverstrikeMode,
	int? CaretOffset = null
);

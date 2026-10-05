using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion.Commit;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion.Snippets;
#if AVALONIAEDIT
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
#else
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;

#else

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;

#endif

/// <summary>
/// The inputs of one completion commit: the primary insertion, the segment it replaces, and the item's optional primary text edit
/// and secondary edits.
/// </summary>
/// <param name="TextArea">The text area whose document receives the commit.</param>
/// <param name="CompletionSegment">The live segment the completion window supplies as the fallback replacement range.</param>
/// <param name="InsertText">The text that replaces the primary range.</param>
/// <param name="CaretOffsetInInsertText">
/// The caret offset within <paramref name="InsertText"/> after the commit, or <see langword="null"/> to leave the caret where the document changes
/// place it.
/// </param>
/// <param name="AdditionalTextEdits">The secondary edits to apply with the insertion.</param>
/// <param name="PrimaryEdit">
/// The item's primary text edit; its range is the primary replacement range while the range still fits the current document,
/// or <see langword="null"/> to use <paramref name="CompletionSegment"/>.
/// </param>
/// <param name="AdjustInsertion">
/// The host hook that rewrites the insertion for the start offset of the primary range, or <see langword="null"/> to apply it as resolved.
/// </param>
/// <param name="Snippet">
/// The expansion that <paramref name="InsertText"/> came from, whose tabstops the commit makes navigable while the insertion text is unchanged,
/// or <see langword="null"/> for an insertion that is plain text.
/// </param>
internal readonly record struct CompletionCommitRequest(
	TextArea TextArea,
	ISegment CompletionSegment,
	string InsertText,
	int? CaretOffsetInInsertText,
	IReadOnlyList<CompletionTextEdit> AdditionalTextEdits,
	CompletionTextEdit? PrimaryEdit,
	Func<ITextSnapshot, int, CompletionInsertion, CompletionInsertion>? AdjustInsertion = null,
	SnippetExpansion? Snippet = null
);

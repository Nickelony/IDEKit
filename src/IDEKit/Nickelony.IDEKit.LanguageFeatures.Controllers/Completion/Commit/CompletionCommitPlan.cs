using Nickelony.IDEKit.Core.Editing;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Completion.Commit;

/// <summary>The document changes and the caret of one completion commit, ready for a binding to apply to its document.</summary>
/// <param name="Operations">
/// The replacements to apply, in application order: from the highest to the lowest source offset, each against its original coordinates,
/// so no applied replacement shifts the offsets of one that is still pending. A binding applies more than one operation as a single document update,
/// so a single undo restores the document.
/// </param>
/// <param name="CaretOffset">
/// The caret offset to set after the operations were applied, already clamped to the length of the document after the commit,
/// or <see langword="null"/> to leave the caret where the document changes place it.
/// </param>
public sealed record CompletionCommitPlan(IReadOnlyList<TextEditOperation> Operations, int? CaretOffset);

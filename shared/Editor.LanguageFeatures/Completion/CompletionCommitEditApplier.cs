#if AVALONIAEDIT
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using Nickelony.IDEKit.AvaloniaEdit.Documents;
#else
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using Nickelony.IDEKit.AvalonEdit.Documents;
#endif
using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.Completion;

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
#else
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
#endif

/// <summary>
/// Applies a completion commit: the primary insertion and the item's additional text edits are
/// applied as one atomic document change.
/// </summary>
/// <remarks>
/// <para>
/// The primary replacement range is the item's edit range while that range still fits the current
/// document (the range was produced against an earlier snapshot): the edit contributes its insert
/// range normally and its replacement range while the text area's overstrike mode is on. The
/// supplied completion segment is the fallback for a missing or stale range. With additional edits
/// present, the primary insertion and every accepted edit are applied inside one document update, so
/// the whole commit is one undo unit and a single undo restores the document. Without additional edits
/// the insertion is applied directly, exactly like a commit that carries no secondary data.
/// </para>
/// <para>
/// The batch is prepared by
/// <see cref="TextEditKernel.PrepareSkippingInvalidEntries(ITextSnapshot, System.Collections.Generic.IEnumerable{TextEditInput?})"/>,
/// so the conflict conventions are the shared kernel's: an insertion that only touches a replacement's
/// boundaries is accepted, an insertion strictly inside a replacement is dropped, and a replacement is
/// applied before an insertion that shares its start offset. An entry that cannot be applied is skipped
/// individually instead of failing the commit - an entry without explicit replacement text, an entry
/// whose range lies outside the current document (a stale range), an insertion strictly inside an
/// accepted replacement, and two overlapping replacements - matching how the response parsers treat
/// malformed payload entries, because a commit must not be lost to one bad secondary payload. The
/// primary insertion is the batch's first entry, so it is always accepted and a conflict is resolved in
/// its favor. The accepted operations are applied from the highest to the lowest offset against their
/// original coordinates, so no applied edit shifts the offsets of an edit that is still pending.
/// </para>
/// </remarks>
internal static class CompletionCommitEditApplier
{
	/// <summary>
	/// Applies a completion commit: the primary insertion and the item's additional text edits are
	/// applied as one document change.
	/// </summary>
	/// <param name="payload">The commit to apply.</param>
	/// <exception cref="InvalidOperationException">The text area has no document to apply the commit to.</exception>
	internal static void Apply(CompletionCommitPayload payload)
	{
		// The text area's document can be cleared while the completion window is still open; failing with a
		// documented exception keeps the commit path from dereferencing a null document inside the engine.
		TextDocument document = payload.TextArea.Document
			?? throw new InvalidOperationException("The text area has no document to apply the completion commit to.");
		(int primaryOffset, int primaryLength) = ResolvePrimarySegment(
			payload.TextArea,
			payload.CompletionSegment,
			payload.PrimaryEdit);

		if (payload.AdditionalTextEdits.Count == 0)
		{
			document.Replace(primaryOffset, primaryLength, payload.InsertText);
			ApplyCaretOffset(payload.TextArea, primaryOffset, payload.CaretOffsetInInsertText);

			return;
		}

		var inputs = new List<TextEditInput>(payload.AdditionalTextEdits.Count + 1)
		{
			new(new TextRange(primaryOffset, primaryLength), payload.InsertText)
		};

		for (int index = 0; index < payload.AdditionalTextEdits.Count; index++)
		{
			TextCompletionTextEdit edit = payload.AdditionalTextEdits[index];
			inputs.Add(new TextEditInput(edit.ReplacementRange, edit.NewText));
		}

		PreparedTextEdits prepared = TextEditKernel
			.PrepareSkippingInvalidEntries(new TextDocumentSnapshot(document), inputs)
			.Edits;

		// The operations are in the kernel's application order (highest source offset first) and the
		// primary insertion is the batch's first entry, so its own position in that order decides which
		// operations shift it: every operation applied after the primary lies at or below the primary
		// start - an operation at the primary start itself is ordered before the primary insertion, so it
		// never shifts it - and each one shifts the inserted text, and the caret derived from it, by its
		// net length delta. Deriving the shift from the primary's position in the application order avoids
		// the ambiguity of comparing offsets directly, where an insertion at exactly the primary start
		// shares that offset yet is applied before the primary insertion.
		int caretShift = 0;
		int primaryOperationIndex = FindPrimaryOperationIndex(prepared.Operations);

		for (int index = primaryOperationIndex + 1; index < prepared.Operations.Count; index++)
		{
			TextEditOperation operation = prepared.Operations[index];
			caretShift += operation.NewText.Length - operation.Length;
		}

		using (document.RunUpdate())
		{
			foreach (TextEditOperation operation in prepared.Operations)
				document.Replace(operation.StartOffset, operation.Length, operation.NewText);
		}

		ApplyCaretOffset(payload.TextArea, primaryOffset + caretShift, payload.CaretOffsetInInsertText);
	}

	/// <summary>
	/// Finds the primary insertion's operation in the prepared batch, which is in the kernel's
	/// application order.
	/// </summary>
	/// <param name="operations">The batch's operations.</param>
	/// <returns>
	/// The index of the operation carrying edit index zero; the operation count when the primary changes
	/// nothing and was dropped by the kernel, which yields a zero shift.
	/// </returns>
	private static int FindPrimaryOperationIndex(IReadOnlyList<TextEditOperation> operations)
	{
		for (int index = 0; index < operations.Count; index++)
		{
			if (operations[index].EditIndex == 0)
				return index;
		}

		return operations.Count;
	}

	/// <summary>
	/// Resolves the primary replacement range: the edit payload's range while it still fits the current
	/// document, or the supplied completion segment otherwise.
	/// </summary>
	/// <param name="textArea">The text area whose document receives the commit.</param>
	/// <param name="completionSegment">The live segment the completion window supplies.</param>
	/// <param name="primaryEdit">The item's edit payload, when the item carries one.</param>
	/// <returns>The zero-based primary replacement range.</returns>
	private static (int Offset, int Length) ResolvePrimarySegment(
		TextArea textArea,
		ISegment completionSegment,
		TextCompletionTextEdit? primaryEdit)
	{
		var fallbackSegment = (completionSegment.Offset, completionSegment.Length);

		if (primaryEdit is not TextCompletionTextEdit edit)
			return fallbackSegment;

		// Overstrike mode replaces the edit's replace range, matching how the editor's own completion
		// window treats insert versus replace ranges.
		TextRange range = textArea.OverstrikeMode ? edit.ReplacementRange : edit.InsertRange;
		TextDocument document = textArea.Document;

		// A range outside the current document is stale: the item was produced against an earlier
		// document state, so the commit falls back to the live completion segment.
		return range.FitsWithin(document.TextLength)
			? (range.Offset, range.Length)
			: fallbackSegment;
	}

	/// <summary>
	/// Places the caret at the offset derived from the insertion text, when one was supplied.
	/// </summary>
	private static void ApplyCaretOffset(TextArea textArea, int insertionStartOffset, int? caretOffsetInInsertText)
	{
		if (caretOffsetInInsertText is int offset)
			textArea.Caret.Offset = insertionStartOffset + offset;
	}
}

/// <summary>
/// The inputs of one completion commit: the primary insertion, the segment it replaces, and the
/// item's optional edit payload and secondary edits.
/// </summary>
/// <param name="TextArea">The text area whose document receives the commit.</param>
/// <param name="CompletionSegment">
/// The live segment the completion window supplies as the fallback replacement range.
/// </param>
/// <param name="InsertText">The text that replaces the primary range.</param>
/// <param name="CaretOffsetInInsertText">
/// The caret offset within <paramref name="InsertText"/> after the commit, or <see langword="null"/>
/// to leave the caret where the document changes place it.
/// </param>
/// <param name="AdditionalTextEdits">The secondary edits to apply with the insertion.</param>
/// <param name="PrimaryEdit">
/// The item's edit payload; its range is the primary replacement range while the range still fits the
/// current document, or <see langword="null"/> to use <paramref name="CompletionSegment"/>.
/// </param>
internal readonly record struct CompletionCommitPayload(
	TextArea TextArea,
	ISegment CompletionSegment,
	string InsertText,
	int? CaretOffsetInInsertText,
	IReadOnlyList<TextCompletionTextEdit> AdditionalTextEdits,
	TextCompletionTextEdit? PrimaryEdit);

using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Completion;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Completion.Commit;

/// <summary>Plans the document changes and the caret of a completion commit without touching a document.</summary>
/// <remarks>
/// <para>
/// The primary replacement range is the item's edit range while that range still fits the snapshot, because a range produced
/// against an earlier snapshot can fall outside it. The edit contributes its insert range normally and its replacement range in overstrike mode.
/// The supplied completion segment is the fallback for a missing or stale range, and a range that ends before the caret extends up to the caret,
/// never past the segment's end, because the word was typed further after the item was answered. Without additional edits the plan holds
/// the one primary replacement and applies it even when it changes nothing.
/// </para>
/// <para>
/// With additional edits, the primary replacement and every additional edit form one batch that <see cref="TextEditKernel"/> prepares,
/// so the conflict conventions are the kernel's: an insertion that only touches a replacement's boundaries is accepted, an insertion strictly
/// inside a replacement is dropped, and a replacement is ordered before an insertion that shares its start offset. An entry that cannot be applied
/// is skipped individually instead of failing the commit, because a commit must not be lost to one bad secondary edit. The primary replacement
/// is the batch's first entry, so a conflict is resolved in its favor, and it is accepted unless it changes nothing.
/// </para>
/// </remarks>
public static class CompletionCommitPlanner
{
	/// <summary>Plans a completion commit against a snapshot of the document.</summary>
	/// <param name="request">The complete input of the commit.</param>
	/// <returns>
	/// The operations to apply, in application order (highest source offset first, each against its original coordinates), and the caret offset
	/// to set afterwards.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// The snapshot, the insertion text, or the additional edit list of <paramref name="request"/> is <see langword="null"/>.
	/// </exception>
	public static CompletionCommitPlan Plan(in CompletionCommitPlanRequest request)
	{
		ArgumentNullException.ThrowIfNull(request.Snapshot);
		ArgumentNullException.ThrowIfNull(request.InsertText);
		ArgumentNullException.ThrowIfNull(request.AdditionalTextEdits);

		TextRange primary = ResolvePrimaryRange(request);

		if (request.AdditionalTextEdits.Count == 0)
		{
			var operation = new TextEditOperation(primary, request.InsertText, editIndex: 0);
			return new CompletionCommitPlan([operation], ResolveCaretOffset(request, [operation], primary.Offset));
		}

		var inputs = new List<TextIncrementalEdit>(request.AdditionalTextEdits.Count + 1) { new(primary, request.InsertText) };

		for (int index = 0; index < request.AdditionalTextEdits.Count; index++)
		{
			CompletionTextEdit edit = request.AdditionalTextEdits[index];

			// An entry that omits its replacement text is skipped instead of failing the commit.
			if (edit.NewText is { } newText)
				inputs.Add(new TextIncrementalEdit(edit.ReplacementRange, newText));
		}

		IReadOnlyList<TextEditOperation> operations = TextEditKernel.PrepareSkippingInvalidEntries(request.Snapshot, inputs).Edits.Operations;

		// The operations are in the kernel's application order (highest source offset first), and the primary replacement is the batch's first
		// entry, so its own position decides which operations shift it: every operation applied after the primary lies at or below the primary
		// start - one at the primary start itself is ordered before the primary replacement, so it never shifts it - and each shifts the inserted
		// text and its caret by its net length delta. Deriving the shift from that position avoids comparing offsets directly, where an insertion
		// at the primary start shares it.
		int caretShift = 0;

		for (int index = FindPrimaryOperationIndex(operations) + 1; index < operations.Count; index++)
			caretShift += operations[index].NewText.Length - operations[index].Range.Length;

		return new CompletionCommitPlan(operations, ResolveCaretOffset(request, operations, primary.Offset + caretShift));
	}

	// The index of the operation carrying edit index zero; the operation count when the primary changes nothing and the kernel dropped it,
	// which yields a zero shift.
	private static int FindPrimaryOperationIndex(IReadOnlyList<TextEditOperation> operations)
	{
		for (int index = 0; index < operations.Count; index++)
		{
			if (operations[index].EditIndex == 0)
				return index;
		}

		return operations.Count;
	}

	/// <summary>Resolves the range that the commit's insertion replaces, the range <see cref="Plan"/> uses for the primary replacement.</summary>
	/// <param name="request">The complete input of the commit.</param>
	/// <returns>
	/// The item's edit range while it fits the snapshot, extended toward the caret and no further than the end of the completion segment
	/// when the range ends before the caret; otherwise, the completion segment of <paramref name="request"/>.
	/// </returns>
	/// <exception cref="ArgumentNullException">The snapshot of <paramref name="request"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// The range does not depend on the insertion text, so a caller that rewrites the insertion for its target offset resolves the range first
	/// and plans the rewritten request.
	/// </remarks>
	public static TextRange ResolvePrimaryRange(in CompletionCommitPlanRequest request)
	{
		ArgumentNullException.ThrowIfNull(request.Snapshot);

		if (request.PrimaryEdit is not CompletionTextEdit edit)
			return request.CompletionSegment;

		// Overstrike mode replaces the edit's replace range, matching how the editor's own completion window treats insert versus replace ranges.
		TextRange range = request.IsOverstrikeMode ? edit.ReplacementRange : edit.InsertRange;

		// A range outside the snapshot is stale: the item was produced against an earlier document state, so the commit falls back to the live
		// completion segment.
		if (!range.FitsWithin(request.Snapshot.TextLength))
			return request.CompletionSegment;

		// An item whose range ends before the caret was answered before the word grew, so the commit also replaces the part typed since, as an item
		// answered after the typing would. The caret caps the extension and the segment's end bounds it, so a segment that spans a whole word never
		// widens an insert range past what was typed; a range that reaches the caret or passes it stays as it is.
		if (request.CaretOffset is not int caret)
			return range;

		TextRange segment = request.CompletionSegment;
		int end = Math.Min(caret, segment.EndOffset);

		return end > range.EndOffset && segment.Offset <= range.EndOffset && end <= request.Snapshot.TextLength
			? TextRange.FromBounds(range.Offset, end)
			: range;
	}

	private static int? ResolveCaretOffset(
		in CompletionCommitPlanRequest request,
		IReadOnlyList<TextEditOperation> operations,
		int insertionStartOffset
	)
	{
		if (request.CaretOffsetInInsertText is not int offset)
			return null;

		int lengthAfterCommit = request.Snapshot.TextLength;

		for (int index = 0; index < operations.Count; index++)
			lengthAfterCommit += operations[index].NewText.Length - operations[index].Range.Length;

		// The caret offset within the insertion text can place the caret past the text applied by the commit (the offset was derived against an
		// earlier document state), so it is clamped to the length after the commit; the sum is widened so a large insertion start cannot overflow
		// before the clamp.
		long requestedOffset = (long)insertionStartOffset + offset;

		return (int)Math.Clamp(requestedOffset, 0L, lengthAfterCommit);
	}
}

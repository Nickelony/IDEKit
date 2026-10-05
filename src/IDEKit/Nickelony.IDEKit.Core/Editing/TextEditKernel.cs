using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Editing;

/// <summary>Prepares neutral text edits without mutating a document or depending on a UI host or protocol.</summary>
public static class TextEditKernel
{
	/// <summary>Validates source-offset edits against a text snapshot and returns descending operations.</summary>
	/// <param name="snapshot">The immutable source text used for range validation.</param>
	/// <param name="edits">
	/// The edits to validate and prepare. A <see langword="null"/> replacement text, which a <see langword="default"/> edit carries,
	/// is reported as an issue instead of throwing.
	/// </param>
	/// <returns>
	/// A successful result containing descending-offset operations when all edits are valid, or a failed result with no operations and the collected
	/// issues when any edit is rejected.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="snapshot"/> or <paramref name="edits"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// An insertion that only touches a replacement's boundaries is accepted: one at the replacement's start lands before the replacement
	/// text and one at its end lands after it. Multiple insertions at one source offset are ordered so applying the list in order inserts their texts
	/// in the caller's edit order. A no-op edit contributes no operation but is still range-validated, so an out-of-range no-op is rejected;
	/// any conflict rejects the batch.
	/// </remarks>
	public static TextEditPreparationResult Prepare(ITextSnapshot snapshot, IEnumerable<TextIncrementalEdit> edits)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		ArgumentNullException.ThrowIfNull(edits);

		var candidates = new List<Candidate>();
		var issues = new List<TextEditPreparationIssue>();

		int editIndex = 0;

		foreach (TextIncrementalEdit edit in edits)
		{
			TextEditPreparationIssue? invalidIssue = CreateInvalidEditIssue(edit, snapshot.TextLength, editIndex);

			if (invalidIssue is not null)
			{
				issues.Add(invalidIssue);
			}
			else if (!edit.IsNoOp)
			{
				// An empty range with an empty replacement text changes nothing, so it is ignored
				// instead of being classified as an insertion and rejected for starting inside a
				// replacement range.
				candidates.Add(new Candidate(editIndex, edit.Range, edit.NewText));
			}

			editIndex++;
		}

		candidates.Sort(CandidateComparer.Instance);

		// The candidates are already in the shared order key, so the conflict pass consumes the same
		// sequence instead of filtering, copying, and sorting the batch a second time.
		var ascendingOperations = new TextEditOperation[candidates.Count];

		for (int index = 0; index < candidates.Count; index++)
		{
			Candidate candidate = candidates[index];
			ascendingOperations[index] = new TextEditOperation(range: candidate.Range, newText: candidate.NewText, editIndex: candidate.EditIndex);
		}

		AddConflictIssues(ascendingOperations, issues);

		if (issues.Count > 0)
			return TextEditPreparationResult.Failure(issues);

		// The application order is the reverse of the candidate order, so the highest source offset
		// applies first; the ascending array is reversed in place because it is not read again and
		// the carrier takes ownership of it.
		Array.Reverse(ascendingOperations);

		// The shared conflict detector already ran over this batch above, so the carrier is created
		// through the kernel-validated factory instead of re-detecting the same conflicts in its
		// validating constructor.
		return TextEditPreparationResult.Success(PreparedTextEdits.CreateKernelValidated(ascendingOperations));
	}

	/// <summary>
	/// Prepares a batch in lenient mode: entries that are invalid or that conflict with an earlier accepted entry are skipped and reported
	/// instead of rejecting the whole batch.
	/// </summary>
	/// <param name="snapshot">The immutable source text used for range validation.</param>
	/// <param name="edits">
	/// The edits to validate and prepare. A <see langword="null"/> replacement text, which a <see langword="default"/> edit carries,
	/// is skipped with an issue instead of throwing.
	/// </param>
	/// <returns>
	/// A result carrying the accepted operations and one issue per skipped entry; the batch may be empty when every entry was skipped.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="snapshot"/> or <paramref name="edits"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// Entries are evaluated in caller order, so the first valid, non-no-op entry is always accepted and a caller keeps the entry it ranks highest
	/// by listing it first, even when a later entry overlaps it. An entry is skipped when it carries a <see langword="null"/> replacement text,
	/// has a range outside the snapshot, or conflicts with an earlier accepted entry under the shared range rule set; a valid no-op
	/// entry is ignored. The acceptance walk compares each entry with those accepted before it, so the cost is quadratic in the batch size.
	/// Use <see cref="Prepare(ITextSnapshot, IEnumerable{TextIncrementalEdit})"/> when a batch must be all-or-nothing.
	/// </remarks>
	public static TextEditSkipResult PrepareSkippingInvalidEntries(ITextSnapshot snapshot, IEnumerable<TextIncrementalEdit> edits)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		ArgumentNullException.ThrowIfNull(edits);

		var accepted = new List<TextEditOperation>();

		List<TextEditPreparationIssue>? skippedIssues = null;
		int editIndex = 0;

		foreach (TextIncrementalEdit edit in edits)
		{
			TextEditPreparationIssue? invalidIssue = CreateInvalidEditIssue(edit, snapshot.TextLength, editIndex);

			if (invalidIssue is not null)
			{
				(skippedIssues ??= []).Add(invalidIssue);
			}
			else if (!edit.IsNoOp)
			{
				var operation = new TextEditOperation(range: edit.Range, newText: edit.NewText, editIndex);

				TextEditOperation? conflicting = FindAcceptedConflict(operation, accepted);

				if (conflicting is null)
					accepted.Add(operation);
				else
					(skippedIssues ??= []).Add(CreateConflictIssue(operation, conflicting, editIndex));
			}

			editIndex++;
		}

		// The accepted operations are emitted in the shared application order, the order key descending:
		// highest source offset first, then highest end offset, and for equal ranges the highest edit
		// index first (the order key is total while edit indexes are unique per batch).
		accepted.Sort(static (left, right) => TextEditOrderKey.From(right).CompareTo(TextEditOrderKey.From(left)));

		return TextEditSkipResult.Success(PreparedTextEdits.CreateKernelValidated([.. accepted]), skippedIssues ?? []);
	}

	/// <summary>Validates a hand-built batch of operations and returns the operations in application order.</summary>
	/// <param name="operations">The operations to validate; they need not be ordered.</param>
	/// <returns>
	/// A successful result carrying the operations in descending application order when the batch is valid, or a failed result carrying the issues
	/// that rejected it.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="operations"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// A rejected batch is reported as preparation issues instead of throwing.
	/// <see cref="TextEditOperation"/> validates its own replacement text and offsets, so the only invalid entry is a <see langword="null"/>
	/// element and a no-op is ignored. Ranges are not validated against a snapshot, and conflicts follow the shared range rule set;
	/// use <see cref="Prepare(ITextSnapshot, IEnumerable{TextIncrementalEdit})"/> to check ranges against a document.
	/// </remarks>
	public static TextEditPreparationResult Prepare(IReadOnlyList<TextEditOperation> operations)
	{
		ArgumentNullException.ThrowIfNull(operations);

		var issues = new List<TextEditPreparationIssue>();
		var candidates = new List<TextEditOperation>(operations.Count);

		for (int index = 0; index < operations.Count; index++)
		{
			TextEditOperation? operation = operations[index];

			if (operation is null)
			{
				issues.Add(
					new TextEditPreparationIssue(
						kind: TextEditPreparationIssueKind.NullEdit,
						editIndex: index,
						relatedEditIndex: null,
						message: "The edit is null."
					)
				);
			}
			else if (!operation.IsNoOp)
			{
				candidates.Add(operation);
			}
		}

		// The candidates are ordered by the shared order key before the conflict pass, so the batch is
		// accepted regardless of the caller's ordering.
		candidates.Sort(static (left, right) => TextEditOrderKey.From(left).CompareTo(TextEditOrderKey.From(right)));

		AddConflictIssues(candidates, issues);

		if (issues.Count > 0)
			return TextEditPreparationResult.Failure(issues);

		// The application order is the reverse of the candidate order, so the highest source offset
		// applies first.
		TextEditOperation[] applicationOrder = [.. candidates];
		Array.Reverse(applicationOrder);

		return TextEditPreparationResult.Success(PreparedTextEdits.CreateKernelValidated(applicationOrder));
	}

	/// <summary>Creates the issue for an invalid edit, or <see langword="null"/> when the edit is valid.</summary>
	/// <remarks>
	/// An edit is classified the same way in either preparation mode: an edit is invalid when it carries a <see langword="null"/> replacement text
	/// or has a range outside the document. The range is validated before the no-op check, so an out-of-range no-op is still reported.
	/// </remarks>
	private static TextEditPreparationIssue? CreateInvalidEditIssue(TextIncrementalEdit edit, int textLength, int editIndex)
	{
		if (edit.NewText is null)
		{
			return new TextEditPreparationIssue(
				kind: TextEditPreparationIssueKind.NullReplacementText,
				editIndex,
				relatedEditIndex: null,
				message: "The edit replacement text is null."
			);
		}

		if (!edit.Range.FitsWithin(textLength))
		{
			return new TextEditPreparationIssue(
				kind: TextEditPreparationIssueKind.RangeOutOfBounds,
				editIndex,
				relatedEditIndex: null,
				message: "The edit range is outside the document."
			);
		}

		return null;
	}

	private static TextEditOperation? FindAcceptedConflict(TextEditOperation candidate, List<TextEditOperation> accepted)
	{
		for (int index = 0; index < accepted.Count; index++)
		{
			if (TextEditConflictDetector.Conflicts(accepted[index], candidate))
				return accepted[index];
		}

		return null;
	}

	private static TextEditPreparationIssue CreateConflictIssue(TextEditOperation candidate, TextEditOperation conflicting, int editIndex)
	{
		// At most one member of a conflicting pair is an insertion, because two insertions never conflict,
		// so the pair is an insertion inside a replacement when either member is an insertion and a
		// replacement overlap otherwise.
		TextEditPreparationIssueKind kind =
			candidate.Range.IsEmpty || conflicting.Range.IsEmpty
				? TextEditPreparationIssueKind.InsertionInsideReplacement
				: TextEditPreparationIssueKind.ReplacementOverlap;

		return new TextEditPreparationIssue(
			kind,
			editIndex,
			relatedEditIndex: conflicting.EditIndex,
			message: TextEditConflictMessages.GetIssueMessage(kind)
		);
	}

	/// <summary>Caps the pairwise conflict issues for one batch; the cap only shapes the report, since any conflict rejects the batch.</summary>
	private const int MaximumConflictIssueCount = 512;

	/// <summary>
	/// Adds an issue for every conflict reported by the shared conflict detector, capped at <see cref="MaximumConflictIssueCount"/> so a pathological
	/// overlapping batch cannot produce a quadratic issue list.
	/// </summary>
	/// <remarks>
	/// The issues follow the order of <see cref="TextEditConflictDetector.FindConflicts"/>.
	/// </remarks>
	private static void AddConflictIssues(IReadOnlyList<TextEditOperation> orderedOperations, List<TextEditPreparationIssue> issues)
	{
		foreach (TextEditConflict conflict in TextEditConflictDetector.FindConflictsInCandidateOrder(orderedOperations, MaximumConflictIssueCount))
		{
			issues.Add(
				new TextEditPreparationIssue(
					kind: conflict.Kind,
					editIndex: conflict.EditIndex,
					relatedEditIndex: conflict.RelatedEditIndex,
					message: TextEditConflictMessages.GetIssueMessage(conflict.Kind)
				)
			);
		}
	}

	private readonly record struct Candidate(int EditIndex, TextRange Range, string NewText)
	{
		internal TextEditOrderKey OrderKey => new(Range, EditIndex);
	}

	private sealed class CandidateComparer : IComparer<Candidate>
	{
		internal static CandidateComparer Instance { get; } = new();
		public int Compare(Candidate left, Candidate right) => left.OrderKey.CompareTo(right.OrderKey);
	}
}

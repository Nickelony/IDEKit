using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Editing;

/// <summary>
/// Prepares neutral text edits without mutating a document or depending on a UI host or protocol.
/// </summary>
public static class TextEditKernel
{
	/// <summary>
	/// Validates source-offset edits against a text snapshot and returns descending operations.
	/// </summary>
	/// <remarks>
	/// Insertions that touch a replacement range's boundaries are accepted: an insertion at a replacement's
	/// start offset is ordered before the replacement, so the inserted text lands before the replacement
	/// text, and an insertion at the replacement's end offset lands after it. Multiple insertions at one
	/// source offset are ordered so applying the list in order inserts their texts in the caller's edit
	/// order. A no-op edit (an empty range with an empty replacement text) contributes no operation, but its
	/// range is still validated first: a no-op whose range lies outside the document is rejected with the
	/// range diagnostic instead of being ignored. Pairwise conflicts are reported in a deterministic order,
	/// capped at a bounded number per batch so a pathological overlapping batch cannot produce a quadratic
	/// report; a batch with any conflict is rejected regardless of how many conflicts are listed.
	/// </remarks>
	/// <param name="snapshot">The immutable source text used for range validation.</param>
	/// <param name="edits">
	/// The edits to validate and prepare. A <see langword="null"/> entry or a <see langword="null"/>
	/// replacement text is reported as an issue instead of throwing.
	/// </param>
	/// <returns>
	/// <list type="bullet">
	/// <item>A result containing descending-offset operations when all edits are valid;</item>
	/// <item>A result with no operations and the collected issues when any edit is rejected.</item>
	/// </list>
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="snapshot"/> or <paramref name="edits"/> is <see langword="null"/>.
	/// </exception>
	public static TextEditPreparationResult Prepare(
		ITextSnapshot snapshot,
		IEnumerable<TextEditInput?> edits)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		ArgumentNullException.ThrowIfNull(edits);

		var candidates = new List<Candidate>();
		var issues = new List<TextEditPreparationIssue>();
		int editIndex = 0;

		foreach (TextEditInput? edit in edits)
		{
			TextEditPreparationIssue? invalidIssue = CreateInvalidEditIssue(edit, snapshot.TextLength, editIndex);

			if (invalidIssue is not null)
			{
				issues.Add(invalidIssue);
			}
			else if (!edit!.IsNoOp)
			{
				// An empty range with an empty replacement text changes nothing, so it is ignored
				// instead of being classified as an insertion and rejected for starting inside a
				// replacement range.
				candidates.Add(new Candidate(
					editIndex,
					edit.Range.Offset,
					edit.Range.EndOffset,
					edit.NewText!));
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

			ascendingOperations[index] = new TextEditOperation(
				candidate.StartOffset,
				candidate.EndOffset,
				candidate.NewText,
				candidate.EditIndex);
		}

		AddConflictIssues(ascendingOperations, issues);

		if (issues.Count > 0)
			return TextEditPreparationResult.Invalid(issues);

		// The application order is the reverse of the candidate order, so the highest source offset
		// applies first; the ascending array is reversed in place because it is not read again and
		// the carrier takes ownership of it.
		Array.Reverse(ascendingOperations);

		// The shared conflict detector already ran over this batch above, so the carrier is created
		// through the kernel-validated factory instead of re-detecting the same conflicts in its
		// validating constructor.
		return TextEditPreparationResult.Valid(PreparedTextEdits.CreateKernelValidated(ascendingOperations));
	}

	/// <summary>
	/// Prepares a batch in lenient mode: entries that are invalid or that conflict with an earlier
	/// accepted entry are skipped and reported instead of rejecting the whole batch.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Entries are evaluated in caller order, so the first entry is always accepted (an earlier entry
	/// always wins a conflict) and a caller that lists the entry it must apply first keeps that entry
	/// even when a later entry overlaps it. The accepted operations are ordered from highest to lowest
	/// source offset, so a caller applies them in list order without an applied entry shifting a
	/// pending one: at one source offset the operation with the highest edit index applies first,
	/// which leaves the lowest edit index leftmost in the resulting text.
	/// </para>
	/// <list type="bullet">
	/// <item>An entry is skipped when it is <see langword="null"/>, carries a <see langword="null"/>
	/// replacement text, has a range outside the snapshot, or conflicts with an earlier accepted entry
	/// under the shared range rule set (<see cref="TextEditConflictDetector.Conflicts"/>): an insertion
	/// that only touches a replacement's boundaries is accepted, an insertion strictly inside a
	/// replacement is skipped, and two replacements conflict when their ranges overlap.</item>
	/// <item>An entry that changes nothing is ignored and produces neither an operation nor an issue.</item>
	/// <item>Each skipped entry produces one issue that names the entry as
	/// <see cref="TextEditPreparationIssue.EditIndex"/> and, for a conflict, the earlier accepted
	/// entry as <see cref="TextEditPreparationIssue.RelatedEditIndex"/>.</item>
	/// <item>The acceptance walk compares each entry with the entries accepted before it, so its cost
	/// grows with the number of accepted entries; a batch in which many entries conflict with one
	/// another is quadratic. Use the strict
	/// <see cref="Prepare(ITextSnapshot, IEnumerable{TextEditInput?})"/> mode when a batch must be
	/// all-or-nothing.</item>
	/// </list>
	/// </remarks>
	/// <param name="snapshot">The immutable source text used for range validation.</param>
	/// <param name="edits">
	/// The edits to validate and prepare. A <see langword="null"/> entry or a <see langword="null"/>
	/// replacement text is skipped with an issue instead of throwing.
	/// </param>
	/// <returns>
	/// A result carrying the accepted operations and one issue per skipped entry; the batch may be
	/// empty when every entry was skipped.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="snapshot"/> or <paramref name="edits"/> is <see langword="null"/>.
	/// </exception>
	public static TextEditSkipResult PrepareSkippingInvalidEntries(
		ITextSnapshot snapshot,
		IEnumerable<TextEditInput?> edits)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		ArgumentNullException.ThrowIfNull(edits);

		var accepted = new List<TextEditOperation>();
		List<TextEditPreparationIssue>? skippedIssues = null;
		int editIndex = 0;

		foreach (TextEditInput? edit in edits)
		{
			TextEditPreparationIssue? invalidIssue = CreateInvalidEditIssue(edit, snapshot.TextLength, editIndex);

			if (invalidIssue is not null)
			{
				(skippedIssues ??= []).Add(invalidIssue);
			}
			else if (!edit!.IsNoOp)
			{
				var operation = new TextEditOperation(
					edit.Range.Offset,
					edit.Range.EndOffset,
					edit.NewText!,
					editIndex);
				TextEditOperation? conflicting = FindAcceptedConflict(operation, accepted);

				if (conflicting is null)
					accepted.Add(operation);
				else
					(skippedIssues ??= []).Add(CreateConflictIssue(operation, conflicting, editIndex));
			}

			editIndex++;
		}

		// The accepted operations are emitted in the shared application order: highest source offset
		// first, and at one offset the highest edit index first (the order key is total while edit
		// indexes are unique per batch).
		accepted.Sort(static (left, right) => TextEditOrderKey.From(right).CompareTo(TextEditOrderKey.From(left)));

		return TextEditSkipResult.Create(PreparedTextEdits.CreateKernelValidated([.. accepted]), skippedIssues ?? []);
	}

	/// <summary>
	/// Validates a hand-built batch of operations and returns the operations in application order.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is the single construction path for a batch a caller already holds as
	/// <see cref="TextEditOperation"/> values: a rejected batch is reported as preparation issues,
	/// exactly as <see cref="Prepare(ITextSnapshot, IEnumerable{TextEditInput?})"/> reports them,
	/// instead of throwing. The operations are ordered for application, so a caller does not have to
	/// supply them in order.
	/// </para>
	/// <list type="bullet">
	/// <item><see cref="TextEditOperation"/> validates its own replacement text and offsets, so the
	/// only invalid entry is a <see langword="null"/> element, reported as
	/// <see cref="TextEditPreparationIssueKind.NullEdit"/>.</item>
	/// <item>An operation that changes nothing is ignored and produces no issue.</item>
	/// <item>Conflicts follow the shared range rule set (<see cref="TextEditConflictDetector"/>):
	/// an insertion strictly inside a replacement and two overlapping replacements are reported and
	/// reject the batch.</item>
	/// <item>Ranges are not validated against a snapshot because none is supplied; use
	/// <see cref="Prepare(ITextSnapshot, IEnumerable{TextEditInput?})"/> when the ranges must be
	/// checked against a document.</item>
	/// </list>
	/// </remarks>
	/// <param name="operations">The operations to validate; they need not be ordered.</param>
	/// <returns>
	/// A result carrying the operations in descending application order when the batch is valid, or
	/// the issues that rejected it.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="operations"/> is <see langword="null"/>.</exception>
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
				issues.Add(new TextEditPreparationIssue(
					TextEditPreparationIssueKind.NullEdit,
					index,
					null,
					"The edit is null."));
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
			return TextEditPreparationResult.Invalid(issues);

		// The application order is the reverse of the candidate order, so the highest source offset
		// applies first.
		TextEditOperation[] applicationOrder = [.. candidates];
		Array.Reverse(applicationOrder);

		return TextEditPreparationResult.Valid(PreparedTextEdits.CreateKernelValidated(applicationOrder));
	}

	/// <summary>
	/// Creates the issue for an invalid edit, or <see langword="null"/> when the edit is valid.
	/// </summary>
	/// <remarks>
	/// Both preparation modes classify an edit the same way: a <see langword="null"/> entry, a
	/// <see langword="null"/> replacement text, and a range outside the document are the three invalid
	/// cases, and the range is validated before the no-op check, so an out-of-range no-op is still
	/// reported.
	/// </remarks>
	/// <param name="edit">The edit to inspect.</param>
	/// <param name="textLength">The snapshot length the edit range is validated against.</param>
	/// <param name="editIndex">The index of the edit within the caller's batch.</param>
	/// <returns>The invalid-edit issue, or <see langword="null"/> when the edit is valid.</returns>
	private static TextEditPreparationIssue? CreateInvalidEditIssue(
		TextEditInput? edit,
		int textLength,
		int editIndex)
	{
		if (edit is null)
		{
			return new TextEditPreparationIssue(
				TextEditPreparationIssueKind.NullEdit,
				editIndex,
				null,
				"The edit is null.");
		}

		if (edit.NewText is null)
		{
			return new TextEditPreparationIssue(
				TextEditPreparationIssueKind.NullReplacementText,
				editIndex,
				null,
				"The edit replacement text is null.");
		}

		if (!edit.Range.FitsWithin(textLength))
		{
			return new TextEditPreparationIssue(
				TextEditPreparationIssueKind.RangeOutOfBounds,
				editIndex,
				null,
				"The edit range is outside the document.");
		}

		return null;
	}

	/// <summary>
	/// Finds the first accepted operation that conflicts with the candidate under the shared range
	/// rule set, or <see langword="null"/> when the candidate is accepted.
	/// </summary>
	/// <param name="candidate">The candidate operation.</param>
	/// <param name="accepted">The operations accepted before the candidate, in caller order.</param>
	/// <returns>The first conflicting accepted operation, or <see langword="null"/>.</returns>
	private static TextEditOperation? FindAcceptedConflict(
		TextEditOperation candidate,
		List<TextEditOperation> accepted)
	{
		for (int index = 0; index < accepted.Count; index++)
		{
			if (TextEditConflictDetector.Conflicts(accepted[index], candidate))
				return accepted[index];
		}

		return null;
	}

	/// <summary>
	/// Creates the issue for an entry skipped because it conflicts with an earlier accepted entry.
	/// </summary>
	/// <param name="candidate">The skipped entry.</param>
	/// <param name="conflicting">The earlier accepted entry it conflicts with.</param>
	/// <param name="editIndex">The index of the skipped entry within the caller's batch.</param>
	/// <returns>The conflict issue.</returns>
	private static TextEditPreparationIssue CreateConflictIssue(
		TextEditOperation candidate,
		TextEditOperation conflicting,
		int editIndex)
	{
		// Exactly one member of a conflicting pair is an insertion (two insertions never conflict),
		// so the pair is an insertion inside a replacement when either member is an insertion and a
		// replacement overlap otherwise.
		TextEditConflictKind kind = candidate.Length == 0 || conflicting.Length == 0
			? TextEditConflictKind.InsertionInsideReplacement
			: TextEditConflictKind.ReplacementOverlap;

		return new TextEditPreparationIssue(
			MapConflictKind(kind),
			editIndex,
			conflicting.EditIndex,
			TextEditConflictMessages.GetIssueMessage(kind));
	}

	/// <summary>
	/// Bounds the pairwise conflict issues for one batch. The cap only shapes the report: a
	/// batch with any conflict is rejected regardless of how many conflicts are listed.
	/// </summary>
	private const int MaximumConflictIssueCount = 512;

	/// <summary>
	/// Adds an issue for every conflict reported by the shared conflict detector, capped so a
	/// pathological overlapping batch cannot produce a quadratic issue list.
	/// </summary>
	/// <remarks>
	/// Each issue names the edit it was detected for as <c>EditIndex</c> and the edit it
	/// conflicts with as <c>RelatedEditIndex</c>. The conflict order is documented on
	/// <see cref="TextEditConflictDetector.FindConflicts"/>. At most
	/// <see cref="MaximumConflictIssueCount"/> pairwise conflicts are reported. The shared
	/// detector is the same rule set that <see cref="PreparedTextEdits"/> validates with, so a
	/// prepared batch never fails construction.
	/// </remarks>
	private static void AddConflictIssues(
		IReadOnlyList<TextEditOperation> orderedOperations,
		List<TextEditPreparationIssue> issues)
	{
		foreach (TextEditConflict conflict in TextEditConflictDetector.FindConflictsInCandidateOrder(orderedOperations, MaximumConflictIssueCount))
		{
			issues.Add(new TextEditPreparationIssue(
				MapConflictKind(conflict.Kind),
				conflict.EditIndex,
				conflict.RelatedEditIndex,
				TextEditConflictMessages.GetIssueMessage(conflict.Kind)));
		}
	}

	/// <summary>
	/// Projects a conflict rule onto its public issue category.
	/// </summary>
	/// <param name="kind">The conflict rule reported by the shared detector.</param>
	/// <returns>The issue category that describes the conflict.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not a defined value.</exception>
	private static TextEditPreparationIssueKind MapConflictKind(TextEditConflictKind kind)
		=> kind switch
		{
			TextEditConflictKind.InsertionInsideReplacement => TextEditPreparationIssueKind.InsertionInsideReplacement,
			TextEditConflictKind.ReplacementOverlap => TextEditPreparationIssueKind.ReplacementOverlap,
			_ => throw new ArgumentOutOfRangeException(nameof(kind)),
		};

	private readonly record struct Candidate(int EditIndex, int StartOffset, int EndOffset, string NewText);

	private sealed class CandidateComparer : IComparer<Candidate>
	{
		public static CandidateComparer Instance { get; } = new();

		public int Compare(Candidate left, Candidate right)
			=> new TextEditOrderKey(left.StartOffset, left.EndOffset, left.EditIndex)
				.CompareTo(new TextEditOrderKey(right.StartOffset, right.EndOffset, right.EditIndex));
	}
}

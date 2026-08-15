using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Editing;

/// <summary>
/// Detects conflicts between text edit operations with one shared rule set, so the preparation
/// kernel (diagnostics) and <see cref="PreparedTextEdits"/> (constructor validation) cannot drift.
/// </summary>
internal static class TextEditConflictDetector
{
	/// <summary>
	/// Returns the conflicts in a batch: pairwise range conflicts in a deterministic order.
	/// Operations that change nothing are ignored.
	/// </summary>
	/// <remarks>
	/// Pairwise range conflicts are ordered by the left (replacement) operation under the candidate
	/// order and then by the right (triggering) operation, so they are not globally sorted by the
	/// triggering edit. The order is total while edit indexes are unique per batch, as the kernel
	/// guarantees; a hand-built batch that reuses an edit index leaves the relative order of the
	/// operations that carry it unspecified. At most <paramref name="maximumConflictCount"/> conflicts
	/// are collected, so a pathological overlapping batch stops reporting at the cap instead of
	/// producing a quadratic diagnostic list; the batch stays rejected regardless.
	/// </remarks>
	/// <param name="operations">The batch's operations.</param>
	/// <param name="maximumConflictCount">The maximum number of conflicts to collect.</param>
	/// <returns>The conflicts in the documented order, at most <paramref name="maximumConflictCount"/> of them.</returns>
	internal static List<TextEditConflict> FindConflicts(
		IReadOnlyList<TextEditOperation> operations,
		int maximumConflictCount = int.MaxValue)
	{
		var candidates = new List<TextEditOperation>(operations.Count);

		foreach (TextEditOperation operation in operations)
		{
			if (!operation.IsNoOp)
				candidates.Add(operation);
		}

		candidates.Sort(static (left, right) => TextEditOrderKey.From(left).CompareTo(TextEditOrderKey.From(right)));

		return CollectConflicts(candidates, maximumConflictCount);
	}

	/// <summary>
	/// Returns the conflicts in a batch whose operations the caller already ordered by
	/// <see cref="TextEditOrderKey"/> ascending and filtered to effective edits, so the preparation
	/// kernel does not filter, copy, and sort the same batch a second time.
	/// </summary>
	/// <remarks>
	/// The sequence must be exactly the candidate order of
	/// <see cref="FindConflicts(IReadOnlyList{TextEditOperation}, int)"/>: ascending by start offset,
	/// end offset, and edit index, with no <see langword="null"/> or no-op entries. The conflict
	/// order, the cap, and the pair rules are those of that overload.
	/// </remarks>
	/// <param name="sortedOperations">The effective operations in candidate order.</param>
	/// <param name="maximumConflictCount">The maximum number of conflicts to collect.</param>
	/// <returns>The conflicts in the documented order, at most <paramref name="maximumConflictCount"/> of them.</returns>
	internal static List<TextEditConflict> FindConflictsInCandidateOrder(
		IReadOnlyList<TextEditOperation> sortedOperations,
		int maximumConflictCount = int.MaxValue)
		=> CollectConflicts(sortedOperations, maximumConflictCount);

	/// <summary>
	/// Reports whether two operations conflict under the shared range rule set.
	/// </summary>
	/// <remarks>
	/// The rule is symmetric and does not depend on the argument order:
	/// <list type="bullet">
	/// <item>Two operations that change nothing never conflict, and two insertions never conflict,
	/// even at one source offset;</item>
	/// <item>An insertion conflicts only when it starts strictly inside a replacement range; an
	/// insertion that touches a replacement's boundary - including a zero-length insertion at its
	/// start or end - is accepted;</item>
	/// <item>Two replacements conflict when their ranges overlap; ranges that merely touch are
	/// accepted.</item>
	/// </list>
	/// The preparation kernel reports these conflicts as issues and
	/// <see cref="PreparedTextEdits"/> rejects them in its constructor, so both paths classify a pair
	/// the same way. The lenient preparation mode also skips a conflicting entry with this rule.
	/// </remarks>
	/// <param name="first">One operation of the pair.</param>
	/// <param name="second">The other operation of the pair.</param>
	/// <returns><see langword="true"/> when the two operations conflict.</returns>
	internal static bool Conflicts(TextEditOperation first, TextEditOperation second)
	{
		if (first.IsNoOp || second.IsNoOp)
			return false;

		bool firstIsInsertion = first.Length == 0;
		bool secondIsInsertion = second.Length == 0;

		if (firstIsInsertion)
		{
			// An insertion is accepted at a replacement's boundaries; it conflicts only when it
			// starts strictly inside one.
			return !secondIsInsertion
				&& first.StartOffset > second.StartOffset
				&& first.StartOffset < second.EndOffset;
		}

		if (secondIsInsertion)
			return second.StartOffset > first.StartOffset && second.StartOffset < first.EndOffset;

		// Two replacements conflict when their ranges overlap; touching ranges are accepted.
		return first.StartOffset < second.EndOffset && second.StartOffset < first.EndOffset;
	}

	private static List<TextEditConflict> CollectConflicts(
		IReadOnlyList<TextEditOperation> candidates,
		int maximumConflictCount)
	{
		var conflicts = new List<TextEditConflict>();

		for (int leftIndex = 0; leftIndex < candidates.Count; leftIndex++)
		{
			TextEditOperation left = candidates[leftIndex];

			// Only a replacement can start a conflicting pair. Candidates are sorted by start offset,
			// so an insertion on the left can only meet candidates at its own offset, and the
			// strict-inside test below never matches those (touching a replacement boundary is
			// accepted); skipping insertions keeps a batch of same-offset insertions linear instead
			// of quadratic.
			if (left.Length == 0)
				continue;

			for (int rightIndex = leftIndex + 1; rightIndex < candidates.Count; rightIndex++)
			{
				TextEditOperation right = candidates[rightIndex];

				// Candidates are sorted by start offset, so once the right candidate starts at or
				// after the left candidate's end, neither it nor any later candidate can conflict
				// with the left one.
				if (right.StartOffset >= left.EndOffset)
					break;

				if (Conflicts(left, right))
				{
					// The left candidate is a replacement, so a conflicting right candidate is either an
					// insertion strictly inside it or another replacement that overlaps it.
					conflicts.Add(new TextEditConflict(
						right.Length == 0 ? TextEditConflictKind.InsertionInsideReplacement : TextEditConflictKind.ReplacementOverlap,
						right.EditIndex,
						left.EditIndex));

					if (conflicts.Count >= maximumConflictCount)
						return conflicts;
				}
			}
		}

		return conflicts;
	}
}

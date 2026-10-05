using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Editing;

/// <summary>Detects conflicts between text edit operations under one shared rule set.</summary>
internal static class TextEditConflictDetector
{
	/// <summary>Returns the pairwise range conflicts in a batch in a deterministic order; operations that change nothing are ignored.</summary>
	/// <remarks>
	/// Conflicts are ordered by the left (replacement) operation under the candidate order and then by the triggering operation,
	/// so they are not globally sorted by the triggering edit. At most <paramref name="maximumConflictCount"/> are collected; the batch stays
	/// rejected regardless.
	/// </remarks>
	internal static List<TextEditConflict> FindConflicts(IReadOnlyList<TextEditOperation> operations, int maximumConflictCount = int.MaxValue)
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
	/// Returns the conflicts in a batch whose operations the caller already ordered by <see cref="TextEditOrderKey"/> ascending and filtered
	/// to effective edits, so the preparation kernel does not filter, copy, and sort the same batch a second time.
	/// </summary>
	/// <remarks>
	/// The sequence must be exactly the candidate order of <see cref="FindConflicts(IReadOnlyList{TextEditOperation}, int)"/>:
	/// ascending by start offset, end offset, and edit index, with no <see langword="null"/> or no-op entries. The conflict order, the cap,
	/// and the pair rules are those of that overload.
	/// </remarks>
	internal static List<TextEditConflict> FindConflictsInCandidateOrder(
		IReadOnlyList<TextEditOperation> sortedOperations,
		int maximumConflictCount = int.MaxValue
	) => CollectConflicts(sortedOperations, maximumConflictCount);

	/// <summary>Reports whether two operations conflict under the shared range rule set.</summary>
	/// <remarks>
	/// The rule is symmetric. Two no-ops or two insertions never conflict; an insertion conflicts only when it starts strictly inside a replacement
	/// (touching a boundary is accepted); two replacements conflict when their ranges overlap (touching ranges are accepted).
	/// </remarks>
	internal static bool Conflicts(TextEditOperation first, TextEditOperation second)
	{
		if (first.IsNoOp || second.IsNoOp)
			return false;

		TextRange firstRange = first.Range;
		TextRange secondRange = second.Range;

		if (firstRange.IsEmpty)
		{
			// An insertion is accepted at a replacement's boundaries; it conflicts only when it
			// starts strictly inside one.
			return !secondRange.IsEmpty && firstRange.Offset > secondRange.Offset && firstRange.Offset < secondRange.EndOffset;
		}

		if (secondRange.IsEmpty)
			return secondRange.Offset > firstRange.Offset && secondRange.Offset < firstRange.EndOffset;

		// Two replacements conflict when their ranges overlap; touching ranges are accepted.
		return firstRange.Offset < secondRange.EndOffset && secondRange.Offset < firstRange.EndOffset;
	}

	private static List<TextEditConflict> CollectConflicts(IReadOnlyList<TextEditOperation> candidates, int maximumConflictCount)
	{
		var conflicts = new List<TextEditConflict>();

		for (int leftIndex = 0; leftIndex < candidates.Count; leftIndex++)
		{
			TextEditOperation left = candidates[leftIndex];
			TextRange leftRange = left.Range;

			// Only a replacement can start a conflicting pair. Candidates are sorted by start offset,
			// so an insertion on the left can only meet candidates at its own offset, and the
			// strict-inside test below never matches those (touching a replacement boundary is
			// accepted); skipping insertions keeps a batch of same-offset insertions linear instead
			// of quadratic.
			if (leftRange.IsEmpty)
				continue;

			for (int rightIndex = leftIndex + 1; rightIndex < candidates.Count; rightIndex++)
			{
				TextEditOperation right = candidates[rightIndex];

				// Candidates are sorted by start offset, so once the right candidate starts at or
				// after the left candidate's end, neither it nor any later candidate can conflict
				// with the left one.
				if (right.Range.Offset >= leftRange.EndOffset)
					break;

				if (Conflicts(left, right))
				{
					// The left candidate is a replacement, so a conflicting right candidate is either an
					// insertion strictly inside it or another replacement that overlaps it.
					conflicts.Add(
						new TextEditConflict(
							right.Range.IsEmpty
								? TextEditPreparationIssueKind.InsertionInsideReplacement
								: TextEditPreparationIssueKind.ReplacementOverlap,

							right.EditIndex,
							left.EditIndex
						)
					);

					if (conflicts.Count >= maximumConflictCount)
						return conflicts;
				}
			}
		}

		return conflicts;
	}
}

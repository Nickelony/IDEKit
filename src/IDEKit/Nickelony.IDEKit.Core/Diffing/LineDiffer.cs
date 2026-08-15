using System.Buffers;

namespace Nickelony.IDEKit.Core.Diffing;

/// <summary>
/// Computes changed lines between two line-split texts using the linear-space refinement of the
/// Myers diff algorithm (divide and conquer around a middle snake).
/// </summary>
public static class LineDiffer
{
	/// <summary>
	/// Gets the one-based line numbers of <paramref name="current"/> that are not part of a longest
	/// common subsequence with <paramref name="baseline"/>; that is, the lines that were inserted or
	/// modified relative to the baseline.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Lines are compared with <see cref="StringComparison.Ordinal"/>. The caller splits the text,
	/// typically with <see cref="Text.TextLineSplitter.Split"/>; line terminators are invisible to
	/// the comparison unless the caller keeps them inside the line strings, so a CRLF-to-LF-only
	/// change reports no difference and a <c>Split('\n')</c> caller reports every line changed for
	/// CRLF input. There is deliberately no comparer overload: ordinal comparison is the contract.
	/// </para>
	/// <para>
	/// The changed set depends on which common subsequence the diff selects, so equivalent edits can
	/// mark different lines. A deletion-only edit marks no line, because the result describes
	/// <paramref name="current"/> and a removed baseline line has no current equivalent.
	/// </para>
	/// <para>
	/// The search uses memory proportional to the compared slices instead of the edit distance, so a
	/// large document with a handful of edits stays affordable. A work budget and a recursion-depth
	/// cap bound pathological inputs, and a split that cannot make progress aborts the search as
	/// well; when the search aborts, the diff reports the whole affected section as changed and sets
	/// <see cref="LineDiffResult.IsApproximate"/> to <see langword="true"/> instead of running
	/// without bound.
	/// </para>
	/// </remarks>
	/// <param name="baseline">The earlier line-split text.</param>
	/// <param name="current">The later line-split text.</param>
	/// <returns>The changed line numbers and whether the result is approximate.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="baseline"/> or <paramref name="current"/> is <see langword="null"/>.
	/// </exception>
	public static LineDiffResult GetChangedLineNumbers(IReadOnlyList<string> baseline, IReadOnlyList<string> current)
	{
		ArgumentNullException.ThrowIfNull(baseline);
		ArgumentNullException.ThrowIfNull(current);

		var changed = new HashSet<int>();
		int baselineCount = baseline.Count;
		int currentCount = current.Count;
		bool isApproximate = false;

		if (baselineCount == 0)
		{
			for (int lineNumber = 1; lineNumber <= currentCount; lineNumber++)
				changed.Add(lineNumber);
		}
		else if (currentCount > 0)
		{
			int prefix = CountCommonPrefix(baseline, current);
			int suffix = CountCommonSuffix(baseline, current, prefix);

			int middleBaselineCount = baselineCount - prefix - suffix;
			int middleCurrentCount = currentCount - prefix - suffix;

			if (middleBaselineCount == 0)
			{
				for (int i = 0; i < middleCurrentCount; i++)
					changed.Add(prefix + i + 1);
			}
			else if (middleCurrentCount > 0)
			{
				isApproximate = CollectChangedLines(
					baseline,
					current,
					new DiffSlice(prefix, middleBaselineCount, prefix, middleCurrentCount),
					changed);
			}
		}

		return LineDiffResult.Create(changed, isApproximate);
	}

	private static int CountCommonPrefix(IReadOnlyList<string> baseline, IReadOnlyList<string> current)
	{
		int count = 0;
		int limit = Math.Min(baseline.Count, current.Count);

		while (count < limit && string.Equals(baseline[count], current[count], StringComparison.Ordinal))
			count++;

		return count;
	}

	private static int CountCommonSuffix(IReadOnlyList<string> baseline, IReadOnlyList<string> current, int prefix)
	{
		int baselineIndex = baseline.Count - 1;
		int currentIndex = current.Count - 1;
		int count = 0;

		while (count < baseline.Count - prefix
			&& count < current.Count - prefix
			&& string.Equals(baseline[baselineIndex], current[currentIndex], StringComparison.Ordinal))
		{
			count++;
			baselineIndex--;
			currentIndex--;
		}

		return count;
	}

	/// <summary>
	/// Bounds the total number of search steps, character comparisons, and trimmed lines so a
	/// pathological comparison cannot run without bound. This budget is an implementation detail:
	/// the approximate fallback it triggers is the contract, not the exact budget value.
	/// </summary>
	private const int MaximumWorkUnitCount = 32 * 1024 * 1024;

	/// <summary>
	/// Bounds the recursion depth of the refinement, keeping it within the default thread stack.
	/// This cap is an implementation detail: the approximate fallback it triggers is the contract,
	/// and it is far above the depth any realistic diff reaches.
	/// </summary>
	private const int MaximumRecursionDepth = 2048;

	// A pair of aligned slice ranges into the baseline and current line lists, so a subproblem
	// carries the two ranges as one value instead of four parameters.
	private readonly record struct DiffSlice(int BaselineStart, int BaselineCount, int CurrentStart, int CurrentCount);

	// The mutable search state shared by the whole recursion: the rented scratch vectors, the
	// diagonal offset, and the work budget consumed so far. Passed by reference so every level
	// charges the same budget.
	private struct DiffSearchState
	{
		public DiffSearchState(int[] forward, int[] backward, int offset)
		{
			Forward = forward;
			Backward = backward;
			Offset = offset;
			Work = 0;
		}

		public int[] Forward { get; }
		public int[] Backward { get; }
		public int Offset { get; }
		public int Work { get; set; }
	}

	/// <summary>
	/// Collects the changed current lines in the middle slices and reports whether the work budget
	/// forced the approximate fallback.
	/// </summary>
	private static bool CollectChangedLines(
		IReadOnlyList<string> baseline,
		IReadOnlyList<string> current,
		DiffSlice slice,
		HashSet<int> changed)
	{
		// The scratch vectors are sized once for the whole middle; every recursive subproblem is a
		// sub-slice of it and reuses the same storage, so the diff keeps linear space. They are
		// rented from the pool because a large changed span pushes each vector past the large-object
		// heap threshold (about 10.6k lines per side) and a per-call allocation would then cost a
		// generation-2 collection. Every diagonal is written before it is read (the search seeds
		// `offset + 1` and each cost step only reads diagonals written by the previous step), so
		// stale entries from a previous rent are never observed.
		int maxD = ((slice.BaselineCount + slice.CurrentCount + 1) / 2) + 1;
		int[] forward = ArrayPool<int>.Shared.Rent((2 * maxD) + 1);
		int[] backward = ArrayPool<int>.Shared.Rent((2 * maxD) + 1);
		var state = new DiffSearchState(forward, backward, maxD);

		bool completed;

		try
		{
			completed = TryCollectChangedLines(baseline, current, slice, ref state, changed, depth: 0);
		}
		finally
		{
			ArrayPool<int>.Shared.Return(forward);
			ArrayPool<int>.Shared.Return(backward);
		}

		if (completed)
			return false;

		// The budget was exceeded; report the whole affected section as changed.
		MarkCurrentLines(changed, slice.CurrentStart, slice.CurrentCount);
		return true;
	}

	/// <summary>
	/// Recursively aligns one slice pair around its middle snake and marks the current lines that
	/// are not part of the common subsequence. Returns <see langword="false"/> when the work budget
	/// or the recursion-depth cap is exhausted.
	/// </summary>
	private static bool TryCollectChangedLines(
		IReadOnlyList<string> baseline,
		IReadOnlyList<string> current,
		DiffSlice slice,
		ref DiffSearchState state,
		HashSet<int> changed,
		int depth)
	{
		if (state.Work > MaximumWorkUnitCount || depth > MaximumRecursionDepth)
			return false;

		int baselineStart = slice.BaselineStart;
		int baselineCount = slice.BaselineCount;
		int currentStart = slice.CurrentStart;
		int currentCount = slice.CurrentCount;

		// Trim the matched prefix and suffix of this subproblem; matched lines are never marked.
		// The trim comparisons are part of the work budget, so a long run of shared lines cannot
		// bypass the bound through the per-level trims.
		int prefix = 0;
		int prefixLimit = Math.Min(baselineCount, currentCount);

		while (prefix < prefixLimit
			&& string.Equals(
				baseline[baselineStart + prefix],
				current[currentStart + prefix],
				StringComparison.Ordinal))
		{
			prefix++;
		}

		baselineStart += prefix;
		baselineCount -= prefix;
		currentStart += prefix;
		currentCount -= prefix;

		int suffix = 0;
		int suffixLimit = Math.Min(baselineCount, currentCount);

		while (suffix < suffixLimit
			&& string.Equals(
				baseline[baselineStart + baselineCount - 1 - suffix],
				current[currentStart + currentCount - 1 - suffix],
				StringComparison.Ordinal))
		{
			suffix++;
		}

		state.Work += prefix + suffix;
		baselineCount -= suffix;
		currentCount -= suffix;

		if (state.Work > MaximumWorkUnitCount)
			return false;

		if (baselineCount == 0)
		{
			MarkCurrentLines(changed, currentStart, currentCount);
			return true;
		}

		if (currentCount == 0)
			return true;

		var trimmed = new DiffSlice(baselineStart, baselineCount, currentStart, currentCount);
		if (!FindMiddleSnake(baseline, current, trimmed, ref state, out int splitBaseline, out int splitCurrent))
			return false;

		// A split that makes no progress on either side would recurse forever; treat it as a budget
		// abort so the caller falls back to the approximate result.
		if ((splitBaseline <= baselineStart && splitCurrent <= currentStart)
			|| (splitBaseline >= baselineStart + baselineCount && splitCurrent >= currentStart + currentCount))
		{
			return false;
		}

		var left = new DiffSlice(
			baselineStart,
			splitBaseline - baselineStart,
			currentStart,
			splitCurrent - currentStart);
		if (!TryCollectChangedLines(baseline, current, left, ref state, changed, depth + 1))
			return false;

		var right = new DiffSlice(
			splitBaseline,
			baselineStart + baselineCount - splitBaseline,
			splitCurrent,
			currentStart + currentCount - splitCurrent);
		return TryCollectChangedLines(baseline, current, right, ref state, changed, depth + 1);
	}

	/// <summary>
	/// Finds the middle snake of the slice pair: the endpoints of a central diagonal run on an
	/// optimal edit path, found by searching forward from the start and backward from the end until
	/// the furthest-reaching paths overlap.
	/// </summary>
	/// <remarks>
	/// The vectors are indexed by diagonal <c>k = x - y</c> from <c>-offset</c> to <c>offset</c>. The
	/// forward vector stores the furthest <c>x</c> reached by a path of the current cost on each
	/// diagonal; the backward vector stores the furthest <c>x</c> reached from the end in reversed
	/// coordinates. Only the diagonals of the current cost hold fresh values, so entries of the
	/// other parity carry the previous cost, which the recurrence reads.
	/// </remarks>
	private static bool FindMiddleSnake(
		IReadOnlyList<string> baseline,
		IReadOnlyList<string> current,
		DiffSlice slice,
		ref DiffSearchState state,
		out int splitBaseline,
		out int splitCurrent)
	{
		int baselineStart = slice.BaselineStart;
		int currentStart = slice.CurrentStart;
		int n = slice.BaselineCount;
		int m = slice.CurrentCount;
		int delta = n - m;
		bool odd = (delta & 1) != 0;
		int maxD = ((n + m + 1) / 2) + 1;
		int offset = state.Offset;
		int[] forward = state.Forward;
		int[] backward = state.Backward;

		// The search starts from the points (0, -1) and (n, m + 1).
		forward[offset + 1] = 0;
		backward[offset + 1] = 0;

		for (int d = 0; d < maxD; d++)
		{
			state.Work += d + 1;

			if (state.Work > MaximumWorkUnitCount)
			{
				splitBaseline = 0;
				splitCurrent = 0;
				return false;
			}

			// Forward search from the start.
			for (int k = -d; k <= d; k += 2)
			{
				int x = k == -d || (k != d && forward[offset + k - 1] < forward[offset + k + 1])
					? forward[offset + k + 1]
					: forward[offset + k - 1] + 1;
				int y = x - k;
				int snakeStartX = x;

				while (x < n && y < m
					&& string.Equals(
						baseline[baselineStart + x],
						current[currentStart + y],
						StringComparison.Ordinal))
				{
					x++;
					y++;
				}

				state.Work += x - snakeStartX;
				forward[offset + k] = x;

				// Only an odd-length optimal path can be found from the forward side, and only when
				// the reciprocal backward diagonal has been visited.
				if (odd
					&& Math.Abs(k - delta) <= d - 1
					&& forward[offset + k] + backward[offset - (k - delta)] >= n)
				{
					splitBaseline = baselineStart + snakeStartX;
					splitCurrent = currentStart + snakeStartX - k;
					return true;
				}
			}

			// Backward search from the end.
			for (int k = -d; k <= d; k += 2)
			{
				int x = k == -d || (k != d && backward[offset + k - 1] < backward[offset + k + 1])
					? backward[offset + k + 1]
					: backward[offset + k - 1] + 1;
				int y = x - k;
				int snakeStartX = x;

				while (x < n && y < m
					&& string.Equals(
						baseline[baselineStart + n - 1 - x],
						current[currentStart + m - 1 - y],
						StringComparison.Ordinal))
				{
					x++;
					y++;
				}

				state.Work += x - snakeStartX;
				backward[offset + k] = x;

				if (!odd
					&& Math.Abs(k - delta) <= d
					&& backward[offset + k] + forward[offset - (k - delta)] >= n)
				{
					splitBaseline = baselineStart + n - x;
					splitCurrent = currentStart + m - y;
					return true;
				}
			}
		}

		splitBaseline = 0;
		splitCurrent = 0;
		return false;
	}

	/// <summary>
	/// Marks every current line of the supplied slice as changed.
	/// </summary>
	private static void MarkCurrentLines(HashSet<int> changed, int currentStart, int currentCount)
	{
		for (int index = 0; index < currentCount; index++)
			changed.Add(currentStart + index + 1);
	}
}

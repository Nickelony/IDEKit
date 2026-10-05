using System.Buffers;

namespace Nickelony.IDEKit.Core.Diffing;

/// <summary>
/// Computes changed lines between two line-split texts using the linear-space refinement of the Myers diff algorithm (divide and conquer
/// around a middle snake).
/// </summary>
public static class LineDiffer
{
	/// <summary>
	/// Returns the one-based line numbers of <paramref name="current"/> that are not part of a longest common
	/// subsequence with <paramref name="baseline"/>; that is, the lines that were inserted or modified relative to the baseline.
	/// </summary>
	/// <param name="baseline">The earlier line-split text.</param>
	/// <param name="current">The later line-split text.</param>
	/// <returns>The changed line numbers and whether the result is approximate.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="baseline"/> or <paramref name="current"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// Lines are compared with <see cref="StringComparison.Ordinal"/> and there is no comparer overload; a line terminator takes part
	/// in the comparison only when the caller's splitter leaves it on the line. The changed set depends on which common subsequence the diff selects,
	/// and a deletion-only edit marks no line. The search uses memory proportional to the compared slices; a work budget, a depth cap,
	/// or a non-progressing split yields the whole affected section marked approximate.
	/// </remarks>
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

					slice: new DiffSlice(
						BaselineStart: prefix,
						BaselineCount: middleBaselineCount,
						CurrentStart: prefix,
						CurrentCount: middleCurrentCount
					),

					changed
				);
			}
		}

		return LineDiffResult.Create(changed, isApproximate);
	}

	/// <summary>Returns the runs of lines that differ between <paramref name="baseline"/> and <paramref name="current"/>, in line order.</summary>
	/// <param name="baseline">The earlier line-split text.</param>
	/// <param name="current">The later line-split text.</param>
	/// <returns>The hunks; an empty list when the texts are equal.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="baseline"/> or <paramref name="current"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// The hunks follow the common subsequence that <see cref="GetChangedLineNumbers"/> selects, so a hunk is the run between two matched lines,
	/// and two hunks never touch. A section that the bounded search gave up on is one hunk over the whole section.
	/// </remarks>
	public static IReadOnlyList<LineDiffHunk> GetHunks(IReadOnlyList<string> baseline, IReadOnlyList<string> current)
	{
		ArgumentNullException.ThrowIfNull(baseline);
		ArgumentNullException.ThrowIfNull(current);

		int prefix = CountCommonPrefix(baseline, current);
		int suffix = CountCommonSuffix(baseline, current, prefix);

		var matches = new List<(int Baseline, int Current)>();

		for (int index = 0; index < prefix; index++)
			matches.Add((index, index));

		var middle = new DiffSlice(
			BaselineStart: prefix,
			BaselineCount: baseline.Count - prefix - suffix,
			CurrentStart: prefix,
			CurrentCount: current.Count - prefix - suffix
		);

		if (middle.BaselineCount > 0 && middle.CurrentCount > 0)
			CollectMatches(baseline, current, slice: middle, matches);

		for (int index = suffix; index > 0; index--)
			matches.Add((baseline.Count - index, current.Count - index));

		matches.Sort();

		var hunks = new List<LineDiffHunk>();

		int baselineIndex = 0;
		int currentIndex = 0;

		foreach ((int matchedBaseline, int matchedCurrent) in matches)
		{
			if (matchedBaseline > baselineIndex || matchedCurrent > currentIndex)
			{
				hunks.Add(
					new LineDiffHunk(
						BaselineStart: baselineIndex,
						BaselineCount: matchedBaseline - baselineIndex,
						CurrentStart: currentIndex,
						CurrentCount: matchedCurrent - currentIndex
					)
				);
			}

			baselineIndex = matchedBaseline + 1;
			currentIndex = matchedCurrent + 1;
		}

		if (baselineIndex < baseline.Count || currentIndex < current.Count)
		{
			hunks.Add(
				new LineDiffHunk(
					BaselineStart: baselineIndex,
					BaselineCount: baseline.Count - baselineIndex,
					CurrentStart: currentIndex,
					CurrentCount: current.Count - currentIndex
				)
			);
		}

		return hunks;
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

		while (
			count < baseline.Count - prefix
			&& count < current.Count - prefix
			&& string.Equals(baseline[baselineIndex], current[currentIndex], StringComparison.Ordinal)
		)
		{
			count++;
			baselineIndex--;
			currentIndex--;
		}

		return count;
	}

	/// <summary>
	/// Bounds the total number of search steps, line comparisons, and trimmed lines so a pathological comparison cannot run without bound.
	/// </summary>
	private const int MaximumWorkUnitCount = 32 * 1024 * 1024;

	/// <summary>Bounds the recursion depth of the refinement, keeping it within the default thread stack.</summary>
	private const int MaximumRecursionDepth = 2048;

	// A pair of aligned slice ranges into the baseline and current line lists, so a subproblem
	// carries the aligned ranges as one value instead of as separate parameters.
	private readonly record struct DiffSlice(int BaselineStart, int BaselineCount, int CurrentStart, int CurrentCount);

	// The mutable search state shared by the whole recursion: the rented scratch vectors, the
	// diagonal offset, and the work budget consumed so far. Passed by reference so every level
	// charges the same budget.
	private struct DiffSearchState
	{
		internal DiffSearchState(int[] forward, int[] backward, int offset)
		{
			Forward = forward;
			Backward = backward;
			Offset = offset;
			Work = 0;
		}

		internal int[] Forward { get; }
		internal int[] Backward { get; }
		internal int Offset { get; }
		internal int Work { get; set; }
	}

	/// <summary>
	/// Collects the changed current lines in the middle slice and reports whether the search gave up, in which case the whole slice
	/// is marked changed.
	/// </summary>
	private static bool CollectChangedLines(IReadOnlyList<string> baseline, IReadOnlyList<string> current, DiffSlice slice, HashSet<int> changed)
	{
		// The scratch vectors are sized once for the whole middle; every recursive subproblem is a sub-slice
		// of it and reuses the same storage, so the diff keeps linear space. They are rented from the pool
		// because a large changed span pushes each vector past the large-object heap threshold (about 10.6k
		// lines per side), and a per-call allocation would then cost a generation-2 collection. Every diagonal
		// is written before it is read (the search seeds `offset + 1`, and each cost step reads only diagonals
		// written by the previous step), so stale entries from a previous rent are never observed.
		int maxD = ((slice.BaselineCount + slice.CurrentCount + 1) / 2) + 1;

		int[] forward = ArrayPool<int>.Shared.Rent((2 * maxD) + 1);
		int[] backward = ArrayPool<int>.Shared.Rent((2 * maxD) + 1);

		var state = new DiffSearchState(forward, backward, maxD);

		bool completed;

		try
		{
			completed = TryCollectChangedLines(baseline, current, slice, ref state, changed, matches: null, depth: 0);
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

	// Collects the matched line pairs of the middle slice, so the hunks are the runs between them. A search that gives up keeps no match of the
	// slice, so the whole slice becomes one hunk.
	private static void CollectMatches(
		IReadOnlyList<string> baseline,
		IReadOnlyList<string> current,
		DiffSlice slice,
		List<(int Baseline, int Current)> matches
	)
	{
		int maxD = ((slice.BaselineCount + slice.CurrentCount + 1) / 2) + 1;

		int[] forward = ArrayPool<int>.Shared.Rent((2 * maxD) + 1);
		int[] backward = ArrayPool<int>.Shared.Rent((2 * maxD) + 1);

		var state = new DiffSearchState(forward, backward, maxD);
		int matchedBefore = matches.Count;

		try
		{
			if (!TryCollectChangedLines(baseline, current, slice, ref state, changed: null, matches, depth: 0))
				matches.RemoveRange(matchedBefore, matches.Count - matchedBefore);
		}
		finally
		{
			ArrayPool<int>.Shared.Return(forward);
			ArrayPool<int>.Shared.Return(backward);
		}
	}

	/// <summary>
	/// Recursively aligns one slice pair around its middle snake, marks the current lines that are not part of the common subsequence and records
	/// the matched pairs, each when its collector is supplied; returns <see langword="false"/> when the work budget or the recursion-depth
	/// cap is exhausted, or when a split makes no progress.
	/// </summary>
	private static bool TryCollectChangedLines(
		IReadOnlyList<string> baseline,
		IReadOnlyList<string> current,
		DiffSlice slice,
		ref DiffSearchState state,
		HashSet<int>? changed,
		List<(int Baseline, int Current)>? matches,
		int depth
	)
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

		while (prefix < prefixLimit && string.Equals(baseline[baselineStart + prefix], current[currentStart + prefix], StringComparison.Ordinal))
			prefix++;

		for (int index = 0; index < prefix; index++)
			matches?.Add((baselineStart + index, currentStart + index));

		baselineStart += prefix;
		baselineCount -= prefix;
		currentStart += prefix;
		currentCount -= prefix;

		int suffix = 0;
		int suffixLimit = Math.Min(baselineCount, currentCount);

		while (
			suffix < suffixLimit
			&& string.Equals(
				baseline[baselineStart + baselineCount - 1 - suffix],
				current[currentStart + currentCount - 1 - suffix],
				StringComparison.Ordinal
			)
		)
		{
			suffix++;
		}

		for (int index = 1; index <= suffix; index++)
			matches?.Add((baselineStart + baselineCount - index, currentStart + currentCount - index));

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

		if (!FindMiddleSnake(baseline, current, slice: trimmed, ref state, out int splitBaseline, out int splitCurrent))
			return false;

		// A split that makes no progress on either side would recurse forever; treat it as a budget
		// abort so the caller falls back to the approximate result.
		if (
			(splitBaseline <= baselineStart && splitCurrent <= currentStart)
			|| (splitBaseline >= baselineStart + baselineCount && splitCurrent >= currentStart + currentCount)
		)
		{
			return false;
		}

		var left = new DiffSlice(
			baselineStart,
			BaselineCount: splitBaseline - baselineStart,
			currentStart,
			CurrentCount: splitCurrent - currentStart
		);

		if (!TryCollectChangedLines(baseline, current, slice: left, ref state, changed, matches, depth: depth + 1))
			return false;

		var right = new DiffSlice(
			BaselineStart: splitBaseline,
			BaselineCount: baselineStart + baselineCount - splitBaseline,
			CurrentStart: splitCurrent,
			CurrentCount: currentStart + currentCount - splitCurrent
		);

		return TryCollectChangedLines(baseline, current, slice: right, ref state, changed, matches, depth: depth + 1);
	}

	/// <summary>
	/// Finds the middle snake of the slice pair: the endpoints of a central diagonal run on an optimal edit path, found by searching forward
	/// from the start and backward from the end until the furthest-reaching paths overlap.
	/// </summary>
	/// <remarks>
	/// The vectors are indexed by diagonal <c>k = x - y</c> from <c>-offset</c> to <c>offset</c>: the forward vector stores the furthest
	/// <c>x</c> reached by a path of the current cost, and the backward vector the furthest <c>x</c> reached from the end in reversed coordinates.
	/// Only the current cost's diagonals hold fresh values, so the other parity carries the previous cost, which the recurrence reads.
	/// </remarks>
	private static bool FindMiddleSnake(
		IReadOnlyList<string> baseline,
		IReadOnlyList<string> current,
		DiffSlice slice,
		ref DiffSearchState state,
		out int splitBaseline,
		out int splitCurrent
	)
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
				int x =
					k == -d || (k != d && forward[offset + k - 1] < forward[offset + k + 1]) ? forward[offset + k + 1] : forward[offset + k - 1] + 1;

				int y = x - k;
				int snakeStartX = x;

				while (x < n && y < m && string.Equals(baseline[baselineStart + x], current[currentStart + y], StringComparison.Ordinal))
				{
					x++;
					y++;
				}

				state.Work += x - snakeStartX;
				forward[offset + k] = x;

				// Only an odd-length optimal path can be found from the forward side, and only when
				// the reciprocal backward diagonal has been visited.
				if (odd && Math.Abs(k - delta) <= d - 1 && forward[offset + k] + backward[offset - (k - delta)] >= n)
				{
					splitBaseline = baselineStart + snakeStartX;
					splitCurrent = currentStart + snakeStartX - k;
					return true;
				}
			}

			// Backward search from the end.
			for (int k = -d; k <= d; k += 2)
			{
				int x =
					k == -d || (k != d && backward[offset + k - 1] < backward[offset + k + 1])
						? backward[offset + k + 1]
						: backward[offset + k - 1] + 1;

				int y = x - k;
				int snakeStartX = x;

				while (
					x < n && y < m && string.Equals(baseline[baselineStart + n - 1 - x], current[currentStart + m - 1 - y], StringComparison.Ordinal)
				)
				{
					x++;
					y++;
				}

				state.Work += x - snakeStartX;
				backward[offset + k] = x;

				if (!odd && Math.Abs(k - delta) <= d && backward[offset + k] + forward[offset - (k - delta)] >= n)
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

	/// <summary>Marks every current line of the supplied slice as changed.</summary>
	private static void MarkCurrentLines(HashSet<int>? changed, int currentStart, int currentCount)
	{
		for (int index = 0; index < currentCount; index++)
			changed?.Add(currentStart + index + 1);
	}
}

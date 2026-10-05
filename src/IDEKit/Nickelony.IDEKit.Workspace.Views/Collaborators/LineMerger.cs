using System.Diagnostics.CodeAnalysis;
using System.Text;
using Nickelony.IDEKit.Core.Diffing;
using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Workspace.Views;

/// <summary>Merges the typed text of a view and the text of the store by line hunks against the text the view last synchronized.</summary>
/// <remarks>
/// <para>
/// Each side's change is the list of line hunks that <see cref="LineDiffer.GetHunks"/> finds against the base, with the line terminators
/// compared as part of the lines. A hunk that overlaps no hunk of the other side is applied, and two sides that change one region to the same lines
/// change it once. Hunks overlap when they replace a common base line, insert at the same point, or one inserts strictly inside the lines
/// the other replaces; an insertion at the edge of a replaced run lands beside it.
/// </para>
/// <para>
/// Only a region whose two sides differ conflicts. A resolution takes one side of each conflicting region and keeps every other hunk of both sides.
/// </para>
/// </remarks>
internal static class LineMerger
{
	/// <summary>Merges the two changes, unless a region conflicts.</summary>
	/// <param name="baseText">The text the view last synchronized.</param>
	/// <param name="viewText">The text the view shows.</param>
	/// <param name="storeText">The text the store holds.</param>
	/// <param name="merged">Receives the merged text, or <see langword="null"/> when a region conflicts.</param>
	internal static bool TryMerge(string baseText, string viewText, string storeText, [NotNullWhen(true)] out string? merged)
	{
		merged = Merge(baseText, viewText, storeText, keepsViewSide: null);
		return merged is not null;
	}

	/// <summary>Merges the two changes and takes one side of each conflicting region.</summary>
	/// <param name="baseText">The text the view last synchronized.</param>
	/// <param name="viewText">The text the view shows.</param>
	/// <param name="storeText">The text the store holds.</param>
	/// <param name="keepsViewSide">
	/// <see langword="true"/> to take the view's side of a conflicting region; <see langword="false"/> to take the store's.
	/// </param>
	internal static string Resolve(string baseText, string viewText, string storeText, bool keepsViewSide) =>
		Merge(baseText, viewText, storeText, keepsViewSide) ?? throw new InvalidOperationException("A resolution always takes a side.");

	// Walks the hunks of both sides in base order, grows a region while a hunk of either side overlaps it, and writes the base lines between the
	// regions. Returns null at the first conflicting region when no side is chosen.
	private static string? Merge(string baseText, string viewText, string storeText, bool? keepsViewSide)
	{
		string[] baseLines = TextLineSplitter.SplitIncludingTerminators(baseText);
		string[] viewLines = TextLineSplitter.SplitIncludingTerminators(viewText);
		string[] storeLines = TextLineSplitter.SplitIncludingTerminators(storeText);

		IReadOnlyList<LineDiffHunk> viewHunks = LineDiffer.GetHunks(baseLines, viewLines);
		IReadOnlyList<LineDiffHunk> storeHunks = LineDiffer.GetHunks(baseLines, storeLines);

		var merged = new StringBuilder(Math.Max(viewText.Length, storeText.Length));

		int baseIndex = 0;
		int view = 0;
		int store = 0;

		while (view < viewHunks.Count || store < storeHunks.Count)
		{
			int firstView = view;
			int firstStore = store;

			LineDiffHunk first;

			if (store >= storeHunks.Count || (view < viewHunks.Count && Precedes(viewHunks[view], storeHunks[store])))
				first = viewHunks[view++];
			else
				first = storeHunks[store++];

			int regionStart = first.BaselineStart;
			int regionEnd = first.BaselineStart + first.BaselineCount;

			for (bool grew = true; grew; )
			{
				grew = false;

				if (view < viewHunks.Count && Overlaps(regionStart, regionEnd, viewHunks[view]))
				{
					regionEnd = Math.Max(regionEnd, viewHunks[view].BaselineStart + viewHunks[view].BaselineCount);
					view++;
					grew = true;
				}

				if (store < storeHunks.Count && Overlaps(regionStart, regionEnd, storeHunks[store]))
				{
					regionEnd = Math.Max(regionEnd, storeHunks[store].BaselineStart + storeHunks[store].BaselineCount);
					store++;
					grew = true;
				}
			}

			AppendLines(text: merged, lines: baseLines, start: baseIndex, end: regionStart);

			string viewSide = Apply(baseLines, regionStart, regionEnd, sideLines: viewLines, hunks: viewHunks, from: firstView, to: view);
			string storeSide = Apply(baseLines, regionStart, regionEnd, sideLines: storeLines, hunks: storeHunks, from: firstStore, to: store);

			if (store == firstStore || string.Equals(viewSide, storeSide, StringComparison.Ordinal))
				merged.Append(viewSide);
			else if (view == firstView)
				merged.Append(storeSide);
			else if (keepsViewSide is { } keepsView)
				merged.Append(keepsView ? viewSide : storeSide);
			else
				return null;

			baseIndex = regionEnd;
		}

		AppendLines(text: merged, lines: baseLines, start: baseIndex, end: baseLines.Length);
		return merged.ToString();
	}

	// An insertion at a point comes before a run that starts there, so the run's region does not swallow it.
	private static bool Precedes(LineDiffHunk candidate, LineDiffHunk other)
	{
		return candidate.BaselineStart < other.BaselineStart
			|| (candidate.BaselineStart == other.BaselineStart && candidate.BaselineCount <= other.BaselineCount);
	}

	private static bool Overlaps(int regionStart, int regionEnd, LineDiffHunk hunk)
	{
		int hunkStart = hunk.BaselineStart;
		int hunkEnd = hunk.BaselineStart + hunk.BaselineCount;

		if (regionStart == regionEnd)
			return hunkStart == hunkEnd ? hunkStart == regionStart : hunkStart < regionStart && regionStart < hunkEnd;

		return hunkStart == hunkEnd ? regionStart < hunkStart && hunkStart < regionEnd : hunkStart < regionEnd && regionStart < hunkEnd;
	}

	// Writes the base lines of the region with the side's hunks of the region applied.
	private static string Apply(
		string[] baseLines,
		int regionStart,
		int regionEnd,
		string[] sideLines,
		IReadOnlyList<LineDiffHunk> hunks,
		int from,
		int to
	)
	{
		var side = new StringBuilder();
		int baseIndex = regionStart;

		for (int index = from; index < to; index++)
		{
			LineDiffHunk hunk = hunks[index];

			AppendLines(text: side, lines: baseLines, start: baseIndex, end: hunk.BaselineStart);
			AppendLines(text: side, lines: sideLines, start: hunk.CurrentStart, end: hunk.CurrentStart + hunk.CurrentCount);

			baseIndex = hunk.BaselineStart + hunk.BaselineCount;
		}

		AppendLines(text: side, lines: baseLines, start: baseIndex, end: regionEnd);
		return side.ToString();
	}

	private static void AppendLines(StringBuilder text, string[] lines, int start, int end)
	{
		for (int index = start; index < end; index++)
			text.Append(lines[index]);
	}
}

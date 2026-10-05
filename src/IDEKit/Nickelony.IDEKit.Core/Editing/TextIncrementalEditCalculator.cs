using Nickelony.IDEKit.Core.Diffing;
using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Editing;

/// <summary>Computes minimal document changes: one range by common-prefix and common-suffix scans, or one range per changed run of lines.</summary>
public static class TextIncrementalEditCalculator
{
	/// <summary>
	/// The largest number of lines, on either side, that <see cref="ComputeHunkEdits"/> compares; a text whose changed lines exceed it gets
	/// the single range of <see cref="Compute"/> instead.
	/// </summary>
	public const int MaximumDiffLineCount = 100_000;

	/// <summary>Computes the minimal edits that transform the old content into the new content, one per changed run of lines.</summary>
	/// <param name="oldText">The old document content; <see langword="null"/> is treated as empty.</param>
	/// <param name="newText">The updated document content; <see langword="null"/> is treated as empty.</param>
	/// <returns>The edits in ascending order of their ranges in the old content, which never overlap; an empty list for equal contents.</returns>
	/// <remarks>
	/// <para>
	/// The changed region is the whole lines around the range of <see cref="Compute"/>. A change inside one line is that single edit.
	/// Otherwise a region with as many lines on both sides pairs its lines in order and gets one edit for each pair that differs,
	/// which keeps the cost linear for the common change that rewrites lines in place, such as a conversion of the terminators or of the indentation.
	/// A region with another number of lines is compared line by line without the terminators, and each run of lines that differs gets one edit,
	/// or one edit per pair when both sides of the run have as many lines. Each edit is trimmed to the characters that differ. An editor that applies
	/// the edits as one batch keeps every position outside them on its text, which one edit over the whole region cannot do.
	/// </para>
	/// <para>
	/// The line comparison is the bounded search of <see cref="LineDiffer"/>, and a region of more than <see cref="MaximumDiffLineCount"/> lines
	/// on either side is not compared at all: it is the single range of <see cref="Compute"/>. A range never splits a CRLF or a surrogate pair.
	/// </para>
	/// </remarks>
	public static IReadOnlyList<TextIncrementalEdit> ComputeHunkEdits(string? oldText, string? newText)
	{
		oldText ??= string.Empty;
		newText ??= string.Empty;

		TextIncrementalEdit span = Compute(oldText, newText);

		if (span.Range.Length == 0 && span.NewText.Length == 0)
			return [];

		// A change that holds no line terminator on either side leaves the lines as they are.
		if (
			!LineTerminators.ContainsTerminator(oldText.AsSpan(span.Range.Offset, span.Range.Length))
			&& !LineTerminators.ContainsTerminator(span.NewText)
		)
		{
			return [span];
		}

		int regionStart = StartOfLine(oldText, span.Range.Offset);
		int oldRegionEnd = EndOfLine(oldText, span.Range.EndOffset);
		int newRegionEnd = oldRegionEnd + (newText.Length - oldText.Length);

		string oldRegion = oldText[regionStart..oldRegionEnd];
		string newRegion = newText[regionStart..newRegionEnd];

		var oldLines = LineLayout.Create(oldRegion);
		var newLines = LineLayout.Create(newRegion);

		if (oldLines.Count > MaximumDiffLineCount || newLines.Count > MaximumDiffLineCount)
			return [span];

		List<TextIncrementalEdit> edits = [];

		if (oldLines.Count == newLines.Count)
		{
			for (int index = 0; index < oldLines.Count; index++)
				AddRunEdit(edits, regionStart, oldRegion, oldLines, oldFirst: index, oldCount: 1, newRegion, newLines, newFirst: index, newCount: 1);

			return edits;
		}

		IReadOnlyList<LineDiffHunk> hunks = LineDiffer.GetHunks(oldLines.Contents, newLines.Contents);

		int oldIndex = 0;
		int newIndex = 0;

		foreach (LineDiffHunk hunk in hunks)
		{
			while (oldIndex < hunk.BaselineStart)
				AddTerminatorEdit(edits, regionStart, oldRegion, oldLines, oldIndex: oldIndex++, newRegion, newLines, newIndex: newIndex++);

			if (hunk.BaselineCount == hunk.CurrentCount)
			{
				for (int offset = 0; offset < hunk.BaselineCount; offset++)
				{
					AddRunEdit(
						edits,
						regionStart,
						oldRegion,
						oldLines,
						oldFirst: hunk.BaselineStart + offset,
						oldCount: 1,
						newRegion,
						newLines,
						newFirst: hunk.CurrentStart + offset,
						newCount: 1
					);
				}
			}
			else
			{
				AddRunEdit(
					edits,
					regionStart,
					oldRegion,
					oldLines,
					oldFirst: hunk.BaselineStart,
					oldCount: hunk.BaselineCount,
					newRegion,
					newLines,
					newFirst: hunk.CurrentStart,
					newCount: hunk.CurrentCount
				);
			}

			oldIndex = hunk.BaselineStart + hunk.BaselineCount;
			newIndex = hunk.CurrentStart + hunk.CurrentCount;
		}

		while (oldIndex < oldLines.Count)
			AddTerminatorEdit(edits, regionStart, oldRegion, oldLines, oldIndex: oldIndex++, newRegion, newLines, newIndex: newIndex++);

		return edits;
	}

	private static void AddTerminatorEdit(
		List<TextIncrementalEdit> edits,
		int regionStart,
		string oldRegion,
		LineLayout oldLines,
		int oldIndex,
		string newRegion,
		LineLayout newLines,
		int newIndex
	)
	{
		ReadOnlySpan<char> oldTerminator = oldRegion.AsSpan(
			oldLines.Starts[oldIndex] + oldLines.ContentLengths[oldIndex],
			oldLines.TerminatorLength(oldIndex, oldRegion.Length)
		);

		ReadOnlySpan<char> newTerminator = newRegion.AsSpan(
			newLines.Starts[newIndex] + newLines.ContentLengths[newIndex],
			newLines.TerminatorLength(newIndex, newRegion.Length)
		);

		if (!oldTerminator.SequenceEqual(newTerminator))
		{
			int offset = regionStart + oldLines.Starts[oldIndex] + oldLines.ContentLengths[oldIndex];
			edits.Add(new TextIncrementalEdit(new TextRange(offset, oldTerminator.Length), newTerminator.ToString()));
		}
	}

	// Replaces the run of old lines with the run of new lines, trimmed to the characters that differ.
	private static void AddRunEdit(
		List<TextIncrementalEdit> edits,
		int regionStart,
		string oldRegion,
		LineLayout oldLines,
		int oldFirst,
		int oldCount,
		string newRegion,
		LineLayout newLines,
		int newFirst,
		int newCount
	)
	{
		(int oldStart, int oldEnd) = oldLines.Bounds(oldFirst, oldCount, oldRegion.Length);
		(int newStart, int newEnd) = newLines.Bounds(newFirst, newCount, newRegion.Length);
		TextIncrementalEdit change = Compute(oldRegion[oldStart..oldEnd], newRegion[newStart..newEnd]);

		if (change.Range.Length == 0 && change.NewText.Length == 0)
			return;

		edits.Add(new TextIncrementalEdit(new TextRange(regionStart + oldStart + change.Range.Offset, change.Range.Length), change.NewText));
	}

	// The offset of the first character of the line that holds the offset.
	private static int StartOfLine(string text, int offset)
	{
		int index = offset;

		while (index > 0 && !LineTerminators.IsTerminator(text[index - 1]))
			index--;

		return index;
	}

	// The offset behind the line terminator of the line that holds the offset, or the end of the text.
	private static int EndOfLine(string text, int offset)
	{
		int found = text.AsSpan(offset).IndexOfAny('\r', '\n');

		if (found < 0)
			return text.Length;

		int index = offset + found;

		return index + LineTerminators.GetTerminatorLength(text, index);
	}

	// The lines of a text as their contents without terminators, with the offset of each line and the length of its content.
	private sealed class LineLayout
	{
		private LineLayout(int[] starts, int[] contentLengths, string[] contents)
		{
			Starts = starts;
			ContentLengths = contentLengths;
			Contents = contents;
		}

		internal int[] Starts { get; }

		internal int[] ContentLengths { get; }

		internal string[] Contents { get; }

		internal int Count => Starts.Length;

		internal static LineLayout Create(string text)
		{
			(int[] starts, int[] lengths) = TextLineIndexBuilder.Build(text);
			string[] contents = new string[starts.Length];

			for (int index = 0; index < contents.Length; index++)
				contents[index] = text.Substring(starts[index], lengths[index]);

			return new LineLayout(starts, lengths, contents);
		}

		internal int TerminatorLength(int index, int textLength) =>
			(index + 1 < Starts.Length ? Starts[index + 1] : textLength) - Starts[index] - ContentLengths[index];

		// The offsets of a run of lines with their terminators.
		internal (int Start, int End) Bounds(int first, int count, int textLength)
		{
			if (count == 0)
			{
				int at = first < Starts.Length ? Starts[first] : textLength;
				return (at, at);
			}

			int last = first + count - 1;

			return (Starts[first], last + 1 < Starts.Length ? Starts[last + 1] : textLength);
		}
	}

	/// <summary>Computes the minimal single-range replacement that transforms the old content into the new content.</summary>
	/// <param name="oldText">The old document content; <see langword="null"/> is treated as empty.</param>
	/// <param name="newText">The updated document content; <see langword="null"/> is treated as empty.</param>
	/// <returns>The replacement range and text. The range is expressed in UTF-16 offsets.</returns>
	/// <remarks>
	/// The range start and end never split a UTF-16 surrogate pair or a CRLF sequence: a minimal boundary that would split
	/// one is widened, never narrowed, to include the whole unit, so offsets stay on character and line boundaries.
	/// </remarks>
	public static TextIncrementalEdit Compute(string? oldText, string? newText)
	{
		oldText ??= string.Empty;
		newText ??= string.Empty;

		int prefixLength = ComputeCommonPrefixLength(oldText, newText);
		int suffixLength = ComputeCommonSuffixLength(oldText, newText, prefixLength);
		int oldEnd = oldText.Length - suffixLength;
		int newEnd = newText.Length - suffixLength;

		// Move the start before any text unit that straddles it.
		while (prefixLength > 0 && SplitsTextUnit(oldText, prefixLength))
			prefixLength--;

		// Move both ends past any text unit that straddles them. The ends move in lockstep, so the
		// shared tail stays identical in both texts and keeps the same length in each.
		while (oldEnd < oldText.Length && SplitsTextUnit(oldText, oldEnd))
		{
			oldEnd++;
			newEnd++;
		}

		string replacement = newEnd > prefixLength ? newText[prefixLength..newEnd] : string.Empty;

		return new(TextRange.FromBounds(prefixLength, oldEnd), replacement);
	}

	/// <summary>
	/// Determines whether the boundary between <paramref name="boundary"/> - 1 and <paramref name="boundary"/> falls inside a UTF-16 surrogate pair
	/// or between the CR and LF of a CRLF sequence.
	/// </summary>
	private static bool SplitsTextUnit(string text, int boundary)
	{
		if (boundary <= 0 || boundary >= text.Length)
			return false;

		char before = text[boundary - 1];
		char after = text[boundary];

		return LineTerminators.IsCrLfPair(before, after) || (char.IsHighSurrogate(before) && char.IsLowSurrogate(after));
	}

	private static int ComputeCommonPrefixLength(string first, string second) => first.AsSpan().CommonPrefixLength(second.AsSpan());

	private static int ComputeCommonSuffixLength(string first, string second, int prefixLength)
	{
		int maximum = Math.Min(first.Length, second.Length) - prefixLength;
		int index = 0;

		while (index < maximum && first[first.Length - 1 - index] == second[second.Length - 1 - index])
			index++;

		return index;
	}
}

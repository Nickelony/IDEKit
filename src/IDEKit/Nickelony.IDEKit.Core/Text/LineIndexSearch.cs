namespace Nickelony.IDEKit.Core.Text;

/// <summary>Maps an offset to the index of the line that contains it.</summary>
internal static class LineIndexSearch
{
	/// <summary>Finds the index of the line containing the supplied offset.</summary>
	/// <param name="lineStartOffsets">The strictly ascending line start offsets; the collection must not be empty.</param>
	/// <param name="offset">The zero-based UTF-16 offset to locate.</param>
	/// <returns>The zero-based index of the containing line.</returns>
	/// <exception cref="ArgumentException"><paramref name="lineStartOffsets"/> is empty.</exception>
	/// <remarks>
	/// The last line whose start offset is at or before <paramref name="offset"/> owns it, so an offset on a line terminator belongs
	/// to the preceding line and an end-of-text offset belongs to the final line. A negative offset belongs to the first line. Callers still validate
	/// untrusted coordinates first, because an offset past the end of the text also maps to the final line.
	/// </remarks>
	internal static int FindLineIndex(ReadOnlySpan<int> lineStartOffsets, int offset)
	{
		if (lineStartOffsets.Length == 0)
			throw new ArgumentException("The line start offsets must contain at least one line.", nameof(lineStartOffsets));

		// MemoryExtensions.BinarySearch returns the index of an exact match or the complement of the
		// insertion point. Line start offsets are strictly ascending, so the containing line is the
		// exact match or the line before the insertion point; an offset before the first start has
		// no line before it, so the first line owns it.
		int index = lineStartOffsets.BinarySearch(offset);
		return index >= 0 ? index : Math.Max(~index - 1, 0);
	}

	/// <summary>Finds the index of the line containing the supplied offset in a span of ascending lines.</summary>
	/// <typeparam name="TLine">The line type that exposes its start offset.</typeparam>
	/// <param name="lines">The ascending lines; the collection must not be empty.</param>
	/// <param name="offset">The zero-based UTF-16 offset to locate.</param>
	/// <returns>The zero-based index of the containing line.</returns>
	/// <exception cref="ArgumentException"><paramref name="lines"/> is empty.</exception>
	/// <remarks>
	/// The same ownership rule as <see cref="FindLineIndex(ReadOnlySpan{int}, int)"/> applies, so a line collection that already carries its start
	/// offsets is searched directly without a second offset array.
	/// </remarks>
	internal static int FindLineIndex<TLine>(ReadOnlySpan<TLine> lines, int offset)
		where TLine : ITextLine
	{
		if (lines.Length == 0)
			throw new ArgumentException("The line collection must contain at least one line.", nameof(lines));

		int low = 0;
		int high = lines.Length - 1;

		while (low < high)
		{
			// The subtraction-first midpoint cannot overflow for any valid index pair, unlike a sum
			// that wraps before the shift.
			int middle = low + ((high - low + 1) >> 1);

			if (lines[middle].Offset <= offset)
				low = middle;
			else
				high = middle - 1;
		}

		return low;
	}
}

namespace Nickelony.IDEKit.Core.Text;

/// <summary>
/// Shared part-character run walks behind <see cref="Identifiers.IdentifierOperations"/> and the word
/// fallback of <see cref="Editing.TextRangeOffsetResolver"/>, so both resolve token boundaries with
/// one implementation.
/// </summary>
/// <remarks>
/// <para>
/// Callers supply their own character rule and seed the walk; this type only locates and expands
/// runs of characters accepted by the supplied rule.
/// </para>
/// <para>
/// The generic <see cref="ITextSource"/> plus struct adapters keep string, memory-slice, and snapshot
/// storage in place without copying a snapshot; a <see cref="ReadOnlyMemory{T}"/> slice satisfies the
/// contract, which a <see cref="ReadOnlySpan{T}"/> cannot.
/// </para>
/// </remarks>
internal static class IdentifierBoundaryWalker
{
	/// <summary>
	/// Provides UTF-16 character access over a string or text snapshot for a boundary walk.
	/// </summary>
	internal interface ITextSource
	{
		/// <summary>
		/// Gets the number of UTF-16 code units in the source.
		/// </summary>
		int Length { get; }

		/// <summary>
		/// Gets the character at the specified zero-based index.
		/// </summary>
		/// <param name="index">The zero-based index of the character to retrieve.</param>
		/// <returns>The character at the specified index.</returns>
		char this[int index] { get; }
	}

	/// <summary>
	/// Wraps a string for a boundary walk.
	/// </summary>
	/// <param name="text">The text to walk.</param>
	internal readonly struct StringSource(string text) : ITextSource
	{
		/// <inheritdoc/>
		public int Length => text.Length;

		/// <inheritdoc/>
		public char this[int index] => text[index];
	}

	/// <summary>
	/// Wraps a memory slice for a boundary walk.
	/// </summary>
	/// <param name="memory">The memory to walk.</param>
	internal readonly struct MemorySource(ReadOnlyMemory<char> memory) : ITextSource
	{
		/// <inheritdoc/>
		public int Length => memory.Length;

		/// <inheritdoc/>
		public char this[int index] => memory.Span[index];
	}

	/// <summary>
	/// Wraps a text snapshot for a boundary walk.
	/// </summary>
	/// <param name="snapshot">The snapshot to walk.</param>
	internal readonly struct SnapshotSource(ITextSnapshot snapshot) : ITextSource
	{
		/// <inheritdoc/>
		public int Length => snapshot.TextLength;

		/// <inheritdoc/>
		public char this[int index] => snapshot.GetCharAt(index);
	}

	/// <summary>
	/// Walks back from <paramref name="endIndex"/> to the start of the part-character run that
	/// immediately precedes it.
	/// </summary>
	/// <typeparam name="TSource">The character source type.</typeparam>
	/// <param name="source">The text to inspect.</param>
	/// <param name="endIndex">The index at which the walk starts.</param>
	/// <param name="isPartCharacter">Determines whether a character may appear within a run.</param>
	/// <returns>
	/// The start index of the run ending at <paramref name="endIndex"/>, or <paramref name="endIndex"/>
	/// itself when the preceding character is not a part character.
	/// </returns>
	internal static int FindPrecedingRunStart<TSource>(TSource source, int endIndex, Func<char, bool> isPartCharacter)
		where TSource : ITextSource
	{
		int start = endIndex;

		while (start > 0 && isPartCharacter(source[start - 1]))
			start--;

		return start;
	}

	/// <summary>
	/// Finds the first part character at or after <paramref name="startIndex"/>.
	/// </summary>
	/// <typeparam name="TSource">The character source type.</typeparam>
	/// <param name="source">The text to inspect.</param>
	/// <param name="startIndex">The index at which the search starts.</param>
	/// <param name="isPartCharacter">Determines whether a character may appear within a run.</param>
	/// <returns>The index of the first part character, or <c>-1</c> when none exists.</returns>
	internal static int FindNextPartCharacter<TSource>(TSource source, int startIndex, Func<char, bool> isPartCharacter)
		where TSource : ITextSource
	{
		int index = startIndex;

		while (index < source.Length && !isPartCharacter(source[index]))
			index++;

		return index < source.Length ? index : -1;
	}

	/// <summary>
	/// Expands the maximal run of part characters around a seed that is itself a part character.
	/// </summary>
	/// <typeparam name="TSource">The character source type.</typeparam>
	/// <param name="source">The text to inspect.</param>
	/// <param name="seedIndex">The index of a part character inside the run.</param>
	/// <param name="isPartCharacter">Determines whether a character may appear within a run.</param>
	/// <param name="start">Receives the run's start index.</param>
	/// <param name="end">Receives the index one past the run's last character.</param>
	/// <returns><see langword="true"/> when the run is non-empty.</returns>
	internal static bool TryExpandRun<TSource>(TSource source, int seedIndex, Func<char, bool> isPartCharacter, out int start, out int end)
		where TSource : ITextSource
	{
		System.Diagnostics.Debug.Assert(
			seedIndex >= 0 && seedIndex < source.Length && isPartCharacter(source[seedIndex]),
			"The seed must be a part character inside the source; any other seed silently truncates the run.");

		start = FindPrecedingRunStart(source, seedIndex, isPartCharacter);
		end = seedIndex;

		while (end < source.Length && isPartCharacter(source[end]))
			end++;

		return end > start;
	}
}

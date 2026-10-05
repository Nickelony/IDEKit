using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Identifiers;

/// <summary>Provides the shared part-character run walks that resolve identifier and word token boundaries.</summary>
/// <remarks>
/// The caller supplies the character rule and seeds the walk; this type only locates and expands runs of accepted characters.
/// The <see cref="ITextCharSource"/> contract lets string, memory, and snapshot storage be walked without copying it: a snapshot is walked
/// as it is, <see cref="StringSource"/> wraps a string, and <see cref="MemorySource"/> wraps a <see cref="ReadOnlyMemory{T}"/>.
/// A <see cref="ReadOnlySpan{T}"/> cannot be wrapped this way because it is a ref struct.
/// </remarks>
internal static class IdentifierBoundaryWalker
{
	/// <summary>Wraps a string for a boundary walk.</summary>
	/// <param name="text">The text to walk.</param>
	internal readonly struct StringSource(string text) : ITextCharSource
	{
		/// <inheritdoc/>
		public int TextLength => text.Length;

		/// <inheritdoc/>
		public char GetCharAt(int offset) => text[offset];
	}

	/// <summary>Wraps a memory slice for a boundary walk.</summary>
	/// <param name="memory">The memory to walk.</param>
	internal readonly struct MemorySource(ReadOnlyMemory<char> memory) : ITextCharSource
	{
		/// <inheritdoc/>
		public int TextLength => memory.Length;

		/// <inheritdoc/>
		public char GetCharAt(int offset) => memory.Span[offset];
	}

	/// <summary>Walks back from <paramref name="endIndex"/> to the start of the part-character run that immediately precedes it.</summary>
	/// <typeparam name="TSource">The character source type.</typeparam>
	/// <param name="source">The text to inspect.</param>
	/// <param name="endIndex">The index at which the walk starts.</param>
	/// <param name="isPartCharacter">Determines whether a character may appear within a run.</param>
	/// <returns>
	/// The start index of the run ending at <paramref name="endIndex"/>, or <paramref name="endIndex"/> itself when the preceding character
	/// is not a part character.
	/// </returns>
	internal static int FindPrecedingRunStart<TSource>(TSource source, int endIndex, Func<char, bool> isPartCharacter)
		where TSource : ITextCharSource
	{
		int start = endIndex;

		while (start > 0 && isPartCharacter(source.GetCharAt(start - 1)))
			start--;

		return start;
	}

	/// <summary>Finds the first part character at or after <paramref name="startIndex"/>.</summary>
	/// <typeparam name="TSource">The character source type.</typeparam>
	/// <param name="source">The text to inspect.</param>
	/// <param name="startIndex">The index at which the search starts.</param>
	/// <param name="isPartCharacter">Determines whether a character may appear within a run.</param>
	/// <returns>The index of the first part character, or <c>-1</c> when none exists.</returns>
	internal static int FindNextPartCharacter<TSource>(TSource source, int startIndex, Func<char, bool> isPartCharacter)
		where TSource : ITextCharSource
	{
		int index = startIndex;

		while (index < source.TextLength && !isPartCharacter(source.GetCharAt(index)))
			index++;

		return index < source.TextLength ? index : -1;
	}

	/// <summary>Expands the maximal run of part characters around a seed that is itself a part character.</summary>
	/// <typeparam name="TSource">The character source type.</typeparam>
	/// <param name="source">The text to inspect.</param>
	/// <param name="seedIndex">The index of a part character inside the run.</param>
	/// <param name="isPartCharacter">Determines whether a character may appear within a run.</param>
	/// <param name="start">Receives the run's start index.</param>
	/// <param name="end">Receives the index one past the run's last character.</param>
	/// <returns><see langword="true"/> when the run is non-empty.</returns>
	internal static bool TryExpandRun<TSource>(TSource source, int seedIndex, Func<char, bool> isPartCharacter, out int start, out int end)
		where TSource : ITextCharSource
	{
		System.Diagnostics.Debug.Assert(
			seedIndex >= 0 && seedIndex < source.TextLength && isPartCharacter(source.GetCharAt(seedIndex)),
			"The seed must be a part character inside the source; any other seed silently truncates the run."
		);

		start = FindPrecedingRunStart(source, seedIndex, isPartCharacter);
		end = seedIndex;

		while (end < source.TextLength && isPartCharacter(source.GetCharAt(end)))
			end++;

		return end > start;
	}
}

namespace Nickelony.IDEKit.Core.Text;

/// <summary>Caches a per-line parser continuation state so line-oriented scans stay fast after document edits.</summary>
/// <typeparam name="TState">The parser continuation state type carried across lines.</typeparam>
/// <remarks>
/// States are computed lazily; applying an edit preserves cached states up to and including the first
/// affected line's start state and invalidates the remainder. All members are safe for concurrent use. The transition delegate runs while the cache
/// lock is held, so it must be fast and pure and must not call back into the cache, whose lock is re-entrant (such a callback can recurse
/// until the stack overflows).
/// </remarks>
public sealed class IncrementalLineStateCache<TState>
{
	private readonly object _syncRoot = new();
	private readonly Func<string, TState, TState> _transition;
	private readonly List<TState> _cachedLineStartStates = [];

	private ITextSnapshot _snapshot;

	/// <param name="snapshot">The snapshot that provides line text and line mapping for the cached states.</param>
	/// <param name="transition">Computes the state at the start of the next line from a line's text and the state at that line's start.</param>
	/// <exception cref="ArgumentNullException"><paramref name="snapshot"/> or <paramref name="transition"/> is <see langword="null"/>.</exception>
	public IncrementalLineStateCache(ITextSnapshot snapshot, Func<string, TState, TState> transition)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		ArgumentNullException.ThrowIfNull(transition);

		_snapshot = snapshot;
		_transition = transition;
	}

	/// <summary>Returns the parser continuation state that applies at the start of the specified one-based line.</summary>
	/// <returns>The cached or computed line-start parser state, or the default state for an empty snapshot.</returns>
	/// <remarks>
	/// Line numbers at or below 1 return the default state, and a line number past the last line clamps to the final line instead of throwing.
	/// The default state is <c>default(TState)</c>, so a reference state type may carry <see langword="null"/>; <typeparamref name="TState"/> is left
	/// unconstrained so hosts can use a nullable state value.
	/// </remarks>
	public TState GetLineStartState(int lineNumber)
	{
		if (lineNumber <= 1)
			return default!;

		lock (_syncRoot)
		{
			if (_snapshot.LineCount == 0)
				return default!;

			int targetLineNumber = Math.Min(lineNumber, _snapshot.LineCount);

			EnsureFirstLineStateCached();
			EnsureStatesCachedThrough(targetLineNumber);
			return _cachedLineStartStates[targetLineNumber - 1];
		}
	}

	/// <summary>Applies a document edit and invalidates the cached states the edit affects.</summary>
	/// <param name="newSnapshot">The snapshot that reflects the document after the edit.</param>
	/// <param name="firstChangedOffset">
	/// The zero-based offset at which the applied edit begins; must lie within the new snapshot's text
	/// (0 through <see cref="ITextCharSource.TextLength"/>).
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="newSnapshot"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="firstChangedOffset"/> is negative or beyond the new snapshot's text length.
	/// </exception>
	/// <remarks>
	/// The caller must pass the earliest start offset of the applied edit (or of a coalesced burst) and leave the text before it unchanged.
	/// The cache cannot verify that precondition - a later offset keeps states computed from changed text - but it rejects an offset
	/// outside the new snapshot.
	/// </remarks>
	public void ApplyEdit(ITextSnapshot newSnapshot, int firstChangedOffset)
	{
		ArgumentNullException.ThrowIfNull(newSnapshot);
		ArgumentOutOfRangeException.ThrowIfNegative(firstChangedOffset);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(firstChangedOffset, newSnapshot.TextLength);

		lock (_syncRoot)
		{
			_snapshot = newSnapshot;

			if (_cachedLineStartStates.Count == 0)
				return;

			int firstAffectedLineNumber = GetLineNumberForOffset(firstChangedOffset);

			// The state at the start of the first affected line depends only on text strictly before
			// that line, which the edit leaves unchanged, so that state stays valid and is preserved as
			// well. For a non-empty snapshot the retained states never outnumber its lines, because the
			// first affected line number is a line number of the new snapshot; an empty snapshot keeps
			// at most the single default first-line state, which GetLineStartState returns anyway.
			int preservedLineCount = firstAffectedLineNumber;

			if (_cachedLineStartStates.Count > preservedLineCount)
				_cachedLineStartStates.RemoveRange(preservedLineCount, _cachedLineStartStates.Count - preservedLineCount);
		}
	}

	private void EnsureFirstLineStateCached()
	{
		if (_snapshot.LineCount > 0 && _cachedLineStartStates.Count == 0)
			_cachedLineStartStates.Add(default!);
	}

	private void EnsureStatesCachedThrough(int lineNumber)
	{
		int targetLineNumber = Math.Min(lineNumber, _snapshot.LineCount);

		if (targetLineNumber <= 0)
			return;

		while (_cachedLineStartStates.Count < targetLineNumber)
		{
			int previousLineNumber = _cachedLineStartStates.Count;
			ITextLine previousLine = _snapshot.GetLineByNumber(previousLineNumber);
			string previousLineText = _snapshot.GetText(previousLine.Offset, previousLine.Length);
			TState nextState = _transition(previousLineText, _cachedLineStartStates[previousLineNumber - 1]);

			_cachedLineStartStates.Add(nextState);
		}
	}

	private int GetLineNumberForOffset(int offset)
	{
		if (_snapshot.LineCount == 0)
			return 1;

		return _snapshot.GetLineByOffset(offset).LineNumber;
	}
}

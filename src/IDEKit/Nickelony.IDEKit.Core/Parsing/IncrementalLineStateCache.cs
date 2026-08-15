using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Parsing;

/// <summary>
/// Caches a per-line parser continuation state so line-oriented scans stay fast after document edits.
/// </summary>
/// <remarks>
/// <para>
/// States are computed lazily from an <see cref="ITextSnapshot"/>. Applying an edit preserves the
/// cached states up to and including the first affected line's start state, which depends only on
/// unchanged text, and invalidates the remainder for recomputation.
/// </para>
/// <para>
/// All members are safe for concurrent use. The transition delegate runs while the cache lock is
/// held, so it should be fast and pure: it must not block on other locks and must not call back
/// into the cache, because the lock is re-entrant and such a callback would recurse until the
/// stack overflows.
/// </para>
/// </remarks>
/// <typeparam name="TState">The parser continuation state type carried across lines.</typeparam>
public sealed class IncrementalLineStateCache<TState>
{
	private readonly object _syncRoot = new();
	private readonly Func<string, TState, TState> _transition;
	private readonly List<TState> _cachedLineStartStates = [];
	private ITextSnapshot _snapshot;

	/// <summary>
	/// Initializes a new instance of the <see cref="IncrementalLineStateCache{TState}"/> class.
	/// </summary>
	/// <param name="snapshot">The snapshot that provides line text and line mapping for the cached states.</param>
	/// <param name="transition">
	/// Computes the continuation state for a line given its text and the previous line's state.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="snapshot"/> or <paramref name="transition"/> is <see langword="null"/>.
	/// </exception>
	public IncrementalLineStateCache(ITextSnapshot snapshot, Func<string, TState, TState> transition)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		ArgumentNullException.ThrowIfNull(transition);

		_snapshot = snapshot;
		_transition = transition;
	}

	/// <summary>
	/// Gets the parser continuation state that applies at the start of the specified one-based line.
	/// </summary>
	/// <remarks>
	/// Line numbers at or below 1 return the default state. Line numbers past the end of the
	/// snapshot clamp to its final line, so a stale number still resolves a line-start state instead
	/// of throwing. The default state is <c>default(TState)</c>, so a reference state type may carry
	/// <see langword="null"/>; the type intentionally leaves <typeparamref name="TState"/>
	/// unconstrained so hosts can use a nullable state value.
	/// </remarks>
	/// <param name="lineNumber">The one-based document line number.</param>
	/// <returns>
	/// The cached or computed line-start parser state, or the default state for an empty snapshot. A
	/// line number past the end returns the state of the clamped final line, not the requested line.
	/// </returns>
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

	/// <summary>
	/// Applies a document edit, preserving cached states up to and including the first affected
	/// line's start state and invalidating the remainder so it can be recomputed from the new snapshot.
	/// </summary>
	/// <remarks>
	/// The caller must pass the earliest start offset of the applied edit (when a burst of edits is
	/// coalesced, the earliest start offset of the burst) and must leave the text before that offset
	/// unchanged, so the line containing it starts at the same state in both snapshots. The cache
	/// cannot verify those preconditions - passing a later offset, for example the end of a replaced
	/// range, keeps states computed from text the edit changed - but it rejects offsets outside the
	/// new snapshot instead of silently preserving every cached state for an impossible position.
	/// </remarks>
	/// <param name="newSnapshot">The snapshot that reflects the document after the edit.</param>
	/// <param name="firstChangedOffset">
	/// The zero-based offset at which the applied edit begins; must lie within the new snapshot's
	/// text (0 through <see cref="ITextSnapshot.TextLength"/>).
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="newSnapshot"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="firstChangedOffset"/> is negative or beyond the new snapshot's text length.
	/// </exception>
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

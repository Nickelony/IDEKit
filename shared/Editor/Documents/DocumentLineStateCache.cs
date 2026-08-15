#if AVALONIAEDIT
using AvaloniaEdit.Document;
#else
using ICSharpCode.AvalonEdit.Document;
#endif
using Nickelony.IDEKit.Core.Parsing;

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.Documents;
#else
namespace Nickelony.IDEKit.AvalonEdit.Documents;
#endif

/// <summary>
/// Tracks parser continuation states at line starts for the editor's <see cref="TextDocument"/>
/// and invalidates affected states when the document changes.
/// </summary>
/// <remarks>
/// <para>
/// Use this class when an editor feature scans a document one line at a time and carries state across line boundaries.
/// For example, a syntax highlighter can cache whether each line starts inside a block comment or string.
/// </para>
/// <para>
/// Edits invalidate the affected suffix, and states are recomputed lazily as needed. Document snapshots
/// are captured lazily as well: edits are coalesced, the document is captured at most once per burst of
/// edits, and only when a request needs a line state after the line that contains the earliest change. A
/// request for the line containing the earliest change, or any earlier line, is served from the existing
/// cache because its start state is unaffected.
/// </para>
/// <para>
/// The cache is not thread-safe: create it, request states, and dispose it on the document's owner
/// thread, like all editor document access.
/// </para>
/// <para>
/// Disposal releases the document subscription and the cached states. The type follows the package's
/// disposable-tracker contract: <see cref="Dispose"/> is idempotent, and after it runs
/// <see cref="GetLineStartState"/> keeps returning <see langword="default"/> instead of throwing, because
/// the read runs inside a colorizer's render pass. The cache has no throwing member, so disposal is never
/// observable beyond those default states.
/// </para>
/// </remarks>
/// <typeparam name="TState">The parser continuation state carried from one line to the next.</typeparam>
public sealed class DocumentLineStateCache<TState> : IDisposable
{
	private readonly TextDocument _document;
	private readonly Func<string, TState, TState> _transition;

	private IncrementalLineStateCache<TState>? _cache;

	private bool _disposed;
	private bool _hasPendingChanges;
	private int _earliestPendingChangeOffset;

	/// <summary>
	/// Initializes a new instance of the <see cref="DocumentLineStateCache{TState}"/> class.
	/// </summary>
	/// <param name="document">The editor's <see cref="TextDocument"/> whose line-start states are tracked.</param>
	/// <param name="transition">
	/// Computes the next line's state from the current line's text and start state. It must be fast and
	/// pure, and it must not call back into this cache: the underlying cache lock is re-entrant, so a
	/// callback that requests line states recurses until the stack overflows.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="document"/> or <paramref name="transition"/> is <see langword="null"/>.
	/// </exception>
	public DocumentLineStateCache(TextDocument document, Func<string, TState, TState> transition)
	{
		ArgumentNullException.ThrowIfNull(document);
		ArgumentNullException.ThrowIfNull(transition);

		_document = document;
		_transition = transition;
		_document.Changed += Document_Changed;
	}

	/// <summary>
	/// Gets the parser continuation state at the start of the specified one-based line.
	/// </summary>
	/// <param name="lineNumber">
	/// The one-based line number. Values less than or equal to 1 return <see langword="default"/>.
	/// Values beyond the document's final line are clamped to the final line.
	/// </param>
	/// <returns>
	/// The cached or computed line-start state;
	/// <see langword="default"/> for line numbers at or below 1, and after disposal.
	/// </returns>
	public TState GetLineStartState(int lineNumber)
	{
		// The read runs inside a colorizer's render pass, so disposal must not turn it into a dispatcher
		// exception; a disposed cache reports the same default states it reports for lines at or below 1.
		if (_disposed || lineNumber <= 1)
			return default!;

		return EnsureCache(lineNumber).GetLineStartState(lineNumber);
	}

	/// <summary>
	/// Stops tracking edits to the editor's <see cref="TextDocument"/> and discards the cached states.
	/// </summary>
	/// <remarks>
	/// A disposed cache reports <see langword="default"/> for every read instead of throwing, because
	/// the reads run inside a colorizer's render pass; disposal is not observable beyond that. Repeated
	/// calls have no effect.
	/// </remarks>
	public void Dispose()
	{
		if (_disposed)
			return;

		_disposed = true;

		_document.Changed -= Document_Changed;
		_cache = null;
	}

	/// <summary>
	/// Gets the incremental cache, creating it on first use and applying the coalesced edits since the
	/// last refresh when <paramref name="lineNumber"/> needs them.
	/// </summary>
	private IncrementalLineStateCache<TState> EnsureCache(int lineNumber)
	{
		IncrementalLineStateCache<TState>? cache = _cache;

		if (cache is null)
		{
			// The first snapshot already contains every pending change.
			cache = new IncrementalLineStateCache<TState>(new TextDocumentSnapshot(_document), _transition);

			_cache = cache;
			_hasPendingChanges = false;
			return cache;
		}

		if (!_hasPendingChanges || IsServedByExistingCache(lineNumber))
			return cache;

		// One snapshot covers every coalesced edit; invalidating from the earliest offset is conservative.
		// A deletion can shrink the document below that offset, so it is clamped to the new length: no line
		// start lies beyond the end, and the trailing region is invalidated from the last position.
		int pendingChangeOffset = _earliestPendingChangeOffset;

		// The snapshot is captured before the pending flag is cleared, so a construction failure leaves
		// the pending state intact and the next request retries instead of serving stale states.
		TextDocumentSnapshot snapshot = new(_document);

		cache.ApplyEdit(snapshot, Math.Min(pendingChangeOffset, snapshot.TextLength));

		_hasPendingChanges = false;
		return cache;
	}

	/// <summary>
	/// Determines whether a line-start state can be served from the existing cache even though edits
	/// are pending.
	/// </summary>
	/// <remarks>
	/// Every pending change starts at or after the earliest pending offset, so the text before that
	/// offset is unchanged and so is every line start at or before the start of the line containing the
	/// earliest change. The state of a line is the state before the line's first character, which depends
	/// only on the lines before it.
	/// </remarks>
	private bool IsServedByExistingCache(int lineNumber)
	{
		DocumentLine changedLine = _document.GetLineByOffset(_document.ClampOffset(_earliestPendingChangeOffset));

		return lineNumber <= changedLine.LineNumber;
	}

	private void Document_Changed(object? sender, DocumentChangeEventArgs e)
	{
		if (!_hasPendingChanges || e.Offset < _earliestPendingChangeOffset)
			_earliestPendingChangeOffset = e.Offset;

		_hasPendingChanges = true;
	}
}

using Nickelony.IDEKit.Core.Diffing;
using Nickelony.IDEKit.Core.LineStatus;
using Nickelony.IDEKit.Core.Notifications;
using Nickelony.IDEKit.Core.Text;
#if AVALONIAEDIT
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.Text;
#else
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Text;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.LineStatus;

#else

namespace Nickelony.IDEKit.AvalonEdit.LineStatus;

#endif

/// <summary>Provides the line numbers of document lines inserted or modified relative to a recorded baseline.</summary>
/// <remarks>
/// Until <see cref="SetBaseline"/> sets a baseline it is empty, so every line is reported as changed; line terminators are normalized
/// before comparison (CRLF, LF, and lone CR compare equal), and deletion-only edits produce no marked line because only lines present
/// in the current document are returned. <see cref="SetBaseline"/> may run on any thread and raises <see cref="Changed"/> on the calling thread,
/// while <see cref="GetMarkedLineNumbers"/> reads the document and must run on the document's owner thread. The tracker subscribes
/// to a document on the first <see cref="GetMarkedLineNumbers"/> call that reads it, so edits made before that call raise no <see cref="Changed"/>.
/// Disposal is optional and releases the document subscription and the maintained line view; after disposal the read returns an empty list
/// and <see cref="SetBaseline"/> throws <see cref="ObjectDisposedException"/>.
/// </remarks>
public sealed class UnsavedChangesTracker : ILineStatusSource, IChangeNotificationSource, IDisposable
{
	private readonly Func<TextDocument?> _documentProvider;

	private string[] _baselineLines = [];

	private long _revision;

	private readonly DocumentVersionCache<IReadOnlyList<int>> _markedLinesCache = new();

	// Returned on the disposed and no-document paths, which a margin's render pass hits on every frame;
	// a shared instance avoids a per-render allocation.
	private static readonly IReadOnlyList<int> s_emptyLineNumbers = [];

	// The incrementally maintained line view of the tracked document; only touched on the document's
	// owner thread, which is also the thread the change notifications arrive on. A change replaces the
	// affected line window in place instead of rebuilding the whole view.
	private TextDocument? _trackedDocument;
	private ITextSourceVersion? _syncedVersion;

	private readonly List<string> _documentLines = [];

	private bool _isViewDirty;
	private bool _isDisposed;

	// The most recent failure of each best-effort maintenance path, retained for diagnostics instead of
	// discarded; the two paths fail independently, so each keeps its own slot. A failure abandons the fast
	// path, and the next read rebuilds the whole view (or recomputes the marked lines).
	internal Exception? LastViewUpdateFailure { get; private set; }

	internal Exception? LastMarkedLinesRefreshFailure { get; private set; }

	// How many whole-document diffs ran, which a test reads to pin that a batch of changes costs one.
	internal int MarkedLinesComputationCount { get; private set; }

	/// <summary>
	/// Raised after <see cref="SetBaseline"/> replaces the comparison baseline, and after every document change once the first
	/// <see cref="GetMarkedLineNumbers"/> call has attached the tracker to the document, because either can change the marked lines.
	/// </summary>
	/// <remarks>
	/// The event is raised on the thread that caused it - the calling thread for <see cref="SetBaseline"/> and the document's
	/// owner thread for edits, where it arrives inside the document's change notification. A host subscriber that touches the editor
	/// must marshal the work itself and must not edit the document synchronously, because the document rejects a change inside another change;
	/// an attached <see cref="ChangeMarkerMargin"/> marshals to its dispatcher and repaints on its own.
	/// </remarks>
	public event EventHandler? Changed;

	/// <param name="documentProvider">
	/// Provides the document whose lines are tracked, or <see langword="null"/> when no document is current; the tracker marks nothing in that state
	/// instead of throwing.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="documentProvider"/> is <see langword="null"/>.</exception>
	public UnsavedChangesTracker(Func<TextDocument?> documentProvider)
	{
		ArgumentNullException.ThrowIfNull(documentProvider);
		_documentProvider = documentProvider;
	}

	/// <summary>Replaces the comparison baseline with <paramref name="content"/>.</summary>
	/// <param name="content">The content to compare with the current document.</param>
	/// <exception cref="ArgumentNullException"><paramref name="content"/> is <see langword="null"/>.</exception>
	/// <exception cref="ObjectDisposedException">The instance has been disposed.</exception>
	/// <remarks>
	/// May be called from any thread; the <see cref="Changed"/> notification is raised on the calling thread.
	/// </remarks>
	public void SetBaseline(string content)
	{
		ArgumentNullException.ThrowIfNull(content);
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		// The baseline write precedes the revision bump, so a reader that captures the new revision
		// is guaranteed to observe the new baseline. The revision guard is what invalidates the cached
		// marked lines: the cache is documented as owner-thread-only, so this method - which may run on
		// any thread - must not touch it, and a reader that misses the revision bump recomputes from the
		// new baseline anyway.
		_baselineLines = TextLineSplitter.Split(content);
		Interlocked.Increment(ref _revision);

		Changed?.Invoke(this, EventArgs.Empty);
	}

	/// <summary>Returns the current document line numbers inserted or modified relative to the baseline.</summary>
	/// <remarks>
	/// Only lines present in the current document are returned, so deletion-only changes produce no marked line and an empty baseline
	/// marks every line; the numbers are one-based, ascending, and refer to the current document. A result is reused while the document, its version,
	/// and the baseline revision are unchanged. A provider that returns <see langword="null"/> or a disposed tracker yields an empty list,
	/// because this read runs in a margin's render pass and must not throw.
	/// </remarks>
	public IReadOnlyList<int> GetMarkedLineNumbers()
	{
		// The read runs inside a margin's render pass, so disposal must not turn it into a dispatcher
		// exception; a disposed tracker marks nothing.
		if (_isDisposed)
			return s_emptyLineNumbers;

		TextDocument? document = _documentProvider();

		if (document is null)
			return s_emptyLineNumbers;

		// The revision is captured before the cache decides, and stored with the result, so a baseline
		// replaced from another thread while the value is computed cannot be served from the cache.
		return _markedLinesCache.GetOrCreate(document, CollectMarkedLineNumbers, Volatile.Read(ref _revision));
	}

	/// <summary>Detaches the tracker from its document and drops the cached lines and result.</summary>
	/// <remarks>
	/// Disposal is optional, because the editor typically owns the tracker and its document together; it releases an abandoned
	/// tracker from the document's change subscription. <see cref="SetBaseline"/> throws <see cref="ObjectDisposedException"/> after disposal,
	/// <see cref="GetMarkedLineNumbers"/> keeps returning an empty list, and repeated calls have no effect.
	/// </remarks>
	public void Dispose()
	{
		if (_isDisposed)
			return;

		_isDisposed = true;

		if (_trackedDocument is not null)
		{
			_trackedDocument.Changed -= HandleDocumentChanged;
			_trackedDocument.UpdateFinished -= HandleDocumentUpdateFinished;
			_trackedDocument = null;
		}

		_documentLines.Clear();
		_markedLinesCache.Invalidate();
	}

	private IReadOnlyList<int> CollectMarkedLineNumbers(TextDocument document)
	{
		MarkedLinesComputationCount++;

		List<string> documentLines = EnsureDocumentLines(document);
		var changedLineNumbers = new List<int>();

		// The diff covers the whole document on purpose: its line alignment is what the differential tests pin.
		foreach (int lineNumber in LineDiffer.GetChangedLineNumbers(_baselineLines, documentLines).ChangedLineNumbers)
		{
			if (lineNumber >= 1 && lineNumber <= document.LineCount)
				changedLineNumbers.Add(lineNumber);
		}

		changedLineNumbers.Sort();

		return changedLineNumbers.AsReadOnly();
	}

	/// <summary>
	/// Returns the incrementally maintained line view of <paramref name="document"/>, attaching to the document and rebuilding the view
	/// when the subscription is new, the view is dirty, or a version difference shows that changes were not observed.
	/// </summary>
	private List<string> EnsureDocumentLines(TextDocument document)
	{
		// The change subscription attaches lazily on the first read, and a version difference that
		// shows unobserved changes (for example edits made before the subscription existed) rebuilds
		// the view defensively.
		if (!ReferenceEquals(_trackedDocument, document))
		{
			if (_trackedDocument is not null)
			{
				_trackedDocument.Changed -= HandleDocumentChanged;
				_trackedDocument.UpdateFinished -= HandleDocumentUpdateFinished;
			}

			_trackedDocument = document;
			RebuildDocumentLines(document);
			document.Changed += HandleDocumentChanged;
			document.UpdateFinished += HandleDocumentUpdateFinished;

			return _documentLines;
		}

		if (_isViewDirty || !ReferenceEquals(_syncedVersion, document.Version))
		{
			// The version changed without a change notification (for example because the subscription was
			// attached after edits), or an incremental update faulted and marked the view dirty, so the
			// defensive path rebuilds the whole view.
			RebuildDocumentLines(document);
		}

		return _documentLines;
	}

	private void RebuildDocumentLines(TextDocument document)
	{
		_documentLines.Clear();
		_documentLines.AddRange(TextLineSplitter.Split(document.Text));
		_syncedVersion = document.Version;
		_isViewDirty = false;
	}

	/// <summary>Handles a change of the tracked document; a test calls it to drive a change whose arguments fault the incremental update.</summary>
	/// <param name="sender">The document the change is attributed to.</param>
	/// <param name="e">The change arguments.</param>
	internal void HandleDocumentChanged(object? sender, DocumentChangeEventArgs e)
	{
		if (sender is not TextDocument document || !ReferenceEquals(document, _trackedDocument))
			return;

		// The incremental math relies on the view matching the document as it was before this change.
		// Once a failure has marked the view dirty it stops matching, so the update is skipped
		// entirely: the next read rebuilds the whole view (EnsureDocumentLines observes _isViewDirty), and
		// running the incremental step against a desynced view could otherwise overwrite
		// LastViewUpdateFailure with a secondary failure.
		if (!_isViewDirty)
		{
			try
			{
				UpdateDocumentLines(document, e);
				_syncedVersion = document.Version;
			}
			catch (Exception exception)
			{
				// The update runs inside the document's change notification, so a defect must not escape
				// into the user's edit. The incremental view is abandoned instead: the failure is retained
				// on LastViewUpdateFailure, and the next read rebuilds the whole view from the document
				// text, so a defect degrades to a full rebuild instead of corrupting the edit that
				// triggered it.
				LastViewUpdateFailure = exception;
				_isViewDirty = true;
			}
		}

		// Subscribers invalidate or repaint as well, because edits can change the marked lines.
		Changed?.Invoke(this, EventArgs.Empty);
	}

	/// <summary>Computes the marked lines once for a finished document update.</summary>
	/// <remarks>
	/// The whole-document diff runs here, off the render path and once per update however many changes the update held, so an attached margin's
	/// render pass reads a ready list instead of running the diff inside its OnRender. A change outside an update is its own update, and a read
	/// that arrives first computes the list itself.
	/// </remarks>
	internal void HandleDocumentUpdateFinished(object? sender, EventArgs e)
	{
		if (sender is not TextDocument document || !ReferenceEquals(document, _trackedDocument))
			return;

		try
		{
			_markedLinesCache.GetOrCreate(document, CollectMarkedLineNumbers, Volatile.Read(ref _revision));
		}
		catch (Exception exception)
		{
			// Like the incremental update, this runs inside the document's notification, so a defect must not escape into the user's
			// edit. The cache is dropped instead: the failure is retained on LastMarkedLinesRefreshFailure (its own slot, so it does not
			// overwrite the line-view failure), and the margin's next read recomputes from the document text.
			LastMarkedLinesRefreshFailure = exception;
			_markedLinesCache.Invalidate();
		}
	}

	/// <summary>Replaces the lines affected by one change with the lines that change produced.</summary>
	/// <remarks>
	/// The replaced window is located by line structure, not by counting line breaks in the changed fragments (a fragment holding
	/// half a CRLF pair is not a complete break): the first affected line is the new document's line at the change's offset, and the last is the line
	/// containing the end of the inserted text. The matching old window ends at the same line index plus the line-count difference of the change,
	/// because the unchanged lines after the window keep their line numbers.
	/// </remarks>
	private void UpdateDocumentLines(TextDocument document, DocumentChangeEventArgs e)
	{
		int startIndex = document.GetLineByOffset(e.Offset).LineNumber - 1;
		int newEndIndex = document.GetLineByOffset(e.Offset + e.InsertionLength).LineNumber - 1;
		int oldEndIndex = newEndIndex + (_documentLines.Count - document.LineCount);
		int removedLineCount = Math.Max(0, oldEndIndex - startIndex + 1);

		var replacementLines = new List<string>(newEndIndex - startIndex + 1);

		for (int lineIndex = startIndex; lineIndex <= newEndIndex; lineIndex++)
			replacementLines.Add(document.GetText(document.GetLineByNumber(lineIndex + 1)));

		if (removedLineCount > 0)
			_documentLines.RemoveRange(startIndex, removedLineCount);

		_documentLines.InsertRange(startIndex, replacementLines);
	}
}

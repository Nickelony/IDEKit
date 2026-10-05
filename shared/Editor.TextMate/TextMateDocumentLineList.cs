using System.Diagnostics;
using TextMateSharp.Grammars;
using TextMateSharp.Model;
#if AVALONIAEDIT
using AvaloniaEdit.Document;
#else
using ICSharpCode.AvalonEdit.Document;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.TextMate;

#else

namespace Nickelony.IDEKit.AvalonEdit.TextMate;

#endif

/// <summary>
/// Provides TextMateSharp with a zero-based view of the editor's <see cref="TextDocument"/> lines, each ended by one line feed whatever terminator
/// the document uses, and tracking the document as it changes.
/// </summary>
/// <remarks>
/// <para>
/// The view is an incrementally maintained snapshot: line texts are copied so the tokenizer can read them from its background thread
/// while the document stays owned by the UI thread. The snapshot stores every line's content followed by a line feed, so it duplicates the document
/// text and carries that cost for large documents. A grammar therefore sees the same text for a document that ends its lines with <c>\n</c>,
/// <c>\r\n</c> or <c>\r</c>, and the last line is ended too, as TextMateSharp does for a line without a terminator. Lines outside a change keep
/// their tokenization state; the changed region is re-read and invalidated.
/// </para>
/// <para>
/// After each handled change, the snapshot, the model's line-state list, and the document agree on the line count, and every snapshot line
/// is the document line's content followed by a line feed.
/// </para>
/// <para>
/// The snapshot members the tokenizer
/// thread calls (<see cref="GetNumberOfLines"/>, <see cref="GetLineLength"/>, and <see cref="GetLineTextIncludingTerminators"/>) are safe to call
/// from any thread: the snapshot is guarded by a private lock and those members only read it. Document change handling runs on the thread
/// that owns the document; <see cref="Dispose"/> sets a disposed flag and detaches that handler, so a change that starts after Dispose is dropped
/// instead of mutating the snapshot or enqueuing work into a disposed model. A tokenizer-thread read can briefly observe an update in flight,
/// but reads are range-guarded and an out-of-range read yields empty text and a zero length instead of throwing.
/// </para>
/// <para>
/// A line list backs exactly one <see cref="TMModel"/> because a model binds its line list when it is constructed, and a second model
/// would replace the first model's binding. Disposing the model also disposes its line list, and this list's <see cref="Dispose"/> is idempotent,
/// so disposing the model is sufficient. A list that is disposed without its model keeps the model alive but freezes tokenization on the last
/// snapshot and stops receiving document updates; dispose the model before dropping the list.
/// </para>
/// </remarks>
internal sealed class TextMateDocumentLineList : AbstractLineList
{
	// The line feed that ends every snapshot line. TextMateSharp appends one to a line that lacks it, and VS Code tokenizes every line of its model as
	// the line content followed by a line feed, so a grammar pattern such as `$` or `\n` sees the same text for every document terminator.
	private const string SyntheticLineFeed = "\n";

	private readonly TextDocument _document;
	private readonly object _syncRoot = new();
	private readonly List<string> _lineTexts = [];

	// Set once by Dispose so a document change that starts after disposal cannot mutate the snapshot or enqueue
	// work into a model that is already disposed. A change already inside the lock when Dispose runs still finishes.
	private int _isDisposed;

	/// <param name="document">The document whose lines are tracked.</param>
	internal TextMateDocumentLineList(TextDocument document)
	{
		_document = document;

		InitializeSnapshot();

		_document.Changed += HandleDocumentChanged;
	}

	/// <inheritdoc/>
	/// <remarks>
	/// The snapshot is always current, so this member only marks the line for re-tokenization; it never re-reads the document.
	/// </remarks>
	public override void UpdateLine(int lineIndex) => InvalidateLine(lineIndex);

	/// <inheritdoc/>
	public override int GetNumberOfLines()
	{
		lock (_syncRoot)
			return _lineTexts.Count;
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Unlike the base contract, an out-of-range index yields an empty line instead of throwing. The tokenizer can probe an index that a concurrent
	/// document update has already removed, and an empty line lets that read complete so the failed line is re-tokenized by the next pass.
	/// </remarks>
	public override LineText GetLineTextIncludingTerminators(int lineIndex)
	{
		lock (_syncRoot)
		{
			// The empty line is a tolerant sentinel, not a real line: callers must pass an index in
			// 0..GetNumberOfLines()-1, and this leniency exists only so the tokenizer's probe of a line a
			// concurrent update has already removed completes.
			if (lineIndex < 0 || lineIndex >= _lineTexts.Count)
				return new LineText(string.Empty);

			return new LineText(_lineTexts[lineIndex]);
		}
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Unlike the base contract, an out-of-range index yields zero instead of throwing;
	/// see <see cref="GetLineTextIncludingTerminators"/> for the rationale.
	/// </remarks>
	public override int GetLineLength(int lineIndex)
	{
		lock (_syncRoot)
		{
			// Zero pairs with GetLineTextIncludingTerminators' tolerant sentinel; see the caller contract
			// documented there.
			if (lineIndex < 0 || lineIndex >= _lineTexts.Count)
				return 0;

			return _lineTexts[lineIndex].Length;
		}
	}

	/// <inheritdoc/>
	public override void Dispose()
	{
		if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
			return;

		_document.Changed -= HandleDocumentChanged;
	}

	private void HandleDocumentChanged(object? sender, DocumentChangeEventArgs e)
	{
		int invalidatedStartLineIndex;
		int invalidatedEndLineIndex;

		// Lock-order contract: TextMateSharp's model acquires its own lock before calling into
		// IModelLines, so this list's lock must never be held while calling back into the model
		// (the order is always model lock -> list lock, never the reverse). The disposed check is inside
		// the lock so a change that starts after Dispose returns before the snapshot is mutated.
		lock (_syncRoot)
		{
			if (Volatile.Read(ref _isDisposed) != 0)
				return;

			(invalidatedStartLineIndex, invalidatedEndLineIndex) = ApplyChange(e);
		}

		// Enqueuing re-checks the flag because a dispose can land after the lock is released.
		if (Volatile.Read(ref _isDisposed) != 0)
			return;

		// Outside _syncRoot: InvalidateLineRange calls TMModel.InvalidateLineRange, which marks the whole
		// range and queues it under a single model lock and signal. Every line of the changed region is
		// invalidated because new lines can land on slots that held removed lines: such slots
		// are neither fresh nor guaranteed to have an end state that differs from their predecessor, so
		// the model's forward walk alone could keep stale tokens. Lines after the region keep their
		// tokenization state, and the tokenizer walks forward again once an end state changes.
		if (invalidatedEndLineIndex >= invalidatedStartLineIndex)
			InvalidateLineRange(invalidatedStartLineIndex, invalidatedEndLineIndex);
	}

	/// <summary>
	/// Applies a document change to the snapshot and the model's line-state slots, and returns the inclusive line range whose tokenization state
	/// must be invalidated.
	/// </summary>
	/// <param name="change">The document change to apply.</param>
	/// <returns>The zero-based inclusive line range to invalidate.</returns>
	/// <remarks>
	/// A snapshot line carries no document terminator, so a line is affected exactly when the change touches its content or the terminator that joins
	/// it to its neighbor. Those are the lines from the one that contains the change start to the one that contains the end of the inserted text,
	/// both read from the new document. The text after the change end is identical in the old and the new document, so the lines after that last line
	/// are the same in number and content; only the lines in between are replaced, plus the line before them when the change removes no line.
	/// </remarks>
	private (int StartLineIndex, int EndLineIndex) ApplyChange(DocumentChangeEventArgs change)
	{
		int oldLineCount = _lineTexts.Count;
		int newLineCount = _document.LineCount;

		int firstLineIndex = _document.GetLineByOffset(change.Offset).LineNumber - 1;
		int lastLineIndex = _document.GetLineByOffset(change.Offset + change.InsertionLength).LineNumber - 1;

		int tailLineCount = newLineCount - 1 - lastLineIndex;
		int oldTailFirstLineIndex = oldLineCount - tailLineCount;
		int newTailFirstLineIndex = lastLineIndex + 1;

		// An insertion that lands between a carriage return and a line feed adds a line without removing one, because the old line the
		// carriage return ended is unchanged. No old slot would then be kept, and a fresh slot has no entering state, so the line
		// before the insertion joins the replaced region: its slot is kept and its state enters the first new line.
		if (oldTailFirstLineIndex == firstLineIndex)
			firstLineIndex--;

		Debug.Assert(firstLineIndex >= 0, "A change that removes no line starts after the first line.");

		Debug.Assert(
			oldTailFirstLineIndex > firstLineIndex,
			"The replaced region keeps at least one old line, so a slot carries the entering state."
		);

		ReplaceSnapshotLines(firstLineIndex, oldTailFirstLineIndex - firstLineIndex, newTailFirstLineIndex - firstLineIndex);

		// The slots of the tail lines stay bound to their lines: slots are inserted ahead of the tail or removed
		// from the end of the replaced region so that the old tail slots land on the new tail indexes. The slots
		// of the replaced region that remain are the ones the invalidated range re-tokenizes, and the first of them
		// keeps the state that entered the region, which is what its new first line must start from.
		ApplySlotDelta(oldTailFirstLineIndex, newTailFirstLineIndex, newLineCount - oldLineCount);

		return (firstLineIndex, lastLineIndex);
	}

	private void InitializeSnapshot()
	{
		lock (_syncRoot)
		{
			for (int i = 0; i < _document.LineCount; i++)
			{
				_lineTexts.Add(ReadDocumentLineText(i));
				AddLine(i);
			}
		}
	}

	private void ReplaceSnapshotLines(int startLineIndex, int removedLineCount, int insertedLineCount)
	{
		var insertedLines = new List<string>(insertedLineCount);

		for (int i = 0; i < insertedLineCount; i++)
			insertedLines.Add(ReadDocumentLineText(startLineIndex + i));

		_lineTexts.RemoveRange(startLineIndex, removedLineCount);
		_lineTexts.InsertRange(startLineIndex, insertedLines);
	}

	/// <summary>
	/// Reconciles the model's line-state slots with a line-count delta so every surviving line keeps the state object that belongs to it.
	/// </summary>
	/// <param name="oldTailFirstLineIndex">The old index of the first line that survives the change.</param>
	/// <param name="slotRemovalStartIndex">The index where removal starts for a shrinking change: the first line that survives the change.</param>
	/// <param name="lineDelta">The signed line-count delta.</param>
	private void ApplySlotDelta(int oldTailFirstLineIndex, int slotRemovalStartIndex, int lineDelta)
	{
		// AddLine/RemoveLine only mutate the model's private line-state list and never call back into the model.
		if (lineDelta > 0)
		{
			for (int i = 0; i < lineDelta; i++)
				AddLine(oldTailFirstLineIndex);
		}
		else if (lineDelta < 0)
		{
			for (int i = 0; i < -lineDelta; i++)
				RemoveLine(slotRemovalStartIndex);
		}
	}

	/// <summary>Reads a document line's content and ends it with the line feed the tokenizer expects after every line.</summary>
	/// <param name="lineIndex">The zero-based index of the line.</param>
	/// <returns>The line's text without its document terminator, followed by one line feed.</returns>
	private string ReadDocumentLineText(int lineIndex)
	{
		// A TextDocument always exposes at least one line, so the index is always in range.
		DocumentLine line = _document.GetLineByNumber(lineIndex + 1);
		return _document.GetText(line.Offset, line.Length) + SyntheticLineFeed;
	}
}

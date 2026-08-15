#if AVALONIAEDIT
using AvaloniaEdit.Document;
#else
using ICSharpCode.AvalonEdit.Document;
#endif
using TextMateSharp.Grammars;
using TextMateSharp.Model;

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.TextMate.Highlighting;
#else
namespace Nickelony.IDEKit.AvalonEdit.TextMate.Highlighting;
#endif

/// <summary>
/// Provides TextMateSharp with a zero-based view of the editor's <see cref="TextDocument"/> lines.
/// Each line includes its terminator when present, and the view is updated when the document changes.
/// </summary>
/// <remarks>
/// <para>
/// The view is an incrementally maintained snapshot: line texts are copied so the tokenizer can read
/// them from its background thread while the document stays owned by the UI thread. The snapshot stores
/// every line including its terminator, so it duplicates the document text and carries that cost for
/// large documents. Lines outside a change keep their tokenization state; the changed region is
/// re-read and invalidated.
/// </para>
/// <para>
/// Change handling derives the affected line range from the document's own line geometry, so the
/// snapshot, the model's line-state list, and the document always agree on the line count and every
/// snapshot line mirrors the document line including its terminator; edits that split or form a CRLF
/// pair also cover the line whose delimiter changes.
/// </para>
/// <para>
/// The snapshot members the tokenizer thread calls (<see cref="GetNumberOfLines"/>,
/// <see cref="GetLineLength"/>, and <see cref="GetLineTextIncludingTerminators"/>) are safe to call
/// from any thread: the snapshot is guarded by a private lock and those members only read it. Document
/// change handling runs on the thread that owns the document; <see cref="Dispose"/> only detaches that
/// handler. A tokenizer-thread read can briefly observe an update in flight, but reads are
/// range-guarded and an out-of-range read yields empty text and a zero length instead of throwing.
/// </para>
/// <para>
/// A line list backs exactly one <see cref="TMModel"/> because a model binds its line list when it is
/// constructed, and a second model would replace the first model's binding. Disposing the model also
/// disposes its line list, and this list's <see cref="Dispose"/> is idempotent, so disposing the model
/// is sufficient. A list that is disposed without its model keeps the model alive but freezes
/// tokenization on the last snapshot and stops receiving document updates; dispose the model before
/// dropping the list.
/// </para>
/// </remarks>
public sealed class TextMateDocumentLineList : AbstractLineList
{
	private readonly TextDocument _document;
	private readonly object _syncRoot = new();
	private readonly List<string> _lineTexts = [];

	// The number of line-state slots this list has added to the model's list. TextMateSharp keeps that
	// list private, so the count is tracked here and used to reconcile the slots in the self-healing
	// path of a change that detects an inconsistent state.
	private int _slotCount;

	/// <summary>
	/// Initializes a new instance of the <see cref="TextMateDocumentLineList"/> class.
	/// </summary>
	/// <param name="document">The document whose lines are tracked.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="document"/> is <see langword="null"/>.
	/// </exception>
	public TextMateDocumentLineList(TextDocument document)
	{
		ArgumentNullException.ThrowIfNull(document);
		_document = document;

		InitializeSnapshot();

		_document.Changed += Document_Changed;
	}

	/// <inheritdoc/>
	/// <remarks>
	/// The snapshot is always current, so this member only marks the line for re-tokenization; it
	/// never re-reads the document.
	/// </remarks>
	public override void UpdateLine(int lineIndex)
		=> InvalidateLine(lineIndex);

	/// <inheritdoc/>
	public override int GetNumberOfLines()
	{
		lock (_syncRoot)
			return _lineTexts.Count;
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Unlike the base contract, an out-of-range index yields an empty line instead of throwing. The
	/// tokenizer can probe an index that a concurrent document update has already removed, and an empty
	/// line lets that read complete so the failed line is re-tokenized by the next pass.
	/// </remarks>
	public override LineText GetLineTextIncludingTerminators(int lineIndex)
	{
		lock (_syncRoot)
		{
			if (lineIndex < 0 || lineIndex >= _lineTexts.Count)
				return new LineText(string.Empty);

			return new LineText(_lineTexts[lineIndex]);
		}
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Unlike the base contract, an out-of-range index yields zero instead of throwing; see
	/// <see cref="GetLineTextIncludingTerminators"/> for the rationale.
	/// </remarks>
	public override int GetLineLength(int lineIndex)
	{
		lock (_syncRoot)
		{
			if (lineIndex < 0 || lineIndex >= _lineTexts.Count)
				return 0;

			return _lineTexts[lineIndex].Length;
		}
	}

	/// <inheritdoc/>
	public override void Dispose()
		=> _document.Changed -= Document_Changed;

	private void Document_Changed(object? sender, DocumentChangeEventArgs e)
	{
		int invalidatedStartLineIndex;
		int invalidatedEndLineIndex;

		// Lock-order contract: TextMateSharp's model acquires its own lock before calling into
		// IModelLines, so this list's lock must never be held while calling back into the model
		// (the order is always model lock -> list lock, never the reverse).
		lock (_syncRoot)
			(invalidatedStartLineIndex, invalidatedEndLineIndex) = ApplyChange(e);

		// Outside _syncRoot: InvalidateLineRange calls TMModel.InvalidateLineRange, which marks the whole
		// range and queues it under a single model lock and signal. Every line of the changed region is
		// invalidated because new lines can land on slots that previously held removed lines: such slots
		// are neither fresh nor guaranteed to have an end state that differs from their predecessor, so
		// the model's forward walk alone could keep stale tokens. Lines after the region keep their
		// tokenization state, and the tokenizer walks forward again once an end state changes.
		if (invalidatedEndLineIndex >= invalidatedStartLineIndex)
			InvalidateLineRange(invalidatedStartLineIndex, invalidatedEndLineIndex);
	}

	/// <summary>
	/// Applies a document change to the snapshot and the model's line-state slots, and returns the
	/// inclusive line range whose tokenization state must be invalidated.
	/// </summary>
	/// <param name="change">The document change to apply.</param>
	/// <returns>The zero-based inclusive line range to invalidate.</returns>
	private (int StartLineIndex, int EndLineIndex) ApplyChange(DocumentChangeEventArgs change)
	{
		int oldLineCount = _lineTexts.Count;
		int newLineCount = _document.LineCount;

		if (!TryComputeAffectedLines(change, oldLineCount, newLineCount, out AffectedLines affected))
		{
			RebuildSnapshot();
			return (0, Math.Max(0, newLineCount - 1));
		}

		int removedLineCount = affected.OldTailFirstLineIndex - affected.FirstAffectedLineIndex;
		int insertedLineCount = affected.NewTailFirstLineIndex - affected.FirstAffectedLineIndex;
		int lineDelta = newLineCount - oldLineCount;

		// The tail line counts must agree for the change to be expressible as one replaced line range;
		// a disagreement means the tracked state is not consistent with the document geometry.
		if (removedLineCount < 0
			|| insertedLineCount < 0
			|| affected.OldTailFirstLineIndex > oldLineCount
			|| affected.NewTailFirstLineIndex > newLineCount
			|| removedLineCount - insertedLineCount != oldLineCount - newLineCount
			|| _slotCount != oldLineCount)
		{
			RebuildSnapshot();
			return (0, Math.Max(0, newLineCount - 1));
		}

		ReplaceSnapshotLines(affected.FirstAffectedLineIndex, removedLineCount, insertedLineCount);

		// A pure whole-line deletion removes lines without inserting content, so nothing in the changed
		// region is re-tokenized and the first preserved tail line takes the deleted block's place: it
		// must keep the state that entered the first deleted line (the state after the last kept line),
		// not the state that followed the deleted block. Every other edit keeps the old tail slots bound
		// to their lines; the invalidated range re-tokenizes the changed region and the tokenizer
		// propagates fresh end states forward into the tail.
		bool isPureLineDeletion = insertedLineCount == 0 && removedLineCount > 0;
		int slotRemovalStartIndex = isPureLineDeletion
			? affected.FirstAffectedLineIndex + 1
			: affected.NewTailFirstLineIndex;

		ApplySlotDelta(affected.OldTailFirstLineIndex, slotRemovalStartIndex, lineDelta);
		_slotCount = newLineCount;

		// The first preserved tail line is invalidated for the same reason: its stored tokens were
		// computed from the removed prefix and must be recomputed from the corrected state. It is the
		// only affected line of a pure deletion, which is why the usual changed-content range is empty.
		int lastInvalidatedLineIndex = isPureLineDeletion
			? affected.NewTailFirstLineIndex
			: affected.NewTailFirstLineIndex - 1;

		return (affected.FirstAffectedLineIndex, lastInvalidatedLineIndex);
	}

	/// <summary>
	/// Computes the line ranges an edit replaces by aligning the old and the new line geometry at the
	/// unchanged text around the change.
	/// </summary>
	/// <param name="change">The document change to analyze.</param>
	/// <param name="oldLineCount">The snapshot's line count before the change.</param>
	/// <param name="newLineCount">The document's line count after the change.</param>
	/// <param name="affected">The computed line ranges when the method returns <see langword="true"/>.</param>
	/// <returns><see langword="true"/> when the affected ranges could be computed.</returns>
	private bool TryComputeAffectedLines(
		DocumentChangeEventArgs change,
		int oldLineCount,
		int newLineCount,
		out AffectedLines affected)
	{
		affected = default;

		int changeOffset = change.Offset;
		int changeEndOffset = changeOffset + change.InsertionLength;

		// The first affected line is the line that contains the first changed character. All text before
		// the change is unchanged, so that line has the same index in the old and the new document; when
		// the change starts exactly where the previous line ends, the change begins on the next line and
		// the previous line keeps its tokenization state.
		int firstAffectedLineIndex = ComputeFirstAffectedLineIndex(change);

		// The preserved tail starts where the line geometry realigns after the change end. The first new
		// line that begins at or after the change end is clean when its start position was also a line
		// start in the old document; otherwise, it merged with the changed region, and the line after it
		// is the first clean tail line.
		int newTailFirstLineIndex;
		DocumentLine endLine = _document.GetLineByOffset(changeEndOffset);

		if (endLine.Offset == changeEndOffset)
		{
			newTailFirstLineIndex = OldTextEndsWithLineTerminator(change)
				? endLine.LineNumber - 1
				: endLine.LineNumber;
		}
		else
		{
			newTailFirstLineIndex = endLine.LineNumber < newLineCount
				? endLine.LineNumber
				: newLineCount;
		}

		// Old and new tail lines stay in a fixed relative order, so the old tail starts one line-count
		// delta before the new tail.
		int oldTailFirstLineIndex = oldLineCount - (newLineCount - newTailFirstLineIndex);

		if (firstAffectedLineIndex < 0
			|| firstAffectedLineIndex > oldLineCount - 1
			|| firstAffectedLineIndex > newLineCount - 1
			|| oldTailFirstLineIndex < firstAffectedLineIndex
			|| newTailFirstLineIndex < firstAffectedLineIndex)
		{
			return false;
		}

		affected = new AffectedLines(firstAffectedLineIndex, oldTailFirstLineIndex, newTailFirstLineIndex);
		return true;
	}

	/// <summary>
	/// Computes the first line whose content changes, which is the line that contains the first changed
	/// character.
	/// </summary>
	/// <param name="change">The document change to analyze.</param>
	/// <returns>The zero-based index of the first changed line.</returns>
	private int ComputeFirstAffectedLineIndex(DocumentChangeEventArgs change)
	{
		if (change.Offset == 0)
			return 0;

		int previousLineIndex = GetDocumentLineIndex(change.Offset - 1);

		// The previous line keeps its text and delimiter when the change starts at a line boundary and
		// leaves that boundary intact; OldLineEndsAt also rejects a carriage return that pairs with a
		// line feed only after the change, which would change the previous line's delimiter.
		return OldLineEndsAt(change, change.Offset)
			? previousLineIndex + 1
			: previousLineIndex;
	}

	/// <summary>
	/// Determines whether the old line that contains the given position ends exactly there, which makes
	/// that position the start of a line.
	/// </summary>
	/// <param name="change">The document change being analyzed.</param>
	/// <param name="offset">The zero-based offset just after the line content to test.</param>
	/// <returns><see langword="true"/> when the old line ends at the offset.</returns>
	private bool OldLineEndsAt(DocumentChangeEventArgs change, int offset)
	{
		// The character before an offset is unchanged by definition, so the document still holds it.
		char previousChar = _document.GetCharAt(offset - 1);

		if (previousChar == '\n')
			return true;

		if (previousChar != '\r')
			return false;

		// A carriage return ends the old line only when the character that followed it in the old text
		// was not a line feed, and it must not be followed by a line feed in the new text either:
		// removing or inserting text between a carriage return and a line feed forms or splits a CRLF
		// pair, which changes the previous line's delimiter even though the change starts at the old
		// line boundary.
		char followingOldChar;

		if (change.RemovalLength > 0)
		{
			followingOldChar = change.RemovedText.Text[0];
		}
		else
		{
			int followingOldOffset = offset + change.InsertionLength;

			followingOldChar = followingOldOffset >= _document.TextLength
				? '\0'
				: _document.GetCharAt(followingOldOffset);
		}

		if (followingOldChar == '\n')
			return false;

		// The character right after the carriage return in the new text sits at the change start - the
		// first inserted character, or the unchanged character for a pure removal - so the carriage
		// return only ends the old line when that character is not a line feed that would re-form a CRLF
		// pair.
		return offset >= _document.TextLength
			|| _document.GetCharAt(offset) != '\n';
	}

	/// <summary>
	/// Determines whether the old text ends with a line terminator at the change end, which decides
	/// whether a line boundary just after the change survives the edit.
	/// </summary>
	/// <param name="change">The document change to analyze.</param>
	/// <returns><see langword="true"/> when the old text ends with a line terminator.</returns>
	private bool OldTextEndsWithLineTerminator(DocumentChangeEventArgs change)
	{
		char lastOldChar;

		if (change.RemovalLength > 0)
		{
			string removedText = change.RemovedText.Text;

			if (removedText.Length == 0)
				return false;

			lastOldChar = removedText[^1];
		}
		else
		{
			// A pure insertion: the character before the insertion point is unchanged, so the
			// document still holds it.
			if (change.Offset == 0)
				return true;

			lastOldChar = _document.GetCharAt(change.Offset - 1);
		}

		if (lastOldChar == '\n')
			return true;

		if (lastOldChar != '\r')
			return false;

		// A carriage return only ends the line when the character that followed it in the old text
		// is not a line feed: the character after the change is unchanged, so a line feed there makes
		// the carriage return the first half of a CRLF pair whose boundary lies beyond the change end.
		int followingOldOffset = change.Offset + change.InsertionLength;

		return followingOldOffset >= _document.TextLength
			|| _document.GetCharAt(followingOldOffset) != '\n';
	}

	private void InitializeSnapshot()
	{
		lock (_syncRoot)
			RebuildSnapshot();
	}

	private void ReplaceSnapshotLines(int startLineIndex, int removedLineCount, int insertedLineCount)
	{
		var insertedLines = new List<string>(insertedLineCount);

		for (int i = 0; i < insertedLineCount; i++)
			insertedLines.Add(ReadDocumentLineText(startLineIndex + i));

		if (removedLineCount > 0)
			_lineTexts.RemoveRange(startLineIndex, removedLineCount);

		if (insertedLines.Count > 0)
			_lineTexts.InsertRange(startLineIndex, insertedLines);
	}

	/// <summary>
	/// Reconciles the model's line-state slots with a line-count delta so every surviving line keeps the
	/// state object that belongs to it.
	/// </summary>
	/// <param name="oldTailFirstLineIndex">The old index of the first line that survives the change.</param>
	/// <param name="slotRemovalStartIndex">
	/// The index where removal starts for a shrinking change: the first line that survives the change for
	/// every edit, or one past the first affected line for a pure whole-line deletion, whose first preserved
	/// tail line must keep the state that entered the first deleted line.
	/// </param>
	/// <param name="lineDelta">The signed line-count delta.</param>
	private void ApplySlotDelta(int oldTailFirstLineIndex, int slotRemovalStartIndex, int lineDelta)
	{
		// The tail line states stay bound to their lines: slots ahead of the tail are inserted or
		// removed so that the old tail slots land on the new tail indexes. AddLine/RemoveLine only
		// mutate the model's private line-state list and never call back into the model.
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

	/// <summary>
	/// Rebuilds the snapshot for the whole document and reconciles the model's line-state slots with it,
	/// used for the initial snapshot and as a self-healing fallback when a change detects an
	/// inconsistent state.
	/// </summary>
	private void RebuildSnapshot()
	{
		_lineTexts.Clear();

		for (int i = 0; i < _document.LineCount; i++)
			_lineTexts.Add(ReadDocumentLineText(i));

		while (_slotCount > _lineTexts.Count)
		{
			RemoveLine(_slotCount - 1);
			_slotCount--;
		}

		while (_slotCount < _lineTexts.Count)
		{
			AddLine(_slotCount);
			_slotCount++;
		}
	}

	private int GetDocumentLineIndex(int offset)
	{
		DocumentLine line = _document.GetLineByOffset(offset);
		return line.LineNumber - 1;
	}

	private string ReadDocumentLineText(int lineIndex)
	{
		// A TextDocument always exposes at least one line, so the index is always in range.
		DocumentLine line = _document.GetLineByNumber(lineIndex + 1);
		return _document.GetText(line.Offset, line.TotalLength);
	}

	/// <summary>
	/// Describes the line ranges a change replaces: the first affected line and the first line of the
	/// preserved tail in both the old and the new line coordinates.
	/// </summary>
	private readonly record struct AffectedLines(
		int FirstAffectedLineIndex,
		int OldTailFirstLineIndex,
		int NewTailFirstLineIndex);
}

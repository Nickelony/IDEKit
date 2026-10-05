using Nickelony.IDEKit.Core.Text;
#if AVALONIAEDIT
using AvaloniaEdit.Document;
#else
using ICSharpCode.AvalonEdit.Document;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.Text;

#else

namespace Nickelony.IDEKit.AvalonEdit.Text;

#endif

/// <summary>An immutable <see cref="ITextSnapshot"/> of the editor's <see cref="TextDocument"/>, including its text and optional file name.</summary>
/// <remarks>
/// The capture wraps <see cref="TextDocument.CreateSnapshot()"/> in amortized constant time and also reads the file name,
/// so call it on the document's owner thread; the result itself is safe to read from a background thread and does not observe later document changes.
/// Line metadata is materialized from the captured text on first access, so a caller that reads only text never pays for the scan.
/// </remarks>
public sealed class TextDocumentSnapshot : ITextSnapshot
{
	private readonly WeakReference<TextDocument> _sourceDocument;
	private readonly ITextSource _snapshot;

	private StringTextSnapshot? _lineSnapshot;

	/// <param name="document">The editor's <see cref="TextDocument"/> to capture.</param>
	/// <exception cref="ArgumentNullException"><paramref name="document"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// The captured document identifier is the value of <see cref="TextDocument.FileName"/> as the host set it on the document;
	/// this package never writes that property, so a host that tracks document identity elsewhere must keep the document's file name in sync
	/// for snapshots to report it.
	/// </remarks>
	public TextDocumentSnapshot(TextDocument document)
	{
		ArgumentNullException.ThrowIfNull(document);

		DocumentId = document.FileName;
		_sourceDocument = new WeakReference<TextDocument>(document);
		_snapshot = document.CreateSnapshot();
	}

	/// <summary>
	/// Returns the editor document this snapshot was captured from, or <see langword="null"/> after that document has been collected.
	/// </summary>
	/// <remarks>
	/// The reference is weak, so a snapshot never extends the document's lifetime; the snapshot still reads its text from the detached capture.
	/// An editing service that keys per-document state (for example auto-closing insertion tracking) uses this to recover the document
	/// from a snapshot it hands to an editor-neutral resolver.
	/// </remarks>
	internal TextDocument? SourceDocument => _sourceDocument.TryGetTarget(out TextDocument? document) ? document : null;

	/// <inheritdoc/>
	public string? DocumentId { get; }

	/// <inheritdoc/>
	public int TextLength => _snapshot.TextLength;

	/// <inheritdoc/>
	public int LineCount => GetLineSnapshot().LineCount;

	/// <inheritdoc/>
	public char GetCharAt(int offset)
	{
		// The wrapper enforces the ITextSnapshot argument contract; the underlying engine
		// snapshot throws different exception types for some invalid ranges.
		ArgumentOutOfRangeException.ThrowIfNegative(offset);
		ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(offset, _snapshot.TextLength);

		return _snapshot.GetCharAt(offset);
	}

	/// <inheritdoc/>
	public string GetText(int offset, int length)
	{
		// The wrapper enforces the ITextSnapshot argument contract; the underlying engine
		// snapshot throws different exception types for some invalid ranges (for example an
		// overflow for a negative length).
		TextOffsetValidation.ValidateOffset(
			textLength: _snapshot.TextLength,
			offset,
			parameterName: nameof(offset),
			offsetDescription: "document snapshot"
		);

		ArgumentOutOfRangeException.ThrowIfNegative(length);

		TextOffsetValidation.ValidateRange(
			textLength: _snapshot.TextLength,
			range: new TextRange(offset, length),
			parameterName: nameof(length),
			rangeDescription: "document snapshot"
		);

		return _snapshot.GetText(offset, length);
	}

	/// <inheritdoc/>
	public ITextLine GetLineByOffset(int offset) => GetLineSnapshot().GetLineByOffset(offset);

	/// <inheritdoc/>
	public ITextLine GetLineByNumber(int lineNumber) => GetLineSnapshot().GetLineByNumber(lineNumber);

	/// <inheritdoc/>
	public IReadOnlyList<ITextLine> Lines => GetLineSnapshot().Lines;

	/// <summary>Returns the line metadata of the snapshot, materializing it from the captured text on first access.</summary>
	/// <remarks>
	/// The field is read and published with <see cref="Volatile"/> so a reader on another thread observes a fully
	/// constructed snapshot instead of a plain write with no publication guarantee. A racing materialization is harmless: either instance describes
	/// the same immutable captured text, so whichever wins changes no observable state.
	/// </remarks>
	private StringTextSnapshot GetLineSnapshot()
	{
		StringTextSnapshot? lineSnapshot = Volatile.Read(ref _lineSnapshot);

		if (lineSnapshot is not null)
			return lineSnapshot;

		lineSnapshot = new StringTextSnapshot(_snapshot.Text, DocumentId);
		Volatile.Write(ref _lineSnapshot, lineSnapshot);

		return lineSnapshot;
	}
}

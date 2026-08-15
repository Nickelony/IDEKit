#if AVALONIAEDIT
using AvaloniaEdit.Document;
#else
using ICSharpCode.AvalonEdit.Document;
#endif
using Nickelony.IDEKit.Core.Text;

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.Documents;
#else
namespace Nickelony.IDEKit.AvalonEdit.Documents;
#endif

/// <summary>
/// An immutable <see cref="ITextSnapshot"/> of the editor's <see cref="TextDocument"/>, including its
/// text and optional file name.
/// </summary>
/// <remarks>
/// <para>
/// The snapshot wraps the document snapshot created by <see cref="TextDocument.CreateSnapshot()"/>, which
/// is captured in amortized constant time and is safe to read from a background thread. The capture also
/// reads the document's file name, so call it on the document's owner thread and hand the result to a
/// parser or request that reads it elsewhere.
/// </para>
/// <para>
/// Line metadata requires a full text scan and is materialized from the captured text on first access,
/// so a caller that only reads text or characters never pays for it, and the first line-metadata
/// access on a large document costs a scan proportional to its size. The snapshot is not a live view
/// and does not observe later document changes.
/// </para>
/// </remarks>
public sealed class TextDocumentSnapshot : ITextSnapshot
{
	private readonly string? _fileName;
	private readonly WeakReference<TextDocument> _sourceDocument;
	private readonly ITextSource _snapshot;

	private StringTextSnapshot? _lineSnapshot;

	/// <summary>
	/// Initializes a new instance of the <see cref="TextDocumentSnapshot"/> class.
	/// </summary>
	/// <remarks>
	/// Capturing reads the document's file name and creates its snapshot, so call this on the document's
	/// owner thread. The returned snapshot itself can be read from any thread. The captured file name is
	/// the value of <see cref="TextDocument.FileName"/> as the host set it on the document; this package
	/// never writes that property, so a host that tracks document identity elsewhere must keep the
	/// document's file name in sync for snapshots to report it.
	/// </remarks>
	/// <param name="document">The editor's <see cref="TextDocument"/> to capture.</param>
	/// <exception cref="ArgumentNullException"><paramref name="document"/> is <see langword="null"/>.</exception>
	public TextDocumentSnapshot(TextDocument document)
	{
		ArgumentNullException.ThrowIfNull(document);

		_fileName = document.FileName;
		_sourceDocument = new WeakReference<TextDocument>(document);
		_snapshot = document.CreateSnapshot();
	}

	/// <summary>
	/// Gets the editor document this snapshot was captured from, or <see langword="null"/> after that
	/// document has been collected.
	/// </summary>
	/// <remarks>
	/// The reference is weak, so a snapshot never extends the document's lifetime; the snapshot still reads
	/// its text from the detached capture. An editing service that keys per-document state (for example
	/// auto-closing insertion tracking) uses this to recover the document from a snapshot it hands to an
	/// editor-neutral resolver.
	/// </remarks>
	internal TextDocument? SourceDocument
		=> _sourceDocument.TryGetTarget(out TextDocument? document) ? document : null;

	/// <inheritdoc/>
	public string? FileName => _fileName;

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
		ArgumentOutOfRangeException.ThrowIfNegative(offset);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, _snapshot.TextLength);
		ArgumentOutOfRangeException.ThrowIfNegative(length);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(length, _snapshot.TextLength - offset);

		return _snapshot.GetText(offset, length);
	}

	/// <inheritdoc/>
	public ITextLine GetLineByOffset(int offset)
		=> GetLineSnapshot().GetLineByOffset(offset);

	/// <inheritdoc/>
	public ITextLine GetLineByNumber(int lineNumber)
		=> GetLineSnapshot().GetLineByNumber(lineNumber);

	/// <inheritdoc/>
	public IReadOnlyList<ITextLine> Lines => GetLineSnapshot().Lines;

	/// <summary>
	/// Gets the line metadata of the snapshot, materializing it from the captured text on first access.
	/// </summary>
	/// <remarks>
	/// The field is read and published with <see cref="Volatile"/> so a reader on another thread observes a
	/// fully constructed snapshot instead of a plain write with no publication guarantee. A racing
	/// materialization is harmless: both instances describe the same immutable captured text, so either
	/// instance may win without changing observable state.
	/// </remarks>
	private StringTextSnapshot GetLineSnapshot()
	{
		StringTextSnapshot? lineSnapshot = Volatile.Read(ref _lineSnapshot);

		if (lineSnapshot is not null)
			return lineSnapshot;

		lineSnapshot = new StringTextSnapshot(_snapshot.Text, _fileName);
		Volatile.Write(ref _lineSnapshot, lineSnapshot);

		return lineSnapshot;
	}
}

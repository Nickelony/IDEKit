using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>
/// An <see cref="ITextSnapshot"/> that captures its content eagerly and builds line metadata on first use.
/// </summary>
/// <remarks>
/// The document store creates a snapshot for every operation result, including failure snapshots taken
/// under its state lock. Building the full line table for each of those snapshots costs time and
/// allocations proportional to the document size, so the table is deferred until a line-oriented member
/// is queried; character and range queries answer directly from the captured string. The captured string
/// is immutable, so deferring the table does not change snapshot semantics.
/// </remarks>
internal sealed class DeferredTextSnapshot : ITextSnapshot
{
	private readonly string _text;
	private StringTextSnapshot? _lineSnapshot;

	public DeferredTextSnapshot(string text, string? fileName)
	{
		_text = text;
		FileName = fileName;
	}

	/// <inheritdoc/>
	public string? FileName { get; }

	/// <summary>
	/// Gets the captured content string.
	/// </summary>
	/// <remarks>
	/// The captured string is immutable, so the snapshot can hand it out directly instead of copying a
	/// substring when the complete content is requested.
	/// </remarks>
	public string Text => _text;

	/// <inheritdoc/>
	public int TextLength => _text.Length;

	/// <inheritdoc/>
	public int LineCount => LineSnapshot.LineCount;

	/// <inheritdoc/>
	public IReadOnlyList<ITextLine> Lines => LineSnapshot.Lines;

	/// <inheritdoc/>
	public char GetCharAt(int offset)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(offset);
		ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(offset, _text.Length);

		return _text[offset];
	}

	/// <inheritdoc/>
	public string GetText(int offset, int length)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(offset);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, _text.Length);

		ArgumentOutOfRangeException.ThrowIfNegative(length);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(length, _text.Length - offset);

		// The captured string is immutable, so a full-range request can hand out the captured
		// instance instead of copying the whole content into a substring.
		return offset == 0 && length == _text.Length
			? _text
			: _text.Substring(offset, length);
	}

	/// <inheritdoc/>
	public ITextLine GetLineByOffset(int offset)
		=> LineSnapshot.GetLineByOffset(offset);

	/// <inheritdoc/>
	public ITextLine GetLineByNumber(int lineNumber)
		=> LineSnapshot.GetLineByNumber(lineNumber);

	private StringTextSnapshot LineSnapshot
		=> LazyInitializer.EnsureInitialized(
			ref _lineSnapshot,
			() => new StringTextSnapshot(_text, FileName));
}

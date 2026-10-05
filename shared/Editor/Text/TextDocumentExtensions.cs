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

/// <summary>Extension methods for the editor's <see cref="TextDocument"/> instances.</summary>
public static class TextDocumentExtensions
{
	/// <summary>Clamps a zero-based document offset to the document's character range.</summary>
	/// <param name="document">The editor's <see cref="TextDocument"/> whose <see cref="TextDocument.TextLength"/> defines the upper bound.</param>
	/// <param name="offset">The zero-based offset to clamp.</param>
	/// <returns>The offset clamped to the inclusive range from <c>0</c> through <see cref="TextDocument.TextLength"/>.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="document"/> is <see langword="null"/>.</exception>
	public static int ClampOffset(this TextDocument document, int offset)
	{
		ArgumentNullException.ThrowIfNull(document);
		return Math.Clamp(offset, 0, document.TextLength);
	}

	/// <summary>Returns the supplied line when it is a live line of this document; otherwise, throws.</summary>
	/// <param name="document">The document the line must belong to.</param>
	/// <param name="line">The line handle to resolve.</param>
	/// <returns>The supplied line, which belongs to this document.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="document"/> or <paramref name="line"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException"><paramref name="line"/> was deleted, or it belongs to another document.</exception>
	/// <remarks>
	/// Callers that accept a line handle from external code resolve it through this method before using its offsets or its neighboring lines.
	/// </remarks>
	public static DocumentLine GetLiveLine(this TextDocument document, DocumentLine line)
	{
		ArgumentNullException.ThrowIfNull(document);
		ArgumentNullException.ThrowIfNull(line);

		// LineNumber and Offset throw on a deleted line, so deletion is tested before the number is read.
		if (line.IsDeleted || line.LineNumber > document.LineCount || !ReferenceEquals(document.GetLineByNumber(line.LineNumber), line))
			throw new ArgumentException("The line is not a live line of this document.", nameof(line));

		return line;
	}
}

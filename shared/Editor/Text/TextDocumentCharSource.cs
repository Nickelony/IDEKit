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

/// <summary>Adapts the live <see cref="TextDocument"/> to the character source the shared scanners read, without allocating or boxing.</summary>
/// <param name="document">The document to read.</param>
internal readonly struct TextDocumentCharSource(TextDocument document) : ITextCharSource
{
	/// <inheritdoc/>
	public int TextLength => document.TextLength;

	/// <inheritdoc/>
	public char GetCharAt(int offset) => document.GetCharAt(offset);
}

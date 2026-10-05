using Nickelony.IDEKit.Core.Text;
#if AVALONIAEDIT
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.Text;
#else
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Text;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.AutoClosing;

#else

namespace Nickelony.IDEKit.AvalonEdit.AutoClosing;

#endif

/// <summary>
/// Matches text against the editor's <see cref="TextDocument"/> through the shared <see cref="TextMatchProbe"/>, so the auto-closing service
/// and its insertion tracker use one rule for "does this text occur at this offset".
/// </summary>
internal static class DocumentTextProbe
{
	/// <summary>Determines whether <paramref name="expectedText"/> occurs at <paramref name="offset"/> in <paramref name="document"/>.</summary>
	/// <param name="document">The document to match against.</param>
	/// <param name="offset">The zero-based offset to compare at.</param>
	/// <param name="expectedText">The text to match.</param>
	/// <returns><see langword="true"/> when the text matches at the offset; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// An empty text or a range outside the document matches nothing.
	/// </remarks>
	internal static bool MatchesAt(TextDocument document, int offset, string expectedText) =>
		TextMatchProbe.MatchesAt(new TextDocumentCharSource(document), offset, expectedText);
}

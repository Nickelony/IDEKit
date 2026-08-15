#if AVALONIAEDIT
using AvaloniaEdit.Document;
#else
using ICSharpCode.AvalonEdit.Document;
#endif
using Nickelony.IDEKit.Infrastructure;

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.Editing;
#else
namespace Nickelony.IDEKit.AvalonEdit.Editing;
#endif

/// <summary>
/// Matches text against the editor's <see cref="TextDocument"/> through the shared
/// <see cref="TextMatchProbe"/>, so the auto-closing service and its insertion tracker use one rule for
/// "does this text occur at this offset".
/// </summary>
internal static class DocumentTextMatch
{
	/// <summary>
	/// Determines whether <paramref name="expectedText"/> occurs at <paramref name="offset"/> in
	/// <paramref name="document"/>. An empty text or a range outside the document matches nothing.
	/// </summary>
	/// <param name="document">The document to match against.</param>
	/// <param name="offset">The zero-based offset to compare at.</param>
	/// <param name="expectedText">The text to match.</param>
	/// <returns><see langword="true"/> when the text matches at the offset; otherwise, <see langword="false"/>.</returns>
	internal static bool MatchesAt(TextDocument document, int offset, string expectedText)
		=> TextMatchProbe.MatchesAt(new TextDocumentCharSource(document), offset, expectedText);

	// Adapts the live document to the shared matcher without allocating: the struct is matched through
	// the generic probe, so no boxing or delegate is involved.
	private readonly struct TextDocumentCharSource(TextDocument document) : ITextCharSource
	{
		/// <inheritdoc/>
		public int TextLength => document.TextLength;

		/// <inheritdoc/>
		public char GetCharAt(int offset) => document.GetCharAt(offset);
	}
}

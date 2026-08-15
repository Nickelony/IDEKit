#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.Markdown;
#else
namespace Nickelony.IDEKit.AvalonEdit.Markdown;
#endif

/// <summary>
/// Normalizes the text of a code block before it is shown in a code surface.
/// </summary>
/// <remarks>
/// The rules are toolkit-neutral and shared by the editor bindings, so a code block renders the same way
/// whichever editor package produces it.
/// </remarks>
internal static class MarkdownCodeText
{
	/// <summary>
	/// Normalizes code-block text: line endings become line feeds, a block whose whole content is blank
	/// becomes empty, and the single newline that terminates the last line is removed.
	/// </summary>
	/// <param name="code">The raw code text taken from the parsed document.</param>
	/// <returns>The normalized code text.</returns>
	internal static string Normalize(string code)
	{
		string normalizedCode = Nickelony.IDEKit.Core.Text.LineTerminatorNormalizer.NormalizeToLineFeeds(code);

		// A block whose whole content is blank carries nothing to display, so it normalizes to empty.
		if (string.IsNullOrWhiteSpace(normalizedCode))
			return string.Empty;

		// A fenced code block conventionally ends with the newline that terminates its last line; that
		// single terminator is removed so the block does not render a trailing empty line, while any
		// additional trailing blank lines the author wrote are preserved (mainstream Markdown
		// renderers keep them).
		return normalizedCode.EndsWith('\n') ? normalizedCode[..^1] : normalizedCode;
	}
}

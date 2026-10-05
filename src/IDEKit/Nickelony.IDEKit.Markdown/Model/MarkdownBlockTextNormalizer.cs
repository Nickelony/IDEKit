using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Markdown.Model;

/// <summary>Normalizes the text of a code or HTML block before it is rendered.</summary>
internal static class MarkdownBlockTextNormalizer
{
	/// <summary>
	/// Normalizes block text: line endings become line feeds, a block whose whole content is blank becomes empty, and the single line feed
	/// that terminates the last line is removed.
	/// </summary>
	/// <param name="text">The raw text of the block.</param>
	/// <returns>The normalized text.</returns>
	internal static string Normalize(string text)
	{
		string normalizedText = LineTerminatorNormalizer.NormalizeToLineFeeds(text);

		// A block whose whole content is blank carries nothing to display, so it normalizes to empty.
		if (string.IsNullOrWhiteSpace(normalizedText))
			return string.Empty;

		// A block conventionally ends with the line feed that terminates its last line; that single terminator
		// is removed so the block does not render a trailing empty line, while any additional trailing blank
		// lines the author wrote are preserved (mainstream Markdown renderers keep them).
		return normalizedText.EndsWith('\n') ? normalizedText[..^1] : normalizedText;
	}
}

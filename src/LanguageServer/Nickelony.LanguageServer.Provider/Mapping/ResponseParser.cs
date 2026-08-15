using Nickelony.IDEKit.Infrastructure;
using Nickelony.IDEKit.IntelliSense;
using System.Text.Json;

namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Parses typed language-server (LSP) responses into shared text-model types (completion items, hover info,
/// definition locations, reference locations, workspace edits, signature help, formatting edits, document
/// symbols, and code actions).
/// </summary>
internal static partial class ResponseParser
{
	/// <summary>
	/// Determines whether a protocol range is ordered - its end is at or after its start.
	/// </summary>
	/// <remarks>
	/// Range consumers validate the raw protocol coordinates with this rule before clamping, because clamping
	/// can collapse an inverted range into a zero-length range that would otherwise pass as valid.
	/// </remarks>
	/// <param name="start">The range start position.</param>
	/// <param name="end">The range end position.</param>
	/// <returns><see langword="true"/> when the range is ordered.</returns>
	internal static bool IsOrderedRange(ProtocolPosition start, ProtocolPosition end)
		=> start.Line < end.Line || (start.Line == end.Line && start.Character <= end.Character);

	/// <summary>
	/// Extracts an LSP markup payload and applies the parser's shared markup policy: Markdown is
	/// line-ending normalized, plain text is trimmed, and a blank result means the content is absent.
	/// </summary>
	/// <remarks>
	/// Completion documentation, hover content, and signature-help documentation all read a payload
	/// through this method, so the three paths agree on how the text is normalized and on what "no
	/// content" means. Completion and hover forward the result as-is; signature help adds its
	/// plain-text-only fence projection on top.
	/// </remarks>
	/// <param name="element">The protocol markup payload to extract.</param>
	/// <returns>
	/// The normalized content, or <see langword="default"/> when the payload carries no usable text.
	/// </returns>
	internal static ProtocolMarkupContent ParseMarkupContent(JsonElement element)
	{
		ProtocolMarkupContent content = MarkupContentReader.ExtractContent(element);

		// Markdown keeps its surrounding whitespace because it carries indented code blocks and hard
		// line breaks; only its line endings are normalized. Plain text is trimmed. Either way a blank
		// result means the payload carried no usable content.
		string? text = content.Kind == TextMarkupKind.Markdown
			? MarkupContentReader.NormalizeMarkdownText(content.Text)
			: OptionalText.Normalize(content.Text);

		return string.IsNullOrWhiteSpace(text)
			? default
			: new ProtocolMarkupContent(text, content.Kind);
	}
}

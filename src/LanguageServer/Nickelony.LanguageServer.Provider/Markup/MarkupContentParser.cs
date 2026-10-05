using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Markup;
using Lsp = Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider;

/// <summary>Holds the markup rules that the per-feature response parsers share.</summary>
internal static class MarkupContentParser
{
	/// <summary>
	/// Applies the parser's shared markup policy to a markup payload: Markdown is line-ending normalized, plain text is trimmed, and a blank result
	/// means the content is absent.
	/// </summary>
	/// <param name="payload">The protocol markup payload, or <see langword="null"/> when the server sent none.</param>
	/// <returns>The normalized content, or <see langword="default"/> when the payload carries no usable text.</returns>
	/// <remarks>
	/// Completion documentation, hover content, and signature-help documentation all read a payload through this method,
	/// so they agree on how the text is normalized and on what "no content" means. Completion and hover forward the result as-is; signature help adds
	/// its plain-text-only fence projection on top.
	/// </remarks>
	internal static MarkupContent? Parse(Lsp.MarkupContentPayload? payload)
	{
		if (payload is null)
			return null;

		MarkupKind kind = MarkupKindConversion.FromProtocol(payload.Kind);

		// Markdown keeps its surrounding whitespace because it carries indented code blocks and hard
		// line breaks; only its line endings are normalized. Plain text is trimmed. Either way a blank
		// result means the payload carried no usable content.
		string? text = kind == MarkupKind.Markdown ? NormalizeLineEndings(payload.Value) : OptionalTextNormalizer.Normalize(payload.Value);

		return string.IsNullOrWhiteSpace(text) ? null : new MarkupContent(text, kind);
	}

	private static string? NormalizeLineEndings(string? text) => text is null ? null : LineTerminatorNormalizer.NormalizeToLineFeeds(text);
}

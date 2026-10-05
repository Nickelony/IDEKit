using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Hover;
using Nickelony.IDEKit.LanguageFeatures.Markup;
using Lsp = Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider;

internal static class HoverResponseParser
{
	/// <summary>Parses hover content from an LSP hover response.</summary>
	/// <param name="response">The hover response payload, or <see langword="null"/> when unavailable.</param>
	/// <param name="lineMap">The line map of the document content the response range refers to.</param>
	/// <returns>The parsed hover info, or <see langword="null"/> when no usable content is present.</returns>
	internal static HoverInformation? Parse(Lsp.HoverResponse? response, TextLineMap lineMap)
	{
		if (response is null)
			return null;

		MarkupContent? hoverContent = MarkupContentParser.Parse(CombineContents(response.Contents));

		return hoverContent is null ? null : new HoverInformation(hoverContent) { Range = GetRange(response.Range, lineMap) };
	}

	/// <summary>Joins hover fragments into one markup payload.</summary>
	/// <param name="fragments">The fragments the server sent, in order.</param>
	/// <returns>The joined payload, or <see langword="null"/> when no fragment carries text.</returns>
	/// <remarks>
	/// Fragments with blank text are skipped, and the others are separated by a blank line. One Markdown fragment makes the joined payload Markdown,
	/// so the per-fragment kind collapses to the strongest kind the response carries.
	/// </remarks>
	private static Lsp.MarkupContentPayload? CombineContents(IReadOnlyList<Lsp.MarkupContentPayload> fragments)
	{
		var texts = new List<string>(fragments.Count);
		Lsp.MarkupKind kind = Lsp.MarkupKind.PlainText;

		foreach (Lsp.MarkupContentPayload fragment in fragments)
		{
			if (string.IsNullOrWhiteSpace(fragment.Value))
				continue;

			texts.Add(fragment.Value);

			if (fragment.Kind == Lsp.MarkupKind.Markdown)
				kind = Lsp.MarkupKind.Markdown;
		}

		return texts.Count == 0 ? null : new Lsp.MarkupContentPayload { Kind = kind, Value = string.Join("\n\n", texts) };
	}

	/// <summary>
	/// Converts the optional protocol range of a hover response into the shared zero-based offset range against the requested document snapshot.
	/// </summary>
	/// <param name="range">The protocol range payload, or <see langword="null"/> when the response carried none.</param>
	/// <param name="lineMap">The line map of the document content the range refers to.</param>
	/// <returns>The converted range, or <see langword="null"/> when the payload carries no usable range.</returns>
	private static TextRange? GetRange(Lsp.RangePayload? range, TextLineMap lineMap) =>
		ProtocolRangeConversion.TryGetOffsetRange(range, lineMap, out TextRange offsetRange) ? offsetRange : null;
}

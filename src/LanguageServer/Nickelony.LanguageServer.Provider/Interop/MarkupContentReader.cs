using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense;
using System.Text.Json;

namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Extracts typed markup content from standard LSP hover, completion, and signature-help payload shapes.
/// </summary>
public static class MarkupContentReader
{
	/// <summary>
	/// Extracts typed markup content from a JsonElement representing an LSP markup payload.
	/// </summary>
	/// <param name="element">The protocol markup payload to interpret.</param>
	/// <returns>The extracted markup content.</returns>
	/// <remarks>
	/// Unsupported or empty payloads return a <see cref="ProtocolMarkupContent"/> whose <see cref="ProtocolMarkupContent.Text"/> is empty.
	/// A bare string payload is treated as Markdown, matching the LSP <c>MarkedString</c> definition used by
	/// <c>Hover.contents</c>; this is the deliberate policy for every string form, including the string form of
	/// <c>CompletionItem.documentation</c> and <c>SignatureInformation.documentation</c>, which LSP defines as plain
	/// text. Servers that use the string form for raw code or plain prose are therefore flagged as Markdown, and the
	/// host decides how to render it.
	/// </remarks>
	public static ProtocolMarkupContent ExtractContent(JsonElement element) => element.ValueKind switch
	{
		JsonValueKind.String => new ProtocolMarkupContent(element.GetString(), TextMarkupKind.Markdown),
		JsonValueKind.Array => CombineArrayMarkupContent(element),

		JsonValueKind.Object when TryGetStringProperty(element, "value", out string? value)
			&& TryGetStringProperty(element, "kind", out string? kind)
				=> new ProtocolMarkupContent(value,
					string.Equals(kind, "markdown", StringComparison.OrdinalIgnoreCase)
						? TextMarkupKind.Markdown
						: TextMarkupKind.PlainText),

		JsonValueKind.Object when TryGetStringProperty(element, "language", out string? language)
			&& TryGetStringProperty(element, "value", out string? codeValue)
				=> new ProtocolMarkupContent(BuildFencedCodeBlock(language, codeValue), TextMarkupKind.Markdown),

		JsonValueKind.Object when TryGetStringProperty(element, "value", out string? plainValue)
			=> new ProtocolMarkupContent(plainValue, TextMarkupKind.PlainText),

		_ => default
	};

	/// <summary>
	/// Normalizes Markdown text by standardizing line endings while preserving surrounding whitespace.
	/// </summary>
	/// <param name="text">The Markdown text to normalize.</param>
	/// <returns>The normalized Markdown text, or <see langword="null"/> when the input is blank.</returns>
	/// <remarks>
	/// The line-ending rule is the Core <see cref="LineTerminatorNormalizer.NormalizeToLineFeeds(string)"/>
	/// rule; this method adds the protocol-level "blank means absent" policy on top of it.
	/// </remarks>
	public static string? NormalizeMarkdownText(string? text)
		=> string.IsNullOrWhiteSpace(text)
			? null
			: LineTerminatorNormalizer.NormalizeToLineFeeds(text);

	/// <summary>
	/// Extracts and joins markup fragments from an LSP markup-content array.
	/// </summary>
	/// <param name="arrayElement">The array payload to combine.</param>
	/// <returns>The combined markup content.</returns>
	private static ProtocolMarkupContent CombineArrayMarkupContent(JsonElement arrayElement)
	{
		var kind = TextMarkupKind.PlainText;
		List<string>? parts = null;

		foreach (JsonElement child in arrayElement.EnumerateArray())
		{
			ProtocolMarkupContent item = ExtractContent(child);

			if (string.IsNullOrWhiteSpace(item.Text))
				continue;

			parts ??= [];
			parts.Add(TrimLineBoundaryPadding(item.Text));

			// One Markdown fragment makes the combined payload Markdown, so the per-fragment kind
			// collapses to the strongest kind the array carries.
			if (item.Kind == TextMarkupKind.Markdown)
				kind = TextMarkupKind.Markdown;
		}

		return parts is null
			? default
			: new ProtocolMarkupContent(string.Join("\n\n", parts), kind);
	}

	/// <summary>
	/// Builds a fenced Markdown code block that remains valid even when the payload already contains backticks.
	/// </summary>
	/// <param name="language">The optional code language identifier.</param>
	/// <param name="code">The code payload.</param>
	/// <returns>The fenced Markdown code block.</returns>
	private static string BuildFencedCodeBlock(string? language, string? code)
	{
		string codeText = code ?? string.Empty;
		int longestFenceRun = GetLongestBacktickRun(codeText);
		string fence = new('`', Math.Max(3, longestFenceRun + 1));

		return string.IsNullOrWhiteSpace(language) || !IsSafeCodeFenceLanguage(language)
			? $"{fence}\n{codeText}\n{fence}"
			: $"{fence}{language}\n{codeText}\n{fence}";
	}

	/// <summary>
	/// Reports whether a language identifier can be embedded in a fence info line without breaking out of it.
	/// </summary>
	/// <param name="language">The language identifier to inspect.</param>
	/// <returns><see langword="true"/> when the identifier contains no backticks, whitespace, or control characters.</returns>
	private static bool IsSafeCodeFenceLanguage(string language)
	{
		for (int i = 0; i < language.Length; i++)
		{
			char current = language[i];

			if (current == '`' || char.IsWhiteSpace(current) || char.IsControl(current))
				return false;
		}

		return true;
	}

	/// <summary>
	/// Trims only line-boundary padding while preserving meaningful leading and trailing spaces inside Markdown lines.
	/// </summary>
	/// <param name="text">The text fragment to trim.</param>
	/// <returns>The fragment without surrounding CR/LF padding.</returns>
	private static string TrimLineBoundaryPadding(string text)
		=> text.Trim('\r', '\n');

	/// <summary>
	/// Finds the longest contiguous run of backticks in the supplied text.
	/// </summary>
	/// <param name="text">The text to inspect.</param>
	/// <returns>The length of the longest backtick run.</returns>
	private static int GetLongestBacktickRun(string text)
	{
		int longestRun = 0;
		int currentRun = 0;

		for (int i = 0; i < text.Length; i++)
		{
			if (text[i] == '`')
			{
				currentRun++;
				longestRun = Math.Max(longestRun, currentRun);
			}
			else
			{
				currentRun = 0;
			}
		}

		return longestRun;
	}

	/// <summary>
	/// Reads one string property while tolerating <see langword="null"/> values and rejecting non-string payloads.
	/// </summary>
	/// <param name="element">The JSON object to inspect.</param>
	/// <param name="propertyName">The property name to read.</param>
	/// <param name="value">Receives the string value when present.</param>
	/// <returns><see langword="true"/> when the property is present and either a string or <see langword="null"/>.</returns>
	private static bool TryGetStringProperty(JsonElement element, string propertyName, out string? value)
	{
		value = null;

		if (!JsonElementReadHelpers.TryGetProperty(element, propertyName, out JsonElement propertyElement))
			return false;

		if (propertyElement.ValueKind is JsonValueKind.Null)
			return true;

		if (propertyElement.ValueKind is not JsonValueKind.String)
			return false;

		value = propertyElement.GetString();
		return true;
	}
}

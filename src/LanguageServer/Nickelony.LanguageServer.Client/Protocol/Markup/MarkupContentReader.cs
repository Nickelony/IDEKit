using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Provides the tolerant reading rules for the markup shapes that LSP hover and documentation members use.</summary>
internal static class MarkupContentReader
{
	/// <summary>Reads one LSP <c>MarkedString</c> or <c>MarkupContent</c> value.</summary>
	/// <param name="element">The JSON value to read.</param>
	/// <returns>
	/// The markup, or <see langword="null"/> when the value is neither a string nor a usable markup object. A bare string is Markdown,
	/// which is the protocol's equivalence for <c>MarkedString</c>.
	/// </returns>
	internal static MarkupContentPayload? ReadMarkedString(JsonElement element)
	{
		return element.ValueKind switch
		{
			JsonValueKind.String => new MarkupContentPayload { Kind = MarkupKind.Markdown, Value = element.GetString() ?? string.Empty },
			JsonValueKind.Object => ReadObject(element),
			_ => null,
		};
	}

	/// <summary>Reads a markup object in either of its two protocol shapes.</summary>
	/// <param name="element">The JSON value to read.</param>
	/// <returns>The markup, or <see langword="null"/> when the value is not an object or carries no <c>value</c> string.</returns>
	/// <remarks>
	/// An object with a <c>kind</c> is <c>MarkupContent</c>: any kind other than <c>markdown</c>
	/// reads as plain text. Otherwise, an object with a <c>language</c> is a <c>MarkedString</c> code block, which becomes a fenced Markdown block.
	/// An object with only a <c>value</c> reads as plain text. A <see langword="null"/> <c>value</c> reads as empty text, and a member
	/// of any other JSON kind counts as absent.
	/// </remarks>
	internal static MarkupContentPayload? ReadObject(JsonElement element)
	{
		if (!TryGetStringProperty(element, "value", out string? value))
			return null;

		if (TryGetStringProperty(element, "kind", out string? kind))
			return new MarkupContentPayload { Kind = MarkupKindJsonConverter.FromWireName(kind), Value = value ?? string.Empty };

		if (TryGetStringProperty(element, "language", out string? language))
			return new MarkupContentPayload { Kind = MarkupKind.Markdown, Value = BuildFencedCodeBlock(language, value) };

		return new MarkupContentPayload { Kind = MarkupKind.PlainText, Value = value ?? string.Empty };
	}

	/// <summary>Builds a fenced Markdown code block that remains valid even when the payload already contains backticks.</summary>
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

	/// <summary>Reports whether a language identifier can be embedded in a fence info line without breaking out of it.</summary>
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

	/// <summary>Finds the longest contiguous run of backticks in the supplied text.</summary>
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

	/// <summary>Reads one string property while tolerating <see langword="null"/> values and rejecting non-string payloads.</summary>
	/// <param name="element">The JSON object to inspect.</param>
	/// <param name="propertyName">The property name to read.</param>
	/// <param name="value">Receives the string value when present.</param>
	/// <returns><see langword="true"/> when the property is present and either a string or <see langword="null"/>.</returns>
	private static bool TryGetStringProperty(JsonElement element, string propertyName, out string? value)
	{
		value = null;

		if (!JsonElementReader.TryGetProperty(element, propertyName, out JsonElement propertyElement))
			return false;

		if (propertyElement.ValueKind is JsonValueKind.Null)
			return true;

		if (propertyElement.ValueKind is not JsonValueKind.String)
			return false;

		value = propertyElement.GetString();
		return true;
	}
}

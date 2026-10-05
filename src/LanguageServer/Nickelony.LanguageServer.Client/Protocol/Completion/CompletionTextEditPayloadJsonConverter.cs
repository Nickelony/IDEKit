using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>
/// Reads and writes <see cref="CompletionTextEditPayload"/> values in either legal protocol shape and rejects any payload that mixes or misses them.
/// </summary>
/// <remarks>
/// <para>
/// A legal payload carries either <c>range</c> (classic shape) or <c>insert</c> together with <c>replace</c> (insert/replace shape).
/// A payload that carries neither, carries <c>range</c> together with <c>insert</c> or <c>replace</c>, or carries only one of the insert/replace
/// ranges is malformed and fails deserialization with <see cref="JsonException"/>. The completion reader owns the containing list and drops
/// such an item with a warning instead of failing the response.
/// </para>
/// <para>
/// Unknown properties are ignored for forward compatibility, and a JSON <see langword="null"/> binds to a <see langword="null"/> nullable edit
/// without invoking this converter.
/// </para>
/// </remarks>
internal sealed class CompletionTextEditPayloadJsonConverter : JsonConverter<CompletionTextEditPayload>
{
	/// <inheritdoc/>
	/// <exception cref="JsonException">The payload is not a legal completion text-edit shape.</exception>
	public override CompletionTextEditPayload Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		using var document = JsonDocument.ParseValue(ref reader);
		JsonElement root = document.RootElement;

		if (root.ValueKind != JsonValueKind.Object)
			throw new JsonException("A completion text edit must be a JSON object.");

		string? newText = null;

		if (JsonElementReader.TryGetProperty(root, "newText", out JsonElement newTextElement) && newTextElement.ValueKind != JsonValueKind.Null)
		{
			if (newTextElement.ValueKind != JsonValueKind.String)
				throw new JsonException("A completion text edit 'newText' must be a JSON string.");

			newText = newTextElement.GetString();
		}

		bool hasRange = JsonElementReader.TryGetProperty(root, "range", out JsonElement rangeElement);
		bool hasInsert = JsonElementReader.TryGetProperty(root, "insert", out JsonElement insertElement);
		bool hasReplace = JsonElementReader.TryGetProperty(root, "replace", out JsonElement replaceElement);

		if (hasRange)
		{
			if (hasInsert || hasReplace)
				throw new JsonException("A completion text edit must not combine 'range' with 'insert' or 'replace'.");

			return new CompletionRangeTextEditPayload { NewText = newText, Range = rangeElement.Deserialize<RangePayload>(options) };
		}

		if (hasInsert && hasReplace)
		{
			return new CompletionInsertReplaceTextEditPayload
			{
				NewText = newText,
				Insert = insertElement.Deserialize<RangePayload>(options),
				Replace = replaceElement.Deserialize<RangePayload>(options),
			};
		}

		throw new JsonException("A completion text edit must carry either 'range' or both 'insert' and 'replace'.");
	}

	/// <inheritdoc/>
	/// <exception cref="JsonException">The payload is not a known completion text-edit shape.</exception>
	public override void Write(Utf8JsonWriter writer, CompletionTextEditPayload value, JsonSerializerOptions options)
	{
		switch (value)
		{
			case CompletionRangeTextEditPayload rangeEdit:
				writer.WriteStartObject();
				WriteNewText(writer, rangeEdit.NewText);
				writer.WritePropertyName("range");
				JsonSerializer.Serialize(writer, rangeEdit.Range, options);
				writer.WriteEndObject();
				break;

			case CompletionInsertReplaceTextEditPayload insertReplaceEdit:
				writer.WriteStartObject();
				WriteNewText(writer, insertReplaceEdit.NewText);
				writer.WritePropertyName("insert");
				JsonSerializer.Serialize(writer, insertReplaceEdit.Insert, options);
				writer.WritePropertyName("replace");
				JsonSerializer.Serialize(writer, insertReplaceEdit.Replace, options);
				writer.WriteEndObject();
				break;

			default:
				throw new JsonException($"The completion text-edit payload type '{value.GetType()}' cannot be serialized.");
		}
	}

	/// <summary>Writes the optional replacement text, omitting it when it is <see langword="null"/>.</summary>
	/// <param name="writer">The writer that receives the property.</param>
	/// <param name="newText">The replacement text, or <see langword="null"/> when absent.</param>
	private static void WriteNewText(Utf8JsonWriter writer, string? newText)
	{
		if (newText is not null)
			writer.WriteString("newText", newText);
	}
}

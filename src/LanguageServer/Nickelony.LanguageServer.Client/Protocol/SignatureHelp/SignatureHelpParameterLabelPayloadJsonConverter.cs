using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Reads and writes <see cref="SignatureHelpParameterLabelPayload"/> in either protocol form.</summary>
/// <remarks>
/// Reading never fails: a label that is neither a string nor an array of exactly two integers reads as the empty label, because the offset form
/// is defined as exactly two numbers and a payload of any other shape is not guessed at. The empty label is written as a JSON <see langword="null"/>;
/// a parameter payload leaves it out instead.
/// </remarks>
internal sealed class SignatureHelpParameterLabelPayloadJsonConverter : JsonConverter<SignatureHelpParameterLabelPayload>
{
	/// <inheritdoc/>
	public override SignatureHelpParameterLabelPayload Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.String)
			return SignatureHelpParameterLabelPayload.FromText(reader.GetString() ?? string.Empty);

		if (reader.TokenType != JsonTokenType.StartArray)
		{
			reader.Skip();
			return default;
		}

		using var document = JsonDocument.ParseValue(ref reader);
		JsonElement.ArrayEnumerator parts = document.RootElement.EnumerateArray();

		if (!TryReadOffset(ref parts, out int startOffset) || !TryReadOffset(ref parts, out int endOffset) || parts.MoveNext())
			return default;

		return SignatureHelpParameterLabelPayload.FromOffsets(startOffset, endOffset);
	}

	/// <inheritdoc/>
	public override void Write(Utf8JsonWriter writer, SignatureHelpParameterLabelPayload value, JsonSerializerOptions options)
	{
		if (value.Text is { } text)
		{
			writer.WriteStringValue(text);
		}
		else if (value is { StartOffset: { } startOffset, EndOffset: { } endOffset })
		{
			writer.WriteStartArray();

			writer.WriteNumberValue(startOffset);
			writer.WriteNumberValue(endOffset);

			writer.WriteEndArray();
		}
		else
		{
			writer.WriteNullValue();
		}
	}

	private static bool TryReadOffset(ref JsonElement.ArrayEnumerator parts, out int offset)
	{
		offset = 0;
		return parts.MoveNext() && parts.Current.ValueKind == JsonValueKind.Number && parts.Current.TryGetInt32(out offset);
	}
}

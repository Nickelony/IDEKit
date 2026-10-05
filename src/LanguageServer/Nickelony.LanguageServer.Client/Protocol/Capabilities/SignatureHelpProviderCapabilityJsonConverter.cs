using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Converts the <c>signatureHelpProvider</c> capability, which may be advertised as either a boolean or an options object.</summary>
/// <remarks>
/// Reading accepts a boolean or an options object; writing always emits the boolean form. A character list that is not an array of strings
/// counts as empty, and an entry that is not a string is skipped.
/// </remarks>
internal sealed class SignatureHelpProviderCapabilityJsonConverter : JsonConverter<SignatureHelpProviderCapability>
{
	/// <inheritdoc/>
	public override SignatureHelpProviderCapability Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		return reader.TokenType switch
		{
			JsonTokenType.True => new SignatureHelpProviderCapability(true, [], []),
			JsonTokenType.StartObject => ReadObject(ref reader),
			JsonTokenType.False or JsonTokenType.Null => new SignatureHelpProviderCapability(false, [], []),
			_ => ReadUnsupported(ref reader),
		};
	}

	/// <inheritdoc/>
	public override void Write(Utf8JsonWriter writer, SignatureHelpProviderCapability value, JsonSerializerOptions options) =>
		writer.WriteBooleanValue(value.IsSupported);

	private static SignatureHelpProviderCapability ReadObject(ref Utf8JsonReader reader)
	{
		using var document = JsonDocument.ParseValue(ref reader);

		return new SignatureHelpProviderCapability(
			true,
			ReadCharacters(document.RootElement, "triggerCharacters"),
			ReadCharacters(document.RootElement, "retriggerCharacters")
		);
	}

	private static IReadOnlyList<string> ReadCharacters(JsonElement options, string propertyName)
	{
		if (!JsonElementReader.TryGetProperty(options, propertyName, out JsonElement list) || list.ValueKind != JsonValueKind.Array)
			return [];

		return [.. list.EnumerateArray().Where(entry => entry.ValueKind == JsonValueKind.String).Select(entry => entry.GetString()).OfType<string>()];
	}

	private static SignatureHelpProviderCapability ReadUnsupported(ref Utf8JsonReader reader)
	{
		using var ignored = JsonDocument.ParseValue(ref reader);
		return new SignatureHelpProviderCapability(false, [], []);
	}
}

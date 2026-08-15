using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Converts LSP capability fields that may be advertised as either booleans or objects.
/// </summary>
/// <remarks>
/// Serialization normalizes the capability to its boolean form; the distinction between the boolean and object
/// advertisement is not retained because both mean the same thing to this client. Reading degrades silently: a
/// <see langword="null"/> advertisement maps to the default (not supported) and any other JSON kind maps to not
/// supported without a warning, because the converter is attribute-registered and therefore has no logger, and a
/// malformed optional capability means the same thing to this client as an absent one. A capability the client
/// cannot run without is surfaced by the capability store's startup validation instead.
/// </remarks>
internal sealed class SupportedCapabilityJsonConverter : JsonConverter<SupportedCapability>
{
	/// <inheritdoc/>
	public override SupportedCapability Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		return reader.TokenType switch
		{
			JsonTokenType.True => new SupportedCapability(true),
			JsonTokenType.False => new SupportedCapability(false),
			JsonTokenType.StartObject => ReadObject(ref reader),
			JsonTokenType.Null => default,
			_ => ReadUnsupported(ref reader)
		};
	}

	/// <inheritdoc/>
	public override void Write(Utf8JsonWriter writer, SupportedCapability value, JsonSerializerOptions options)
		=> writer.WriteBooleanValue(value.IsSupported);

	private static SupportedCapability ReadObject(ref Utf8JsonReader reader)
	{
		using JsonDocument ignored = JsonDocument.ParseValue(ref reader);
		return new(true);
	}

	private static SupportedCapability ReadUnsupported(ref Utf8JsonReader reader)
	{
		using JsonDocument ignored = JsonDocument.ParseValue(ref reader);
		return new(false);
	}
}

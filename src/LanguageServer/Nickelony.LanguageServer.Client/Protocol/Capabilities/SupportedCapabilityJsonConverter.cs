using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Converts LSP capability fields that may be advertised as either booleans or objects.</summary>
/// <remarks>
/// Reading accepts a boolean or an object; any other JSON kind counts as not supported. Writing always emits the boolean form.
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
			_ => ReadUnsupported(ref reader),
		};
	}

	/// <inheritdoc/>
	public override void Write(Utf8JsonWriter writer, SupportedCapability value, JsonSerializerOptions options) =>
		writer.WriteBooleanValue(value.IsSupported);

	private static SupportedCapability ReadObject(ref Utf8JsonReader reader)
	{
		using var ignored = JsonDocument.ParseValue(ref reader);
		return new(true);
	}

	private static SupportedCapability ReadUnsupported(ref Utf8JsonReader reader)
	{
		using var ignored = JsonDocument.ParseValue(ref reader);
		return new(false);
	}
}

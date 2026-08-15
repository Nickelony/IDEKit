using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Converts the <c>codeActionProvider</c> capability, which may be advertised as either a boolean or an options
/// object.
/// </summary>
/// <remarks>
/// Serialization normalizes the capability to its boolean form; the resolve-provider flag is retained only when
/// reading, because the boolean and object advertisements mean the same thing to this client. Reading degrades
/// silently: a <see langword="null"/> advertisement maps to the default (not supported), any other JSON kind maps
/// to not supported, and a non-boolean <c>resolveProvider</c> counts as not advertised - none of which is reported,
/// the converter is attribute-registered and therefore has no logger. A malformed optional capability means the same
/// thing to this client as an absent one.
/// </remarks>
internal sealed class CodeActionProviderCapabilityJsonConverter : JsonConverter<CodeActionProviderCapability>
{
	/// <inheritdoc/>
	public override CodeActionProviderCapability Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		return reader.TokenType switch
		{
			JsonTokenType.True => new CodeActionProviderCapability(true, null),
			JsonTokenType.False => new CodeActionProviderCapability(false, null),
			JsonTokenType.StartObject => ReadObject(ref reader),
			JsonTokenType.Null => default,
			_ => ReadUnsupported(ref reader)
		};
	}

	/// <inheritdoc/>
	public override void Write(Utf8JsonWriter writer, CodeActionProviderCapability value, JsonSerializerOptions options)
		=> writer.WriteBooleanValue(value.IsSupported);

	private static CodeActionProviderCapability ReadObject(ref Utf8JsonReader reader)
	{
		using JsonDocument document = JsonDocument.ParseValue(ref reader);

		bool? resolveProvider = JsonElementReadHelpers.TryGetProperty(document.RootElement, "resolveProvider", out JsonElement resolveProviderElement)
			? resolveProviderElement.ValueKind switch
			{
				JsonValueKind.True => true,
				JsonValueKind.False => false,
				_ => null
			}
			: null;

		return new CodeActionProviderCapability(true, resolveProvider);
	}

	private static CodeActionProviderCapability ReadUnsupported(ref Utf8JsonReader reader)
	{
		using JsonDocument ignored = JsonDocument.ParseValue(ref reader);
		return new CodeActionProviderCapability(false, null);
	}
}

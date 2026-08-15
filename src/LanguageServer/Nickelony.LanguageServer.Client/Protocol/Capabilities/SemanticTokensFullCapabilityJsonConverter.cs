using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Converts the semantic tokens full capability, whose wire shape is a boolean or an object carrying an optional
/// <c>delta</c> flag.
/// </summary>
/// <remarks>
/// Reading degrades silently: a <see langword="null"/> advertisement maps to the default (not supported), and any
/// other JSON kind maps to not supported. This client never requests delta refreshes, so the object form only
/// signals that full requests are supported. None of this is reported, because the converter is
/// attribute-registered and therefore has no logger; a malformed optional capability means the same thing to this
/// client as an absent one.
/// </remarks>
internal sealed class SemanticTokensFullCapabilityJsonConverter : JsonConverter<SemanticTokensFullCapability>
{
	/// <inheritdoc/>
	public override SemanticTokensFullCapability Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		return reader.TokenType switch
		{
			JsonTokenType.True => new SemanticTokensFullCapability(true),
			JsonTokenType.False => new SemanticTokensFullCapability(false),
			JsonTokenType.StartObject => ReadObjectForm(ref reader),
			JsonTokenType.Null => default,
			_ => ReadUnsupported(ref reader)
		};
	}

	/// <inheritdoc/>
	public override void Write(Utf8JsonWriter writer, SemanticTokensFullCapability value, JsonSerializerOptions options)
		=> writer.WriteBooleanValue(value.IsSupported);

	private static SemanticTokensFullCapability ReadObjectForm(ref Utf8JsonReader reader)
	{
		// The object form carries only an optional `delta` flag, which this client ignores; its presence means
		// full requests are supported.
		using JsonDocument ignored = JsonDocument.ParseValue(ref reader);
		return new SemanticTokensFullCapability(true);
	}

	private static SemanticTokensFullCapability ReadUnsupported(ref Utf8JsonReader reader)
	{
		using JsonDocument ignored = JsonDocument.ParseValue(ref reader);
		return new SemanticTokensFullCapability(false);
	}
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>
/// Converts the semantic tokens full capability, whose wire shape is a boolean or an object carrying an optional <c>delta</c> flag.
/// </summary>
/// <remarks>
/// The object form only signals that full requests are supported; delta refreshes are never requested. Any non-boolean, non-object JSON kind counts
/// as not supported.
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
			JsonTokenType.StartObject => ReadObject(ref reader),
			JsonTokenType.Null => default,
			_ => ReadUnsupported(ref reader),
		};
	}

	/// <inheritdoc/>
	public override void Write(Utf8JsonWriter writer, SemanticTokensFullCapability value, JsonSerializerOptions options) =>
		writer.WriteBooleanValue(value.IsSupported);

	private static SemanticTokensFullCapability ReadObject(ref Utf8JsonReader reader)
	{
		// The object form carries only an optional `delta` flag, which this client ignores; its presence means
		// full requests are supported.
		using var ignored = JsonDocument.ParseValue(ref reader);
		return new SemanticTokensFullCapability(true);
	}

	private static SemanticTokensFullCapability ReadUnsupported(ref Utf8JsonReader reader)
	{
		using var ignored = JsonDocument.ParseValue(ref reader);
		return new SemanticTokensFullCapability(false);
	}
}

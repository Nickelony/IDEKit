using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Reads LSP <c>string | MarkupContent</c> into a <see cref="MarkupContentPayload"/> and writes it as <c>MarkupContent</c>.</summary>
/// <remarks>
/// A bare string reads as plain text. An object reads through the same rules as a hover fragment (<c>kind</c> with <c>value</c>,
/// a <c>language</c> code block, or a <c>value</c> alone), so a server that sends a code-block object where documentation is expected keeps its text.
/// A JSON value of any other kind reads as <see langword="null"/> with a warning, because documentation is optional and one malformed member
/// must not fail the response that carries it.
/// </remarks>
internal sealed class MarkupContentPayloadJsonConverter : TolerantPayloadJsonConverter<MarkupContentPayload>
{
	public MarkupContentPayloadJsonConverter() { }

	/// <param name="logger">The logger used for malformed-payload diagnostics.</param>
	public MarkupContentPayloadJsonConverter(ILogger? logger)
		: base(logger) { }

	/// <inheritdoc/>
	public override MarkupContentPayload? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.String)
			return new MarkupContentPayload { Kind = MarkupKind.PlainText, Value = reader.GetString() ?? string.Empty };

		using var document = JsonDocument.ParseValue(ref reader);
		JsonElement root = document.RootElement;

		if (MarkupContentReader.ReadObject(root) is { } content)
			return content;

		Logger.LogWarning(
			"Ignoring malformed markup content because its JSON value of kind {Kind} is neither a string nor a markup object.",
			root.ValueKind
		);

		return null;
	}

	/// <inheritdoc/>
	public override void Write(Utf8JsonWriter writer, MarkupContentPayload value, JsonSerializerOptions options)
	{
		writer.WriteStartObject();

		writer.WriteString("kind", MarkupKindJsonConverter.ToWireName(value.Kind));
		writer.WriteString("value", value.Value);

		writer.WriteEndObject();
	}
}

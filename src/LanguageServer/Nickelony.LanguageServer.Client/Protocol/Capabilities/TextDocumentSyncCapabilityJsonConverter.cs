using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Converts the LSP text-document sync capability from either numeric or object form.</summary>
/// <remarks>
/// Writing emits the negotiated kind as its numeric form; the object form's additional
/// members (<c>openClose</c>, <c>save</c>, <c>willSave</c>, <c>willSaveWaitUntil</c>) are not retained by this client. An unrecognized numeric kind
/// (or a missing <c>change</c> member) maps to <see cref="TextDocumentSyncKind.None"/>.
/// </remarks>
internal sealed class TextDocumentSyncCapabilityJsonConverter : JsonConverter<TextDocumentSyncCapability>
{
	private readonly record struct TextDocumentSyncCapabilityObject([property: JsonPropertyName("change")] JsonElement Change);

	/// <inheritdoc/>
	public override TextDocumentSyncCapability Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		switch (reader.TokenType)
		{
			case JsonTokenType.Number:
				return reader.TryGetInt32(out int rawSyncKind)
					? new TextDocumentSyncCapability(ParseTextDocumentSyncKind(rawSyncKind))
					: new TextDocumentSyncCapability(TextDocumentSyncKind.None);

			case JsonTokenType.StartObject:
				return ReadObject(ref reader, options);

			case JsonTokenType.Null:
				return default;

			default:
				using (var ignored = JsonDocument.ParseValue(ref reader))
				{ }

				return new(TextDocumentSyncKind.None);
		}
	}

	/// <inheritdoc/>
	public override void Write(Utf8JsonWriter writer, TextDocumentSyncCapability value, JsonSerializerOptions options) =>
		writer.WriteNumberValue((int)value.Kind);

	private static TextDocumentSyncCapability ReadObject(ref Utf8JsonReader reader, JsonSerializerOptions options)
	{
		TextDocumentSyncCapabilityObject payload = JsonSerializer.Deserialize<TextDocumentSyncCapabilityObject>(ref reader, options);

		if (payload.Change.ValueKind != JsonValueKind.Number || !payload.Change.TryGetInt32(out int rawSyncKind))
			return new(TextDocumentSyncKind.None);

		return new(ParseTextDocumentSyncKind(rawSyncKind));
	}

	private static TextDocumentSyncKind ParseTextDocumentSyncKind(int rawSyncKind)
	{
		return rawSyncKind switch
		{
			0 => TextDocumentSyncKind.None,
			1 => TextDocumentSyncKind.Full,
			2 => TextDocumentSyncKind.Incremental,
			_ => TextDocumentSyncKind.None,
		};
	}
}

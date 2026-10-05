using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>
/// Reads and writes raw JSON members, writing a JSON <see langword="null"/> for an <see cref="JsonValueKind.Undefined"/> element so a host-assigned
/// <c>default(JsonElement)</c> cannot make serialization throw.
/// </summary>
internal sealed class JsonElementJsonConverter : JsonConverter<JsonElement>
{
	/// <inheritdoc/>
	public override JsonElement Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
		JsonElement.ParseValue(ref reader);

	/// <inheritdoc/>
	public override void Write(Utf8JsonWriter writer, JsonElement value, JsonSerializerOptions options)
	{
		if (value.ValueKind == JsonValueKind.Undefined)
		{
			writer.WriteNullValue();
			return;
		}

		value.WriteTo(writer);
	}
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Reads the <c>workspace/configuration</c> request items while preserving request positions.</summary>
/// <remarks>
/// Entries are never skipped: a dropped entry would shift every later answer to the wrong section. A root that is not an array degrades to an empty
/// collection with a warning.
/// </remarks>
internal sealed class WorkspaceConfigurationItemsJsonConverter : JsonConverter<WorkspaceConfigurationItemPayload[]>
{
	private readonly ILogger _logger;

	/// <param name="logger">The logger used for malformed-payload diagnostics.</param>
	public WorkspaceConfigurationItemsJsonConverter(ILogger? logger) => _logger = logger ?? NullLogger.Instance;

	/// <inheritdoc/>
	public override WorkspaceConfigurationItemPayload[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		using var document = JsonDocument.ParseValue(ref reader);
		JsonElement root = document.RootElement;

		if (root.ValueKind != JsonValueKind.Array)
		{
			_logger.LogWarning("Ignoring a malformed workspace configuration request because its JSON kind {Kind} is not an array.", root.ValueKind);
			return [];
		}

		var items = new WorkspaceConfigurationItemPayload[root.GetArrayLength()];
		int index = 0;

		foreach (JsonElement element in root.EnumerateArray())
			items[index++] = ReadItem(element);

		return items;
	}

	/// <inheritdoc/>
	public override void Write(Utf8JsonWriter writer, WorkspaceConfigurationItemPayload[] value, JsonSerializerOptions options)
	{
		writer.WriteStartArray();

		foreach (WorkspaceConfigurationItemPayload item in value)
			JsonSerializer.Serialize(writer, item, options);

		writer.WriteEndArray();
	}

	/// <summary>
	/// Reads one request entry, marking <see langword="null"/> or malformed entries as placeholders whose answer is <see langword="null"/>.
	/// </summary>
	/// <param name="element">The entry to read.</param>
	private WorkspaceConfigurationItemPayload ReadItem(JsonElement element)
	{
		if (element.ValueKind == JsonValueKind.Object)
		{
			// An object with no section member, or with a null section, requests the whole settings root, which is legal. The lookup
			// ignores case, as the reflection-bound members of the serializer options do, so a differently cased section member is still found.
			if (!JsonElementReader.TryGetProperty(element, "section", out JsonElement sectionElement))
				return new WorkspaceConfigurationItemPayload(Section: null);

			if (sectionElement.ValueKind is JsonValueKind.String or JsonValueKind.Null)
				return new WorkspaceConfigurationItemPayload(sectionElement.GetString());
		}

		_logger.LogWarning("A workspace configuration request entry is null or malformed; answering null for that entry.");

		return new WorkspaceConfigurationItemPayload(Section: null, IsMalformed: true);
	}
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Reads the <c>workspace/configuration</c> request items while preserving request positions.
/// </summary>
/// <remarks>
/// <para>
/// A null or malformed entry becomes a <see cref="WorkspaceConfigurationItem.IsMalformed"/> placeholder instead of
/// being skipped, so the response array stays aligned one-for-one with the request as LSP requires.
/// </para>
/// <para>
/// The <see cref="TolerantCollectionJsonConverter{TElement}"/> skip policy is safe for result lists, where a
/// dropped entry only loses its own data, but for the positional configuration request/response pair it would
/// shift every later answer to the wrong section. A root that is not an array degrades to an empty collection
/// with a warning, matching the tolerant reader policy.
/// </para>
/// </remarks>
internal sealed class WorkspaceConfigurationItemsJsonConverter : JsonConverter<WorkspaceConfigurationItem[]>
{
	private readonly ILogger _logger;

	/// <summary>
	/// Initializes a new instance of the <see cref="WorkspaceConfigurationItemsJsonConverter"/> class.
	/// </summary>
	/// <param name="logger">The logger used for malformed-payload diagnostics.</param>
	public WorkspaceConfigurationItemsJsonConverter(ILogger? logger)
		=> _logger = logger ?? NullLogger.Instance;

	/// <inheritdoc/>
	public override WorkspaceConfigurationItem[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		using JsonDocument document = JsonDocument.ParseValue(ref reader);
		JsonElement root = document.RootElement;

		if (root.ValueKind != JsonValueKind.Array)
		{
			_logger.LogWarning(
				"Ignoring a malformed workspace configuration request because its JSON kind {Kind} is not an array.",
				root.ValueKind);

			return [];
		}

		var items = new WorkspaceConfigurationItem[root.GetArrayLength()];
		int index = 0;

		foreach (JsonElement element in root.EnumerateArray())
			items[index++] = ReadItem(element);

		return items;
	}

	/// <inheritdoc/>
	public override void Write(Utf8JsonWriter writer, WorkspaceConfigurationItem[] value, JsonSerializerOptions options)
	{
		writer.WriteStartArray();

		foreach (WorkspaceConfigurationItem item in value)
			JsonSerializer.Serialize(writer, item, options);

		writer.WriteEndArray();
	}

	/// <summary>
	/// Reads one request entry, marking null or malformed entries as placeholders whose answer is null.
	/// </summary>
	/// <param name="element">The entry to read.</param>
	/// <returns>The item for the entry.</returns>
	private WorkspaceConfigurationItem ReadItem(JsonElement element)
	{
		if (element.ValueKind == JsonValueKind.Object)
		{
			// An object without a section string requests the whole settings root, which is legal. The lookup is
			// case-insensitive like every sibling read, so a hand-written member lookup cannot lose a section the
			// reflection-bound members would have bound.
			if (!JsonElementReadHelpers.TryGetProperty(element, "section", out JsonElement sectionElement))
				return new WorkspaceConfigurationItem(Section: null);

			if (sectionElement.ValueKind is JsonValueKind.String or JsonValueKind.Null)
				return new WorkspaceConfigurationItem(sectionElement.GetString());
		}

		_logger.LogWarning("A workspace configuration request entry is null or malformed; answering null for that entry.");

		return new WorkspaceConfigurationItem(Section: null, IsMalformed: true);
	}
}

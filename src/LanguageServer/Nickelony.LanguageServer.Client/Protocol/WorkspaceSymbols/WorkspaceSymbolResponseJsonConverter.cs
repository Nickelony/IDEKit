using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Deserializes workspace-symbol responses tolerantly so one malformed element cannot fail the whole response.</summary>
/// <remarks>
/// <para>
/// A JSON <see langword="null"/> response deserializes to a <see langword="null"/> response because the converter is not invoked
/// for JSON <see langword="null"/>. A payload that is not an array produces an empty response with a warning.
/// </para>
/// <para>
/// Both result shapes read as the same entry. An entry without a name, a numeric kind and a location with a URI is skipped with a warning,
/// except that a JSON <see langword="null"/> is skipped silently.
/// </para>
/// </remarks>
internal sealed class WorkspaceSymbolResponseJsonConverter : TolerantPayloadJsonConverter<WorkspaceSymbolResponse>
{
	public WorkspaceSymbolResponseJsonConverter() { }

	/// <param name="logger">The logger used for malformed-payload diagnostics.</param>
	public WorkspaceSymbolResponseJsonConverter(ILogger? logger)
		: base(logger) { }

	/// <inheritdoc/>
	public override WorkspaceSymbolResponse Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		using var document = JsonDocument.ParseValue(ref reader);
		JsonElement root = document.RootElement;

		if (root.ValueKind != JsonValueKind.Array)
		{
			Logger.LogWarning("Ignoring malformed workspace-symbol payload because its JSON kind {Kind} is not an array.", root.ValueKind);
			return new WorkspaceSymbolResponse();
		}

		var symbols = new List<WorkspaceSymbolPayload>();

		foreach (JsonElement symbolElement in root.EnumerateArray())
		{
			if (
				symbolElement.ValueKind == JsonValueKind.Null
				|| !TryReadElement(element: symbolElement, options, elementDescription: "workspace symbol", value: out WorkspaceSymbolPayload? symbol)
			)
			{
				continue;
			}

			if (!IsUsable(symbol))
			{
				Logger.LogWarning("Ignoring a workspace symbol that did not contain a usable name, kind and location.");
				continue;
			}

			symbols.Add(symbol);
		}

		return new WorkspaceSymbolResponse(symbols);
	}

	/// <inheritdoc/>
	public override void Write(Utf8JsonWriter writer, WorkspaceSymbolResponse value, JsonSerializerOptions options)
	{
		writer.WriteStartArray();

		for (int i = 0; i < value.Symbols.Count; i++)
			JsonSerializer.Serialize(writer, value.Symbols[i], options);

		writer.WriteEndArray();
	}

	private static bool IsUsable(WorkspaceSymbolPayload symbol)
	{
		return !string.IsNullOrWhiteSpace(symbol.Name)
			&& symbol.Kind > 0
			&& symbol.Location is { Uri: { } uri }
			&& Uri.TryCreate(uri, UriKind.Absolute, out _);
	}
}

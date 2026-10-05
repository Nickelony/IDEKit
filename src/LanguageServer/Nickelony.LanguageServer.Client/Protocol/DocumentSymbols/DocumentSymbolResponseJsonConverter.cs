using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Deserializes document-symbol responses tolerantly so one malformed element cannot fail the whole response.</summary>
/// <remarks>
/// <para>
/// A JSON <see langword="null"/> response deserializes to a <see langword="null"/> response because the converter is not invoked
/// for JSON <see langword="null"/>. A payload that is not an array produces an empty response with a warning.
/// </para>
/// <para>
/// Symbol entries without a usable name, a numeric kind, and at least one usable range or location are skipped with a warning;
/// non-object elements are skipped likewise, except that a JSON <see langword="null"/> is skipped silently. Range coordinates must be integers,
/// while negative or out-of-document coordinates remain representable for the consuming conversion to validate and clamp. A range whose end position
/// is missing or malformed degrades to an empty range at its start so an otherwise usable entry stays usable.
/// </para>
/// </remarks>
internal sealed class DocumentSymbolResponseJsonConverter : TolerantPayloadJsonConverter<DocumentSymbolResponse>
{
	public DocumentSymbolResponseJsonConverter() { }

	/// <param name="logger">The logger used for malformed-payload diagnostics.</param>
	public DocumentSymbolResponseJsonConverter(ILogger? logger)
		: base(logger) { }

	/// <inheritdoc/>
	public override DocumentSymbolResponse Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		using var document = JsonDocument.ParseValue(ref reader);
		JsonElement root = document.RootElement;

		if (root.ValueKind != JsonValueKind.Array)
		{
			Logger.LogWarning("Ignoring malformed document-symbol payload because its JSON kind {Kind} is not an array.", root.ValueKind);
			return new DocumentSymbolResponse();
		}

		var symbols = new List<DocumentSymbolPayload>();

		foreach (JsonElement symbolElement in root.EnumerateArray())
			AppendSymbol(symbols, symbolElement);

		return new DocumentSymbolResponse(symbols);
	}

	/// <inheritdoc/>
	public override void Write(Utf8JsonWriter writer, DocumentSymbolResponse value, JsonSerializerOptions options)
	{
		writer.WriteStartArray();

		for (int i = 0; i < value.Symbols.Count; i++)
			WriteSymbol(writer, value.Symbols[i]);

		writer.WriteEndArray();
	}

	/// <summary>Parses one symbol element and appends it when it is usable; malformed elements are skipped with a warning.</summary>
	/// <param name="symbols">The destination list that receives the parsed symbol.</param>
	/// <param name="symbolElement">The payload element to parse.</param>
	private void AppendSymbol(List<DocumentSymbolPayload> symbols, JsonElement symbolElement)
	{
		if (TryParseSymbol(symbolElement, out DocumentSymbolPayload? symbol))
		{
			symbols.Add(symbol);
			return;
		}

		if (symbolElement.ValueKind == JsonValueKind.Object)
			Logger.LogWarning("Ignoring a document symbol that did not contain a usable name, kind, and range or location.");
		else if (symbolElement.ValueKind != JsonValueKind.Null)
			Logger.LogWarning("Skipping malformed document symbol because its JSON kind {Kind} is not an object.", symbolElement.ValueKind);
	}

	/// <summary>Parses one usable document-symbol entry from a payload element.</summary>
	/// <param name="symbolElement">The payload element to parse.</param>
	/// <param name="symbol">Receives the parsed symbol when the element is usable.</param>
	/// <returns><see langword="true"/> when the element carries a usable symbol.</returns>
	private bool TryParseSymbol(JsonElement symbolElement, [NotNullWhen(true)] out DocumentSymbolPayload? symbol)
	{
		symbol = null;

		if (symbolElement.ValueKind != JsonValueKind.Object)
			return false;

		string? name = JsonElementReader.GetString(symbolElement, "name");

		if (
			string.IsNullOrWhiteSpace(name)
			|| !JsonElementReader.TryGetProperty(symbolElement, "kind", out JsonElement kindElement)
			|| kindElement.ValueKind != JsonValueKind.Number
			|| !kindElement.TryGetInt32(out int kind)
		)
		{
			return false;
		}

		// A hierarchical entry carries at least one range property; a flat entry carries a location.
		RangePayload? range = ReadRange(symbolElement, "range");
		RangePayload? selectionRange = ReadRange(symbolElement, "selectionRange");

		SymbolLocationPayload? location = TryReadLocation(symbolElement, out SymbolLocationPayload? parsedLocation) ? parsedLocation : null;

		if (range is null && selectionRange is null && location is null)
			return false;

		symbol = new DocumentSymbolPayload
		{
			Name = name,
			Detail = JsonElementReader.GetString(symbolElement, "detail"),
			Kind = (SymbolKind)kind,
			Tags = ReadSymbolTags(symbolElement),
			Deprecated = JsonElementReader.GetBoolean(symbolElement, "deprecated"),
			Range = range,
			SelectionRange = selectionRange,
			Location = location,
			ContainerName = JsonElementReader.GetString(symbolElement, "containerName"),
			Children = ReadChildren(symbolElement),
		};

		return true;
	}

	/// <summary>Parses the nested children of a hierarchical symbol element.</summary>
	/// <param name="symbolElement">The symbol element to read from.</param>
	/// <returns>
	/// The usable children in response order, or <see langword="null"/> when the property is absent or a JSON <see langword="null"/>;
	/// an attached empty array reads as an empty list.
	/// </returns>
	private IReadOnlyList<DocumentSymbolPayload>? ReadChildren(JsonElement symbolElement)
	{
		if (
			!JsonElementReader.TryGetProperty(symbolElement, "children", out JsonElement childrenElement)
			|| childrenElement.ValueKind == JsonValueKind.Null
		)
		{
			return null;
		}

		if (childrenElement.ValueKind != JsonValueKind.Array)
		{
			Logger.LogWarning(
				"Ignoring malformed document-symbol child list because its JSON kind {Kind} is not an array.",
				childrenElement.ValueKind
			);

			return null;
		}

		var children = new List<DocumentSymbolPayload>();

		// AppendSymbol warns for every skipped entry other than a JSON null, so a child that does not survive is never
		// dropped silently.
		foreach (JsonElement childElement in childrenElement.EnumerateArray())
			AppendSymbol(children, childElement);

		return [.. children];
	}

	/// <summary>Parses the optional tag array of a symbol element.</summary>
	/// <param name="symbolElement">The symbol element to read from.</param>
	/// <returns>
	/// The parsed tags, or <see langword="null"/> when the property is absent or a JSON <see langword="null"/>; an attached empty array reads
	/// as an empty list.
	/// </returns>
	private ReadOnlyCollection<SymbolTag>? ReadSymbolTags(JsonElement symbolElement)
	{
		if (!JsonElementReader.TryGetProperty(symbolElement, "tags", out JsonElement tagsElement) || tagsElement.ValueKind == JsonValueKind.Null)
			return null;

		if (tagsElement.ValueKind != JsonValueKind.Array)
		{
			Logger.LogWarning("Ignoring malformed document-symbol tag list because its JSON kind {Kind} is not an array.", tagsElement.ValueKind);
			return null;
		}

		var tags = new List<SymbolTag>();

		foreach (JsonElement tagElement in tagsElement.EnumerateArray())
		{
			if (tagElement.ValueKind == JsonValueKind.Number && tagElement.TryGetInt32(out int tag))
			{
				tags.Add((SymbolTag)tag);
				continue;
			}

			Logger.LogWarning("Skipping a malformed document-symbol tag entry with JSON kind {Kind}.", tagElement.ValueKind);
		}

		return Array.AsReadOnly([.. tags]);
	}

	/// <summary>Parses one location property of a flat symbol element.</summary>
	/// <param name="symbolElement">The symbol element to read from.</param>
	/// <param name="location">Receives the parsed location when the property is usable.</param>
	/// <returns><see langword="true"/> when the property carries a usable URI and range.</returns>
	private static bool TryReadLocation(JsonElement symbolElement, [NotNullWhen(true)] out SymbolLocationPayload? location)
	{
		location = null;

		if (
			!JsonElementReader.TryGetProperty(symbolElement, "location", out JsonElement locationElement)
			|| locationElement.ValueKind != JsonValueKind.Object
		)
		{
			return false;
		}

		string? uri = JsonElementReader.GetString(locationElement, "uri");

		// The payload contract requires an absolute URI; a blank or relative URI is treated as a malformed entry,
		// so parsing skips it instead of surfacing a location that cannot name a document.
		if (string.IsNullOrWhiteSpace(uri) || !Uri.TryCreate(uri, UriKind.Absolute, out _) || ReadRange(locationElement, "range") is not { } range)
			return false;

		location = new(uri, range);
		return true;
	}

	/// <summary>
	/// Parses one range property of a payload element, accepting negative or inverted coordinates because the consuming conversion validates
	/// and clamps them.
	/// </summary>
	/// <param name="payloadElement">The payload element to read from.</param>
	/// <param name="propertyName">The property that carries the range.</param>
	/// <returns>The parsed range, or <see langword="null"/> when the property carries none.</returns>
	private static RangePayload? ReadRange(JsonElement payloadElement, string propertyName) =>
		RangePayloadJsonConverter.ReadRangeProperty(payloadElement, propertyName, allowNegative: true, allowInverted: true);

	/// <summary>Writes one document-symbol entry with its present members in canonical protocol order.</summary>
	/// <param name="writer">The writer that receives the symbol object.</param>
	/// <param name="symbol">The symbol to write.</param>
	private static void WriteSymbol(Utf8JsonWriter writer, DocumentSymbolPayload symbol)
	{
		// Validate before any bytes are written, so a location without a URI fails the same way the definition
		// converter does instead of leaving a partially written symbol object.
		if (symbol.Location is { Uri: null })
			throw new InvalidOperationException("A symbol location without a URI cannot be serialized.");

		writer.WriteStartObject();

		if (symbol.Name is not null)
			writer.WriteString("name", symbol.Name);

		if (symbol.Detail is not null)
			writer.WriteString("detail", symbol.Detail);

		// kind is required in each response shape; always write it (even the schema-invalid
		// default value) so a tolerant read round-trips the member instead of dropping it.
		writer.WriteNumber("kind", (int)symbol.Kind);

		// An attached empty list writes as an empty array so a round trip keeps an empty list distinguishable
		// from an absent member.
		if (symbol.Tags is { } tags)
		{
			writer.WritePropertyName("tags");
			writer.WriteStartArray();

			for (int i = 0; i < tags.Count; i++)
				writer.WriteNumberValue((int)tags[i]);

			writer.WriteEndArray();
		}

		if (symbol.Deprecated is { } deprecated)
			writer.WriteBoolean("deprecated", deprecated);

		if (symbol.Range is { } range)
		{
			writer.WritePropertyName("range");
			RangePayloadJsonConverter.WriteRange(writer, range);
		}

		if (symbol.SelectionRange is { } selectionRange)
		{
			writer.WritePropertyName("selectionRange");
			RangePayloadJsonConverter.WriteRange(writer, selectionRange);
		}

		if (symbol.Location is { } location)
		{
			writer.WritePropertyName("location");
			writer.WriteStartObject();
			writer.WriteString("uri", location.Uri);
			writer.WritePropertyName("range");
			RangePayloadJsonConverter.WriteRange(writer, location.Range);
			writer.WriteEndObject();
		}

		if (symbol.ContainerName is not null)
			writer.WriteString("containerName", symbol.ContainerName);

		// An attached empty list writes as an empty array so a round trip keeps an empty list distinguishable
		// from an absent member.
		if (symbol.Children is { } children)
		{
			writer.WritePropertyName("children");
			writer.WriteStartArray();

			for (int i = 0; i < children.Count; i++)
				WriteSymbol(writer, children[i]);

			writer.WriteEndArray();
		}

		writer.WriteEndObject();
	}
}

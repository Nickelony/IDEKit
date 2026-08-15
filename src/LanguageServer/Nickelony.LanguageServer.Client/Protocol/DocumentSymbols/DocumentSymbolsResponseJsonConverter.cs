using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Deserializes document-symbol responses tolerantly so one malformed element cannot fail the whole response.
/// </summary>
/// <remarks>
/// <para>
/// A JSON <see langword="null"/> response deserializes to a <see langword="null"/> response because the converter
/// is not invoked for JSON null. A payload that is not an array produces an empty response with a warning.
/// </para>
/// <para>
/// Symbol entries without a usable name, a numeric kind, and at least one usable range or location are skipped
/// with a warning; non-object elements are skipped likewise. Range coordinates must be integers, while negative or
/// out-of-document coordinates remain representable for the consuming conversion to validate and clamp. A range
/// whose end position is missing or malformed degrades to an empty range at its start so an otherwise usable entry
/// stays usable, mirroring the definition-target reader.
/// </para>
/// <para>
/// Writing echoes whichever shape an entry carries: hierarchical entries write <c>range</c>,
/// <c>selectionRange</c>, and <c>children</c>, while flat entries write <c>location</c> and <c>containerName</c>.
/// The modeled members (including <c>tags</c> and <c>deprecated</c>) round-trip; unmodeled protocol members are not
/// preserved.
/// </para>
/// </remarks>
public sealed class DocumentSymbolsResponseJsonConverter : JsonConverter<DocumentSymbolsResponse>
{
	private readonly ILogger _logger;

	/// <summary>
	/// Initializes a new instance of the <see cref="DocumentSymbolsResponseJsonConverter"/> class.
	/// </summary>
	public DocumentSymbolsResponseJsonConverter()
		: this(NullLogger.Instance)
	{ }

	/// <summary>
	/// Initializes a new instance of the <see cref="DocumentSymbolsResponseJsonConverter"/> class.
	/// </summary>
	/// <param name="logger">The logger used for malformed-payload diagnostics.</param>
	public DocumentSymbolsResponseJsonConverter(ILogger? logger)
		=> _logger = logger ?? NullLogger.Instance;

	/// <inheritdoc/>
	public override DocumentSymbolsResponse Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		using JsonDocument document = JsonDocument.ParseValue(ref reader);
		JsonElement root = document.RootElement;

		if (root.ValueKind != JsonValueKind.Array)
		{
			_logger.LogWarning("Ignoring malformed document-symbol payload because its JSON kind {Kind} is not an array.", root.ValueKind);
			return new DocumentSymbolsResponse();
		}

		var symbols = new List<DocumentSymbolPayload>();

		foreach (JsonElement symbolElement in root.EnumerateArray())
			TryAppendSymbol(symbols, symbolElement);

		return new DocumentSymbolsResponse(symbols);
	}

	/// <inheritdoc/>
	public override void Write(Utf8JsonWriter writer, DocumentSymbolsResponse value, JsonSerializerOptions options)
	{
		writer.WriteStartArray();

		for (int i = 0; i < value.Symbols.Count; i++)
			WriteSymbol(writer, value.Symbols[i]);

		writer.WriteEndArray();
	}

	/// <summary>
	/// Parses one symbol element and appends it when it is usable; malformed elements are skipped with a warning.
	/// </summary>
	/// <param name="symbols">The destination list that receives the parsed symbol.</param>
	/// <param name="symbolElement">The payload element to parse.</param>
	private void TryAppendSymbol(List<DocumentSymbolPayload> symbols, JsonElement symbolElement)
	{
		if (TryParseSymbol(symbolElement, out DocumentSymbolPayload? symbol))
		{
			symbols.Add(symbol);
			return;
		}

		if (symbolElement.ValueKind == JsonValueKind.Object)
		{
			_logger.LogWarning("Ignoring a document symbol that did not contain a usable name, kind, and range or location.");
		}
		else if (symbolElement.ValueKind != JsonValueKind.Null)
		{
			_logger.LogWarning("Skipping malformed document symbol because its JSON kind {Kind} is not an object.", symbolElement.ValueKind);
		}
	}

	/// <summary>
	/// Parses one usable document-symbol entry from a payload element.
	/// </summary>
	/// <param name="symbolElement">The payload element to parse.</param>
	/// <param name="symbol">Receives the parsed symbol when the element is usable.</param>
	/// <returns><see langword="true"/> when the element carries a usable symbol.</returns>
	private bool TryParseSymbol(JsonElement symbolElement, [NotNullWhen(true)] out DocumentSymbolPayload? symbol)
	{
		symbol = null;

		if (symbolElement.ValueKind != JsonValueKind.Object)
			return false;

		string? name = JsonElementReadHelpers.TryGetString(symbolElement, "name");

		if (string.IsNullOrWhiteSpace(name)
			|| !JsonElementReadHelpers.TryGetProperty(symbolElement, "kind", out JsonElement kindElement)
			|| kindElement.ValueKind != JsonValueKind.Number
			|| !kindElement.TryGetInt32(out int kind))
		{
			return false;
		}

		// Hierarchical entries carry one or both range properties; flat entries carry a location.
		ProtocolRangePayload? range = TryReadRange(symbolElement, "range");
		ProtocolRangePayload? selectionRange = TryReadRange(symbolElement, "selectionRange");
		SymbolLocationPayload? location = TryReadLocation(symbolElement, out SymbolLocationPayload? parsedLocation)
			? parsedLocation
			: null;

		if (range is null && selectionRange is null && location is null)
			return false;

		symbol = new DocumentSymbolPayload
		{
			Name = name,
			Detail = JsonElementReadHelpers.TryGetString(symbolElement, "detail"),
			Kind = (SymbolKind)kind,
			Tags = TryReadSymbolTags(symbolElement),
			Deprecated = JsonElementReadHelpers.TryGetBoolean(symbolElement, "deprecated"),
			Range = range,
			SelectionRange = selectionRange,
			Location = location,
			ContainerName = JsonElementReadHelpers.TryGetString(symbolElement, "containerName"),
			Children = TryReadChildren(symbolElement)
		};

		return true;
	}

	/// <summary>
	/// Parses the nested children of a hierarchical symbol element.
	/// </summary>
	/// <param name="symbolElement">The symbol element to read from.</param>
	/// <returns>
	/// The usable children in response order, or <see langword="null"/> when the property is absent or a JSON
	/// null; an attached empty array reads as an empty list.
	/// </returns>
	private IReadOnlyList<DocumentSymbolPayload>? TryReadChildren(JsonElement symbolElement)
	{
		if (!JsonElementReadHelpers.TryGetProperty(symbolElement, "children", out JsonElement childrenElement)
			|| childrenElement.ValueKind == JsonValueKind.Null)
		{
			return null;
		}

		if (childrenElement.ValueKind != JsonValueKind.Array)
		{
			_logger.LogWarning("Ignoring malformed document-symbol child list because its JSON kind {Kind} is not an array.", childrenElement.ValueKind);
			return null;
		}

		var children = new List<DocumentSymbolPayload>();

		// TryAppendSymbol warns for every skipped entry (malformed kind, or an object without a usable
		// name/kind/range), so a child that does not survive is never dropped silently.
		foreach (JsonElement childElement in childrenElement.EnumerateArray())
			TryAppendSymbol(children, childElement);

		return [.. children];
	}

	/// <summary>
	/// Parses the optional tag array of a symbol element.
	/// </summary>
	/// <param name="symbolElement">The symbol element to read from.</param>
	/// <returns>
	/// The parsed tags, or <see langword="null"/> when the property is absent or a JSON null; an attached empty
	/// array reads as an empty list.
	/// </returns>
	private ReadOnlyCollection<SymbolTag>? TryReadSymbolTags(JsonElement symbolElement)
	{
		if (!JsonElementReadHelpers.TryGetProperty(symbolElement, "tags", out JsonElement tagsElement)
			|| tagsElement.ValueKind == JsonValueKind.Null)
		{
			return null;
		}

		if (tagsElement.ValueKind != JsonValueKind.Array)
		{
			_logger.LogWarning("Ignoring malformed document-symbol tag list because its JSON kind {Kind} is not an array.", tagsElement.ValueKind);
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

			_logger.LogWarning("Skipping a malformed document-symbol tag entry with JSON kind {Kind}.", tagElement.ValueKind);
		}

		return Array.AsReadOnly([.. tags]);
	}

	/// <summary>
	/// Parses one location property of a flat symbol element.
	/// </summary>
	/// <param name="symbolElement">The symbol element to read from.</param>
	/// <param name="location">Receives the parsed location when the property is usable.</param>
	/// <returns><see langword="true"/> when the property carries a usable URI and range.</returns>
	private static bool TryReadLocation(JsonElement symbolElement, [NotNullWhen(true)] out SymbolLocationPayload? location)
	{
		location = null;

		if (!JsonElementReadHelpers.TryGetProperty(symbolElement, "location", out JsonElement locationElement)
			|| locationElement.ValueKind != JsonValueKind.Object)
		{
			return false;
		}

		string? uri = JsonElementReadHelpers.TryGetString(locationElement, "uri");

		// The payload contract requires an absolute URI; a blank or relative URI is treated as a malformed entry,
		// so parsing skips it instead of surfacing a location that cannot name a document.
		if (string.IsNullOrWhiteSpace(uri)
			|| !Uri.TryCreate(uri, UriKind.Absolute, out _)
			|| TryReadRange(locationElement, "range") is not { } range)
		{
			return false;
		}

		location = new(uri, range);
		return true;
	}

	/// <summary>
	/// Parses one range property of a payload element.
	/// </summary>
	/// <param name="payloadElement">The payload element to read from.</param>
	/// <param name="propertyName">The property that carries the range.</param>
	/// <returns>The parsed range, or <see langword="null"/> when the property carries none.</returns>
	private static ProtocolRangePayload? TryReadRange(JsonElement payloadElement, string propertyName)
	{
		if (!JsonElementReadHelpers.TryGetProperty(payloadElement, propertyName, out JsonElement rangeElement)
			|| rangeElement.ValueKind != JsonValueKind.Object
			|| !JsonElementReadHelpers.TryReadPosition(rangeElement, "start", out ProtocolPosition start))
		{
			return null;
		}

		// A missing or malformed end position degrades to an empty range at the start so an otherwise
		// usable entry stays usable.
		if (!JsonElementReadHelpers.TryReadPosition(rangeElement, "end", out ProtocolPosition end))
			end = start;

		return new(start, end);
	}

	/// <summary>
	/// Writes one document-symbol entry with its present members in canonical protocol order.
	/// </summary>
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

		// kind is required in both DocumentSymbol and SymbolInformation; always write it (even the schema-invalid
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
			ProtocolJsonWriteHelpers.WriteRange(writer, range);
		}

		if (symbol.SelectionRange is { } selectionRange)
		{
			writer.WritePropertyName("selectionRange");
			ProtocolJsonWriteHelpers.WriteRange(writer, selectionRange);
		}

		if (symbol.Location is { } location)
		{
			writer.WritePropertyName("location");
			writer.WriteStartObject();
			writer.WriteString("uri", location.Uri);
			writer.WritePropertyName("range");
			ProtocolJsonWriteHelpers.WriteRange(writer, location.Range);
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

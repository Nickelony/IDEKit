using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Converts definition responses between LSP location or location-link payloads and typed definition targets.</summary>
/// <remarks>
/// <para>
/// A JSON <see langword="null"/> response deserializes to a <see langword="null"/> response because the converter is not invoked
/// for JSON <see langword="null"/>. Malformed target entries are skipped instead of failing the whole response.
/// </para>
/// <para>
/// A definition target is dereferenced to open a document and <see cref="DefinitionTargetPayload.Uri"/> is non-nullable, so an entry
/// is kept only when it carries an absolute URI. A skipped entry is reported with a warning whether it is an object without a usable URI and range
/// or a non-object value; only a JSON <see langword="null"/> entry stays silent.
/// </para>
/// </remarks>
internal sealed class DefinitionResponseJsonConverter : TolerantPayloadJsonConverter<DefinitionResponse>
{
	public DefinitionResponseJsonConverter() { }

	/// <param name="logger">The logger used for malformed-payload diagnostics.</param>
	public DefinitionResponseJsonConverter(ILogger? logger)
		: base(logger) { }

	/// <inheritdoc/>
	public override DefinitionResponse Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		using var document = JsonDocument.ParseValue(ref reader);
		JsonElement root = document.RootElement;

		if (root.ValueKind == JsonValueKind.Array)
		{
			var targets = new List<DefinitionTargetPayload>();

			foreach (JsonElement definitionElement in root.EnumerateArray())
			{
				if (TryParseDefinitionTarget(definitionElement, out DefinitionTargetPayload? target))
					targets.Add(target);
				else
					LogSkippedDefinitionTarget(definitionElement);
			}

			return new([.. targets]);
		}

		if (TryParseDefinitionTarget(root, out DefinitionTargetPayload? targetResponse))
			return new DefinitionResponse([targetResponse]);

		LogSkippedDefinitionTarget(root);

		return new DefinitionResponse([]);
	}

	/// <summary>Reports one skipped definition entry.</summary>
	/// <param name="definitionElement">The skipped payload element.</param>
	/// <remarks>
	/// An object entry that lacked a usable target and a non-object entry are reported distinctly so the diagnostic names the actual defect;
	/// a JSON <see langword="null"/> entry stays silent.
	/// </remarks>
	private void LogSkippedDefinitionTarget(JsonElement definitionElement)
	{
		if (definitionElement.ValueKind == JsonValueKind.Object)
			Logger.LogWarning("Ignoring a definition target that did not contain a usable URI and range.");
		else if (definitionElement.ValueKind != JsonValueKind.Null)
			Logger.LogWarning("Skipping malformed definition target because its JSON kind {Kind} is not an object.", definitionElement.ValueKind);
	}

	/// <inheritdoc/>
	public override void Write(Utf8JsonWriter writer, DefinitionResponse value, JsonSerializerOptions options)
	{
		writer.WriteStartArray();

		for (int i = 0; i < value.Targets.Count; i++)
			WriteDefinitionTarget(writer, value.Targets[i]);

		writer.WriteEndArray();
	}

	private static void WriteDefinitionTarget(Utf8JsonWriter writer, DefinitionTargetPayload target)
	{
		if (target.Uri is null)
			throw new InvalidOperationException("A definition target without a URI cannot be serialized.");

		writer.WriteStartObject();

		if (target.SelectionRange is { } selectionRange)
		{
			// The parsed payload was a location link, so write its distinct selection range back.
			writer.WriteString("targetUri", target.Uri);

			writer.WritePropertyName("targetRange");
			RangePayloadJsonConverter.WriteRange(writer, target.TargetRange);
			writer.WritePropertyName("targetSelectionRange");
			RangePayloadJsonConverter.WriteRange(writer, selectionRange);

			if (target.OriginSelectionRange is { } originSelectionRange)
			{
				writer.WritePropertyName("originSelectionRange");
				RangePayloadJsonConverter.WriteRange(writer, originSelectionRange);
			}
		}
		else
		{
			writer.WriteString("uri", target.Uri);

			writer.WritePropertyName("range");
			RangePayloadJsonConverter.WriteRange(writer, target.TargetRange);
		}

		writer.WriteEndObject();
	}

	/// <summary>Parses one usable definition target from a definition payload element.</summary>
	/// <param name="definitionElement">The definition payload element.</param>
	/// <param name="target">Receives the parsed definition target.</param>
	/// <returns><see langword="true"/> when a usable target was found.</returns>
	private static bool TryParseDefinitionTarget(JsonElement definitionElement, [NotNullWhen(true)] out DefinitionTargetPayload? target)
	{
		target = null;

		if (definitionElement.ValueKind != JsonValueKind.Object)
			return false;

		// Non-string URI properties are treated as absent so one malformed entry cannot fail the response.
		string? uri = JsonElementReader.GetString(definitionElement, "targetUri") ?? JsonElementReader.GetString(definitionElement, "uri");

		// A relative or otherwise non-absolute URI is rejected because it names no document a host could open.
		if (string.IsNullOrWhiteSpace(uri) || !Uri.TryCreate(uri, UriKind.Absolute, out _))
			return false;

		// Location links carry a distinct selection range; plain locations carry one range that
		// serves as the target range.
		RangePayload? selectionRange = ReadRange(definitionElement, "targetSelectionRange");

		RangePayload? targetRange = ReadRange(definitionElement, "targetRange") ?? ReadRange(definitionElement, "range") ?? selectionRange;

		if (targetRange is null)
			return false;

		// The optional origin range identifies the range in the requesting document that the link refers to; it is
		// preserved for hosts that highlight the origin of a location link.
		RangePayload? originSelectionRange = ReadRange(definitionElement, "originSelectionRange");

		target = new DefinitionTargetPayload(uri, TargetRange: targetRange.Value, selectionRange, originSelectionRange);
		return true;
	}

	/// <summary>
	/// Parses one protocol range property of a definition payload element, rejecting negative or inverted coordinates because the target
	/// is dereferenced to open a document.
	/// </summary>
	/// <param name="definitionElement">The definition payload element.</param>
	/// <param name="rangeProperty">The property name that carries the range.</param>
	/// <returns>The parsed zero-based protocol range, or <see langword="null"/> when the property carries none.</returns>
	private static RangePayload? ReadRange(JsonElement definitionElement, string rangeProperty)
	{
		return RangePayloadJsonConverter.ReadRangeProperty(
			payloadElement: definitionElement,
			propertyName: rangeProperty,
			allowNegative: false,
			allowInverted: false
		);
	}
}

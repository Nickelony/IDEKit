using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Deserializes hover responses tolerantly so one malformed optional member cannot fail the whole response.</summary>
/// <remarks>
/// <para>
/// A JSON <see langword="null"/> response deserializes to a <see langword="null"/> response because the converter
/// is not invoked for JSON <see langword="null"/>. A <c>range</c> that is not an object or that carries no usable start position is dropped
/// with a warning instead of failing the response, so the hover contents stay usable. The <c>contents</c> value is read as <c>MarkupContent</c>,
/// <c>MarkedString</c> or <c>MarkedString[]</c>; an element that is none of them is skipped with a warning.
/// </para>
/// <para>
/// Writing emits a member only when the response carries it, so a payload without <c>contents</c> or a <c>range</c> member round-trips
/// without an explicit JSON <see langword="null"/>.
/// </para>
/// </remarks>
internal sealed class HoverResponseJsonConverter : TolerantPayloadJsonConverter<HoverResponse>
{
	public HoverResponseJsonConverter() { }

	/// <param name="logger">The logger used for malformed-payload diagnostics.</param>
	public HoverResponseJsonConverter(ILogger? logger)
		: base(logger) { }

	/// <inheritdoc/>
	public override HoverResponse Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		using var document = JsonDocument.ParseValue(ref reader);
		JsonElement root = document.RootElement;

		if (root.ValueKind != JsonValueKind.Object)
		{
			Logger.LogWarning("Ignoring malformed hover payload because its JSON kind {Kind} is not an object.", root.ValueKind);
			return new HoverResponse();
		}

		IReadOnlyList<MarkupContentPayload> contents = JsonElementReader.TryGetProperty(root, "contents", out JsonElement contentsElement)
			? ReadContents(contentsElement)
			: [];

		return new HoverResponse { Contents = contents, Range = ReadRange(root) };
	}

	/// <inheritdoc/>
	public override void Write(Utf8JsonWriter writer, HoverResponse value, JsonSerializerOptions options)
	{
		writer.WriteStartObject();

		if (value.Contents is { Count: > 0 } contents)
		{
			writer.WritePropertyName("contents");

			if (contents.Count == 1)
			{
				JsonSerializer.Serialize(writer, contents[0], options);
			}
			else
			{
				writer.WriteStartArray();

				foreach (MarkupContentPayload fragment in contents)
					JsonSerializer.Serialize(writer, fragment, options);

				writer.WriteEndArray();
			}
		}

		if (value.Range is { } range)
		{
			writer.WritePropertyName("range");
			RangePayloadJsonConverter.WriteRange(writer, range);
		}

		writer.WriteEndObject();
	}

	/// <summary>Reads the <c>contents</c> value into one entry per fragment.</summary>
	/// <param name="contentsElement">The <c>contents</c> value.</param>
	/// <returns>The fragments, or an empty list when the value is JSON <see langword="null"/> or has none usable.</returns>
	/// <remarks>
	/// The CR and LF characters around each array element are removed, because the elements are separate blocks that a consumer joins;
	/// a single value keeps them, since its leading and trailing line breaks can be part of the Markdown the server sent.
	/// </remarks>
	private List<MarkupContentPayload> ReadContents(JsonElement contentsElement)
	{
		var fragments = new List<MarkupContentPayload>();

		if (contentsElement.ValueKind == JsonValueKind.Null)
			return fragments;

		if (contentsElement.ValueKind != JsonValueKind.Array)
		{
			if (ReadFragment(contentsElement) is { } single)
				fragments.Add(single);

			return fragments;
		}

		foreach (JsonElement element in contentsElement.EnumerateArray())
		{
			if (ReadFragment(element) is { } fragment)
				fragments.Add(fragment with { Value = fragment.Value.Trim('\r', '\n') });
		}

		return fragments;
	}

	private MarkupContentPayload? ReadFragment(JsonElement element)
	{
		MarkupContentPayload? fragment = MarkupContentReader.ReadMarkedString(element);

		if (fragment is null && element.ValueKind != JsonValueKind.Null)
		{
			Logger.LogWarning(
				"Skipping malformed hover contents because its JSON kind {Kind} is neither a string nor a markup object.",
				element.ValueKind
			);
		}

		return fragment;
	}

	/// <summary>Reads the optional hover range tolerantly, accepting negative or inverted coordinates.</summary>
	/// <param name="root">The hover payload object.</param>
	/// <returns>
	/// The parsed range, or <see langword="null"/> when the payload carries none. A malformed range is dropped with a warning instead of failing
	/// the response.
	/// </returns>
	private RangePayload? ReadRange(JsonElement root)
	{
		if (!JsonElementReader.TryGetProperty(root, "range", out JsonElement rangeElement) || rangeElement.ValueKind == JsonValueKind.Null)
			return null;

		if (rangeElement.ValueKind != JsonValueKind.Object)
		{
			Logger.LogWarning("Dropping malformed hover range because its JSON kind {Kind} is not an object.", rangeElement.ValueKind);
			return null;
		}

		if (
			RangePayloadJsonConverter.ReadRangeProperty(payloadElement: root, propertyName: "range", allowNegative: true, allowInverted: true) is
			{ } range
		)
		{
			return range;
		}

		// A missing or malformed end position degrades to an empty range at the start, so an otherwise usable hover
		// keeps its range; only an unusable start reaches this warning.
		Logger.LogWarning("Dropping malformed hover range because it does not carry a usable start position.");
		return null;
	}
}

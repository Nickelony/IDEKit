using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>
/// Reads collections of <typeparamref name="TElement"/> while skipping <see langword="null"/> or malformed elements with a warning instead of failing
/// the whole response.
/// </summary>
/// <typeparam name="TElement">The element type whose collections are read tolerantly.</typeparam>
/// <remarks>
/// A non-<see langword="null"/> root that is not an array degrades to an empty collection with a warning by default.
/// With <c>requireArrayRoot</c> set, it fails deserialization with <see cref="JsonException"/> instead. A JSON <see langword="null"/> root
/// never reaches the converter, so the collection stays <see langword="null"/>.
/// </remarks>
internal sealed class TolerantCollectionJsonConverter<TElement> : JsonConverterFactory
{
	private readonly ILogger _logger;
	private readonly bool _requireArrayRoot;
	private readonly Func<TElement, bool>? _isUsableElement;

	public TolerantCollectionJsonConverter()
		: this(NullLogger.Instance) { }

	/// <param name="logger">The logger used for malformed-payload diagnostics.</param>
	public TolerantCollectionJsonConverter(ILogger? logger)
		: this(logger, requireArrayRoot: false) { }

	/// <param name="logger">The logger used for malformed-payload diagnostics.</param>
	/// <param name="requireArrayRoot">
	/// Whether a root that is neither a JSON <see langword="null"/> nor an array must fail deserialization with <see cref="JsonException"/>
	/// instead of degrading to an empty collection. Use this for edit-bearing members, where an empty collection is a valid outcome
	/// ("nothing to apply") and would silently turn a malformed payload into a successful no-op.
	/// </param>
	/// <param name="isUsableElement">
	/// A predicate that reports whether a bound element is usable, or <see langword="null"/> to accept every bound value. Use it for members
	/// whose required shape an empty JSON object would otherwise satisfy as a no-op.
	/// </param>
	public TolerantCollectionJsonConverter(ILogger? logger, bool requireArrayRoot, Func<TElement, bool>? isUsableElement = null)
	{
		_logger = logger ?? NullLogger.Instance;
		_requireArrayRoot = requireArrayRoot;
		_isUsableElement = isUsableElement;
	}

	/// <inheritdoc/>
	public override bool CanConvert(Type typeToConvert)
	{
		return typeToConvert == typeof(TElement[])
			|| typeToConvert == typeof(IReadOnlyList<TElement>)
			|| typeToConvert == typeof(IList<TElement>)
			|| typeToConvert == typeof(List<TElement>);
	}

	/// <inheritdoc/>
	public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
	{
		return (JsonConverter)
			Activator.CreateInstance(
				typeof(TolerantCollectionConverter<>).MakeGenericType(typeof(TElement), typeToConvert),
				_logger,
				_requireArrayRoot,
				_isUsableElement
			)!;
	}

	/// <summary>Reads and writes one concrete collection shape of <typeparamref name="TElement"/>.</summary>
	/// <typeparam name="TCollection">The collection shape to read.</typeparam>
	private sealed class TolerantCollectionConverter<TCollection> : JsonConverter<TCollection>
	{
		private readonly ILogger _logger;
		private readonly bool _requireArrayRoot;
		private readonly Func<TElement, bool>? _isUsableElement;

		/// <param name="logger">The logger used for malformed-payload diagnostics.</param>
		/// <param name="requireArrayRoot">Whether a non-array, non-<see langword="null"/> root must fail deserialization.</param>
		/// <param name="isUsableElement">The predicate that reports whether a bound element is usable.</param>
		public TolerantCollectionConverter(ILogger logger, bool requireArrayRoot, Func<TElement, bool>? isUsableElement)
		{
			_logger = logger;
			_requireArrayRoot = requireArrayRoot;
			_isUsableElement = isUsableElement;
		}

		/// <inheritdoc/>
		public override TCollection? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			using var document = JsonDocument.ParseValue(ref reader);
			JsonElement root = document.RootElement;

			if (root.ValueKind != JsonValueKind.Array)
			{
				if (_requireArrayRoot)
				{
					throw new JsonException(
						$"A {typeof(TElement).Name} collection carries a malformed root: expected a JSON array, found {root.ValueKind}."
					);
				}

				_logger.LogWarning(
					"Ignoring malformed {ElementType} collection because its JSON kind {Kind} is not an array.",
					typeof(TElement).Name,
					root.ValueKind
				);

				return Materialize([]);
			}

			var elements = new List<TElement>(root.GetArrayLength());

			foreach (JsonElement element in root.EnumerateArray())
			{
				if (element.ValueKind == JsonValueKind.Null)
				{
					_logger.LogWarning("Skipping a null {ElementType} element.", typeof(TElement).Name);
					continue;
				}

				try
				{
					TElement? value = element.Deserialize<TElement>(options);

					if (value is null)
					{
						_logger.LogWarning("Skipping a {ElementType} element that deserialized to null.", typeof(TElement).Name);
						continue;
					}

					if (_isUsableElement is not null && !_isUsableElement(value))
					{
						_logger.LogWarning("Skipping an unusable {ElementType} element; it was missing required members.", typeof(TElement).Name);
						continue;
					}

					elements.Add(value);
				}
				catch (Exception exception) when (exception is JsonException or InvalidOperationException)
				{
					_logger.LogWarning(exception, "Skipping a malformed {ElementType} element.", typeof(TElement).Name);
				}
			}

			return Materialize(elements);
		}

		/// <inheritdoc/>
		public override void Write(Utf8JsonWriter writer, TCollection value, JsonSerializerOptions options)
		{
			writer.WriteStartArray();

			// The serializer never invokes a converter for a null reference value, so the collection is non-null here.
			foreach (TElement element in (IEnumerable<TElement>)value!)
				JsonSerializer.Serialize(writer, element, options);

			writer.WriteEndArray();
		}

		/// <summary>Converts the accumulated elements back into the requested collection shape.</summary>
		private static TCollection? Materialize(List<TElement> elements)
		{
			if (typeof(TCollection) == typeof(TElement[]))
				return (TCollection)(object)elements.ToArray();

			// A read-only interface target stays non-mutable, so a consumer cannot cast back and mutate shared storage.
			if (typeof(TCollection) == typeof(IReadOnlyList<TElement>))
				return (TCollection)(object)Array.AsReadOnly(elements.ToArray());

			return (TCollection)(object)elements;
		}
	}
}

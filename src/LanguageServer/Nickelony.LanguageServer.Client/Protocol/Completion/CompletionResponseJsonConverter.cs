using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Accepts completion responses in either LSP wire form when reading and serializes the typed response using the
/// completion-list form.
/// </summary>
/// <remarks>
/// A JSON <see langword="null"/> response deserializes to a <see langword="null"/> response because the converter is
/// not invoked for JSON null. Malformed elements inside a completion list - non-object entries and objects whose
/// members do not bind, such as an illegal text-edit union or a partial position - are skipped with a warning
/// instead of failing the whole response. Writing always emits the completion-list shape; the item-array form is
/// not reconstructed, and an absent <c>items</c> member stays a JSON <see langword="null"/> so the tolerant
/// round-trip preserves it. Member lookups in the default merge fall back to a case-insensitive match, mirroring
/// the transport's reflection binding, so a casing variant of <c>label</c>, <c>textEdit</c>, or an
/// <c>itemDefaults</c> member cannot silently change the synthesized result.
/// </remarks>
public sealed class CompletionResponseJsonConverter : JsonConverter<CompletionResponse>
{
	private readonly ILogger _logger;

	/// <summary>
	/// Initializes a new instance of the <see cref="CompletionResponseJsonConverter"/> class.
	/// </summary>
	public CompletionResponseJsonConverter()
		: this(NullLogger.Instance)
	{ }

	/// <summary>
	/// Initializes a new instance of the <see cref="CompletionResponseJsonConverter"/> class.
	/// </summary>
	/// <param name="logger">The logger used for malformed-payload diagnostics.</param>
	public CompletionResponseJsonConverter(ILogger? logger)
		=> _logger = logger ?? NullLogger.Instance;

	/// <inheritdoc/>
	public override CompletionResponse? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		using JsonDocument document = JsonDocument.ParseValue(ref reader);
		JsonElement root = document.RootElement;

		IReadOnlyList<CompletionItemPayload>? items = null;
		bool isIncomplete = false;

		if (root.ValueKind == JsonValueKind.Array)
		{
			items = DeserializeItems(root, itemDefaultsElement: null, options);
		}
		else if (root.ValueKind == JsonValueKind.Object)
		{
			bool hasSupportedItemsShape = false;

			if (JsonElementReadHelpers.TryGetProperty(root, "items", out JsonElement itemsElement))
			{
				if (itemsElement.ValueKind == JsonValueKind.Array)
				{
					JsonElement? itemDefaultsElement = JsonElementReadHelpers.TryGetProperty(root, "itemDefaults", out JsonElement defaultsElement)
						&& defaultsElement.ValueKind == JsonValueKind.Object
							? defaultsElement
							: null;

					items = DeserializeItems(itemsElement, itemDefaultsElement, options);

					hasSupportedItemsShape = true;
				}
				else if (itemsElement.ValueKind == JsonValueKind.Null)
				{
					items = null;
					hasSupportedItemsShape = true;
				}
				else
				{
					_logger.LogWarning("Ignoring malformed completion-list payload because 'items' had unsupported JSON kind {Kind}.", itemsElement.ValueKind);
				}
			}
			else
			{
				_logger.LogWarning("Ignoring malformed completion-list payload because the 'items' property was missing.");
			}

			if (hasSupportedItemsShape
				&& JsonElementReadHelpers.TryGetProperty(root, "isIncomplete", out JsonElement isIncompleteElement)
				&& (isIncompleteElement.ValueKind == JsonValueKind.True || isIncompleteElement.ValueKind == JsonValueKind.False))
			{
				isIncomplete = isIncompleteElement.GetBoolean();
			}
		}
		else
		{
			_logger.LogWarning("Ignoring malformed completion response because its JSON kind {Kind} is neither an array nor an object.", root.ValueKind);
		}

		return new(items, isIncomplete);
	}

	private List<CompletionItemPayload> DeserializeItems(
		JsonElement itemsElement,
		JsonElement? itemDefaultsElement,
		JsonSerializerOptions options)
	{
		var items = new List<CompletionItemPayload>();

		foreach (JsonElement itemElement in itemsElement.EnumerateArray())
		{
			if (itemElement.ValueKind != JsonValueKind.Object)
			{
				// Spec-shaped items are objects; one malformed element must not fail the whole list.
				if (itemElement.ValueKind != JsonValueKind.Null)
				{
					_logger.LogWarning("Skipping malformed completion item because its JSON kind {Kind} is not an object.",
						itemElement.ValueKind);
				}

				continue;
			}

			try
			{
				CompletionItemPayload item = itemElement.Deserialize<CompletionItemPayload>(options) ?? new CompletionItemPayload();

				// The item is bound once and the list defaults are folded into the bound payload afterwards; merging
				// them through a JsonNode round trip would parse every item a second time.
				items.Add(itemDefaultsElement is JsonElement defaultsElement
					? ApplyCompletionItemDefaults(item, itemElement, defaultsElement, options)
					: item);
			}
			catch (Exception exception) when (exception is JsonException or InvalidOperationException)
			{
				// An item whose members do not bind (for example an illegal text-edit union or a partial
				// position) is dropped with a warning; the rest of the list stays usable.
				_logger.LogWarning(exception, "Skipping malformed completion item because its payload could not be deserialized.");
			}
		}

		return items;
	}

	/// <summary>
	/// Folds a completion list's <c>itemDefaults</c> into an item that was already bound to its payload.
	/// </summary>
	/// <param name="item">The bound completion item.</param>
	/// <param name="itemElement">The item's raw JSON element, inspected to detect members the item itself carries.</param>
	/// <param name="itemDefaultsElement">The <c>itemDefaults</c> object of the completion list.</param>
	/// <param name="options">The serializer options used to deserialize a nested default value.</param>
	/// <returns>The item with the applicable defaults applied.</returns>
	/// <remarks>
	/// A default is applied only when the item carries no non-null member of that name; both an absent member and a
	/// JSON <see langword="null"/> member take the default. The member presence is read from the raw element with the
	/// same case-insensitive lookup the transport applies, so a casing variant cannot silently change the result.
	/// </remarks>
	private static CompletionItemPayload ApplyCompletionItemDefaults(
		CompletionItemPayload item,
		JsonElement itemElement,
		JsonElement itemDefaultsElement,
		JsonSerializerOptions options)
	{
		IReadOnlyList<string>? commitCharacters = item.CommitCharacters;
		InsertTextFormat? insertTextFormat = item.InsertTextFormat;
		CompletionTextEditPayload? textEdit = item.TextEdit;
		IDictionary<string, JsonElement>? extensionData = item.ExtensionData;

		if (!HasNonNullProperty(itemElement, "commitCharacters")
			&& JsonElementReadHelpers.TryGetProperty(itemDefaultsElement, "commitCharacters", out JsonElement defaultCommitCharacters)
			&& defaultCommitCharacters.ValueKind != JsonValueKind.Null)
		{
			commitCharacters = defaultCommitCharacters.Deserialize<IReadOnlyList<string>>(options);
		}

		if (!HasNonNullProperty(itemElement, "insertTextFormat")
			&& JsonElementReadHelpers.TryGetProperty(itemDefaultsElement, "insertTextFormat", out JsonElement defaultInsertTextFormat)
			&& defaultInsertTextFormat.ValueKind != JsonValueKind.Null)
		{
			insertTextFormat = defaultInsertTextFormat.Deserialize<InsertTextFormat?>(options);
		}

		if (!HasNonNullProperty(itemElement, "data")
			&& JsonElementReadHelpers.TryGetProperty(itemDefaultsElement, "data", out JsonElement defaultData)
			&& defaultData.ValueKind != JsonValueKind.Null)
		{
			// The default data element is owned by the response document, which is disposed when the read ends, so
			// it is cloned into an independent element before it is stored on the payload.
			extensionData = new Dictionary<string, JsonElement>(extensionData ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal), StringComparer.Ordinal)
			{
				["data"] = defaultData.Clone()
			};
		}

		if (!HasNonNullProperty(itemElement, "textEdit")
			&& JsonElementReadHelpers.TryGetProperty(itemDefaultsElement, "editRange", out JsonElement editRangeElement))
		{
			string? newText = GetDefaultTextEditNewText(itemElement);

			if (!string.IsNullOrEmpty(newText))
				textEdit = CreateDefaultTextEdit(editRangeElement, newText, options);
		}

		return item with
		{
			CommitCharacters = commitCharacters,
			InsertTextFormat = insertTextFormat,
			TextEdit = textEdit,
			ExtensionData = extensionData
		};
	}

	/// <summary>
	/// Reports whether an element carries a member of the given name whose JSON value is not null.
	/// </summary>
	/// <param name="element">The element to inspect.</param>
	/// <param name="propertyName">The member name to look for.</param>
	/// <returns><see langword="true"/> when the member is present with a non-null value; otherwise, <see langword="false"/>.</returns>
	private static bool HasNonNullProperty(JsonElement element, string propertyName)
		=> JsonElementReadHelpers.TryGetProperty(element, propertyName, out JsonElement value)
			&& value.ValueKind != JsonValueKind.Null;

	private static string? GetDefaultTextEditNewText(JsonElement itemElement)
	{
		// LSP 3.17 defines the default text-edit text as "textEditText ?? label": when a client synthesizes an
		// edit from itemDefaults.editRange, insertText must not be used even though the item carries it.
		return TryGetNonEmptyString(itemElement, "textEditText")
			?? TryGetNonEmptyString(itemElement, "label");
	}

	private static string? TryGetNonEmptyString(JsonElement element, string propertyName)
	{
		string? value = JsonElementReadHelpers.TryGetString(element, propertyName);
		return string.IsNullOrEmpty(value) ? null : value;
	}

	private static CompletionTextEditPayload? CreateDefaultTextEdit(
		JsonElement editRangeElement,
		string newText,
		JsonSerializerOptions options)
	{
		if (LooksLikeProtocolRange(editRangeElement))
		{
			return new CompletionRangeTextEditPayload
			{
				NewText = newText,
				Range = editRangeElement.Deserialize<ProtocolRangePayload>(options)
			};
		}

		if (!TryGetObjectProperty(editRangeElement, "insert", out JsonElement insertRange)
			|| !TryGetObjectProperty(editRangeElement, "replace", out JsonElement replaceRange))
		{
			return null;
		}

		return new CompletionInsertReplaceTextEditPayload
		{
			NewText = newText,
			Insert = insertRange.Deserialize<ProtocolRangePayload>(options),
			Replace = replaceRange.Deserialize<ProtocolRangePayload>(options)
		};
	}

	private static bool LooksLikeProtocolRange(JsonElement element)
	{
		return element.ValueKind == JsonValueKind.Object
			&& JsonElementReadHelpers.TryGetProperty(element, "start", out _)
			&& JsonElementReadHelpers.TryGetProperty(element, "end", out _);
	}

	private static bool TryGetObjectProperty(JsonElement element, string propertyName, out JsonElement propertyValue)
	{
		if (JsonElementReadHelpers.TryGetProperty(element, propertyName, out propertyValue) && propertyValue.ValueKind == JsonValueKind.Object)
			return true;

		propertyValue = default;
		return false;
	}

	/// <inheritdoc/>
	public override void Write(Utf8JsonWriter writer, CompletionResponse value, JsonSerializerOptions options)
	{
		writer.WriteStartObject();
		writer.WriteBoolean("isIncomplete", value.IsIncomplete);
		writer.WritePropertyName("items");

		if (value.Items is null)
			writer.WriteNullValue();
		else
			JsonSerializer.Serialize(writer, value.Items, options);

		writer.WriteEndObject();
	}
}

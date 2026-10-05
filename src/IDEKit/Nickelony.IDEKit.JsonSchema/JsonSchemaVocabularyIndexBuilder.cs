using System.Text.Json;

namespace Nickelony.IDEKit.JsonSchema;

/// <summary>Builds an immutable <see cref="JsonSchemaVocabularyIndex"/> from a JSON Schema.</summary>
/// <remarks>
/// The builder accepts schema text and a parsed <see cref="JsonElement"/>, and reports outcomes through <see cref="JsonSchemaVocabularyIndexResult"/>.
/// A property name or constant value that occurs more than once is kept at its first occurrence in traversal order, and a property descriptor retains
/// that occurrence's types and description. A property that is a <c>$ref</c> takes the type and the description it leaves out from the schema the
/// reference names. A <c>$ref</c> resolves when it is <c>#</c> or a JSON Pointer fragment such as <c>#/$defs/name</c> into the
/// same document; any other reference adds a warning and is skipped.
/// </remarks>
public static class JsonSchemaVocabularyIndexBuilder
{
	private static readonly JsonDocumentOptions s_parseOptions = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

	// The types are emitted in the declaration order of JsonSchemaPropertyType so the list order stays deterministic and matches the enum.
	private static readonly JsonSchemaPropertyType[] s_typeDeclarationOrder = Enum.GetValues<JsonSchemaPropertyType>();

	/// <summary>Builds a vocabulary index from a parsed schema.</summary>
	/// <param name="schema">The root schema to index, which is a JSON object or a boolean.</param>
	/// <returns>The built vocabulary index and any warnings, or a failed result when the root is neither an object nor a boolean.</returns>
	/// <remarks>
	/// The element must belong to a document that stays undisposed for the duration of the call. The result keeps no reference to it.
	/// </remarks>
	public static JsonSchemaVocabularyIndexResult Build(JsonElement schema)
	{
		if (schema.ValueKind is not (JsonValueKind.Object or JsonValueKind.True or JsonValueKind.False))
		{
			var exception = new JsonException($"The schema root has the JSON kind {schema.ValueKind}, but a schema is an object or a boolean.");

			return Fail(JsonSchemaVocabularyIndexFailureCodes.ParseFailed, $"The value could not be read as a JSON schema: {exception.Message}", exception);
		}

		var context = new BuildContext(schema);

		foreach (JsonElement currentSchema in SchemaTraversal.FlattenSchemas(schema, context.Warnings))
			Collect(currentSchema, context);

		return new JsonSchemaVocabularyIndexResult(new JsonSchemaVocabularyIndex(context.Properties, context.Constants), context.Warnings);
	}

	/// <summary>Builds a vocabulary index from schema text read through a reader.</summary>
	/// <param name="reader">The reader over the schema text.</param>
	/// <returns>The built vocabulary index and any warnings, or a failed result when the schema text cannot be read or parsed.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="reader"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// The method reads the entire reader and does not dispose it. The text may contain comments and trailing commas.
	/// </remarks>
	public static JsonSchemaVocabularyIndexResult Build(TextReader reader)
	{
		ArgumentNullException.ThrowIfNull(reader);

		string schemaJson;

		try
		{
			schemaJson = reader.ReadToEnd();
		}
		catch (Exception exception)
		{
			return Fail(JsonSchemaVocabularyIndexFailureCodes.ReadFailed, $"The schema text could not be read: {exception.Message}", exception);
		}

		JsonDocument document;

		try
		{
			document = JsonDocument.Parse(schemaJson, s_parseOptions);
		}
		catch (JsonException exception)
		{
			return Fail(JsonSchemaVocabularyIndexFailureCodes.ParseFailed, $"The schema could not be parsed: {exception.Message}", exception);
		}

		using (document)
		{
			return Build(document.RootElement);
		}
	}

	/// <summary>Creates a failed result that carries the exception behind the failure.</summary>
	private static JsonSchemaVocabularyIndexResult Fail(string code, string message, Exception exception) =>
		new(new JsonSchemaVocabularyIndexFailure(code, message, exception));

	private static void Collect(JsonElement schema, BuildContext context)
	{
		// Extract string values from const declarations at every visited schema.
		if (schema.TryGetProperty("const", out JsonElement constant) && constant.ValueKind == JsonValueKind.String && constant.GetString() is { } constantValue)
			AddConstant(constantValue, context);

		// Extract string values from enum declarations at every visited schema.
		if (schema.TryGetProperty("enum", out JsonElement enumeration) && enumeration.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement enumValue in enumeration.EnumerateArray())
			{
				if (enumValue.ValueKind == JsonValueKind.String && enumValue.GetString() is { } text)
					AddConstant(text, context);
			}
		}

		if (!schema.TryGetProperty("properties", out JsonElement properties) || properties.ValueKind != JsonValueKind.Object)
			return;

		foreach (JsonProperty property in properties.EnumerateObject())
			AddProperty(property.Name, property.Value, context);
	}

	private static void AddProperty(string name, JsonElement propertySchema, BuildContext context)
	{
		// The traversal reports a value that is not a schema, so the property is left out here.
		if (propertySchema.ValueKind is not (JsonValueKind.Object or JsonValueKind.True or JsonValueKind.False))
			return;

		// When a name occurs more than once, keep the first occurrence in traversal order.
		if (!context.PropertyNames.Add(name))
			return;

		List<JsonSchemaPropertyType> types = ReadTypes(propertySchema);
		string? description = ReadDescription(propertySchema);

		// A keyword beside a $ref wins, and a keyword the property leaves out is read from the referenced schema.
		var followed = new HashSet<string>(StringComparer.Ordinal);
		JsonElement current = propertySchema;

		while ((types.Count == 0 || description is null) && TryFollowReference(current, context.Root, followed, out JsonElement target))
		{
			if (types.Count == 0)
				types = ReadTypes(target);

			description ??= ReadDescription(target);
			current = target;
		}

		context.Properties.Add(new JsonSchemaVocabularyPropertyDescriptor(name, types, description));
	}

	// The traversal reports a reference that does not resolve, so it ends the chain here without a warning.
	private static bool TryFollowReference(JsonElement schema, JsonElement root, HashSet<string> followed, out JsonElement target)
	{
		target = default;

		if (schema.ValueKind != JsonValueKind.Object
			|| !schema.TryGetProperty("$ref", out JsonElement reference)
			|| reference.ValueKind != JsonValueKind.String
			|| reference.GetString() is not { } referenceText)
		{
			return false;
		}

		return SchemaTraversal.TryResolve(root, referenceText, out target, out string pointer) && followed.Add(pointer);
	}

	private static void AddConstant(string value, BuildContext context)
	{
		if (!context.ConstantNames.Add(value))
			return;

		context.Constants.Add(value);
	}

	private static string? ReadDescription(JsonElement schema)
	{
		if (schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("description", out JsonElement description) && description.ValueKind == JsonValueKind.String)
			return description.GetString();

		return null;
	}

	private static List<JsonSchemaPropertyType> ReadTypes(JsonElement schema)
	{
		if (schema.ValueKind != JsonValueKind.Object || !schema.TryGetProperty("type", out JsonElement type))
			return [];

		var declared = new HashSet<JsonSchemaPropertyType>();

		if (type.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement name in type.EnumerateArray())
				AddDeclaredType(name, declared);
		}
		else
		{
			AddDeclaredType(type, declared);
		}

		var result = new List<JsonSchemaPropertyType>();

		foreach (JsonSchemaPropertyType candidate in s_typeDeclarationOrder)
		{
			if (declared.Contains(candidate))
				result.Add(candidate);
		}

		return result;
	}

	// A name that is not one of the JSON Schema type names contributes no type.
	private static void AddDeclaredType(JsonElement name, HashSet<JsonSchemaPropertyType> declared)
	{
		if (name.ValueKind != JsonValueKind.String)
			return;

		JsonSchemaPropertyType? type = name.GetString() switch
		{
			"object" => JsonSchemaPropertyType.Object,
			"array" => JsonSchemaPropertyType.Array,
			"string" => JsonSchemaPropertyType.String,
			"integer" => JsonSchemaPropertyType.Integer,
			"number" => JsonSchemaPropertyType.Number,
			"boolean" => JsonSchemaPropertyType.Boolean,
			"null" => JsonSchemaPropertyType.Null,
			_ => null,
		};

		if (type is { } declaredType)
			declared.Add(declaredType);
	}

	/// <summary>Accumulates the state of one index build.</summary>
	private sealed class BuildContext(JsonElement root)
	{
		internal JsonElement Root { get; } = root;

		internal List<JsonSchemaVocabularyPropertyDescriptor> Properties { get; } = [];

		internal HashSet<string> PropertyNames { get; } = new(StringComparer.Ordinal);

		internal List<string> Constants { get; } = [];

		internal HashSet<string> ConstantNames { get; } = new(StringComparer.Ordinal);

		internal List<string> Warnings { get; } = [];
	}
}

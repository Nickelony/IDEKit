using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;
using System.Diagnostics.CodeAnalysis;

namespace Nickelony.IDEKit.JsonSchema;

/// <summary>
/// Builds an immutable <see cref="JsonSchemaVocabularyIndex"/> from a JSON Schema. It accepts
/// parsed schemas, schema tokens, and schema text, and reports outcomes through
/// <see cref="JsonSchemaVocabularyIndexResult"/>.
/// </summary>
public sealed class JsonSchemaVocabularyIndexBuilder
{
	/// <summary>
	/// Builds a vocabulary index from an already-parsed schema object.
	/// </summary>
	/// <param name="schema">The root schema to index.</param>
	/// <returns>The built vocabulary index and any diagnostics.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="schema"/> is <see langword="null"/>.</exception>
	[SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "The public builder API intentionally exposes instance methods.")]
	public JsonSchemaVocabularyIndexResult Build(JSchema schema)
	{
		ArgumentNullException.ThrowIfNull(schema);

		var context = new BuildContext();

		CollectReachable(schema, context);
		CollectDefinitionSections(schema.ExtensionData, context);

		return new JsonSchemaVocabularyIndexResult(
			new JsonSchemaVocabularyIndex(context.Properties, context.Constants),
			context.Diagnostics);
	}

	/// <summary>
	/// Builds a vocabulary index from a schema token, converting the token to a schema object first.
	/// </summary>
	/// <param name="schemaToken">The token that represents the root schema.</param>
	/// <returns>
	/// The built vocabulary index and diagnostics, or a failed result when conversion or indexing
	/// fails.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="schemaToken"/> is <see langword="null"/>.</exception>
	public JsonSchemaVocabularyIndexResult Build(JToken schemaToken)
	{
		ArgumentNullException.ThrowIfNull(schemaToken);

		JSchema schema;

		try
		{
			schema = schemaToken.ToObject<JSchema>() ?? throw new JSchemaException("The token does not represent a schema object.");
		}
		catch (Exception exception)
		{
			return Failure($"The schema token could not be read as a JSON schema: {exception.Message}");
		}

		return Build(schema);
	}

	/// <summary>
	/// Builds a vocabulary index from schema text read through a reader, optionally using custom
	/// reader settings.
	/// </summary>
	/// <remarks>The method reads the entire reader and does not dispose it.</remarks>
	/// <param name="reader">The reader over the schema text.</param>
	/// <param name="settings">
	/// The schema reader settings, or <see langword="null"/> to use the defaults.
	/// </param>
	/// <returns>
	/// The built vocabulary index and any diagnostics, or a failed result when the schema text
	/// cannot be read or parsed.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="reader"/> is <see langword="null"/>.</exception>
	public JsonSchemaVocabularyIndexResult Build(TextReader reader, JSchemaReaderSettings? settings = null)
	{
		ArgumentNullException.ThrowIfNull(reader);

		string schemaJson;

		try
		{
			schemaJson = reader.ReadToEnd();
		}
		catch (Exception exception)
		{
			return Failure($"The schema text could not be read: {exception.Message}");
		}

		JSchema schema;

		try
		{
			schema = settings is null
				? JSchema.Parse(schemaJson)
				: JSchema.Parse(schemaJson, settings);
		}
		catch (Exception exception)
		{
			return Failure($"The schema could not be parsed: {exception.Message}");
		}

		return Build(schema);
	}

	/// <summary>
	/// Creates a failed result carrying a single diagnostic.
	/// </summary>
	private static JsonSchemaVocabularyIndexResult Failure(string diagnostic)
		=> new(null, [diagnostic]);

	private static void CollectReachable(JSchema schema, BuildContext context)
	{
		foreach (JSchema currentSchema in SchemaTraversal.FlattenSchemas(schema))
			Collect(currentSchema, context);
	}

	private static void Collect(JSchema schema, BuildContext context)
	{
		// Extract string values from const declarations at every visited schema.
		if (schema.Const is not null && schema.Const.Type == JTokenType.String)
			AddConstant(schema.Const.ToString(), context);

		// Extract string values from enum declarations at every visited schema.
		if (schema.Enum is not null)
		{
			foreach (JToken enumValue in schema.Enum)
			{
				if (enumValue.Type == JTokenType.String)
					AddConstant(enumValue.ToString(), context);
			}
		}

		if (schema.Properties is null)
			return;

		foreach (KeyValuePair<string, JSchema> property in schema.Properties)
			AddProperty(property.Key, property.Value, context);
	}

	private static void CollectDefinitionSections(IDictionary<string, JToken>? extensionData, BuildContext context)
	{
		if (extensionData is null)
			return;

		// Include both common definition section names. Walk them directly so definitions that
		// are not referenced from the root are included as well.
		string[] sectionNames = ["definitions", "$defs"];

		foreach (string sectionName in sectionNames)
		{
			if (!extensionData.TryGetValue(sectionName, out JToken? section) || section is not JObject definitions)
				continue;

			foreach (KeyValuePair<string, JToken?> definition in definitions)
			{
				JSchema? definitionSchema;

				try
				{
					definitionSchema = definition.Value?.ToObject<JSchema>();
				}
				catch (Exception exception)
				{
					context.Diagnostics.Add($"Definition '{definition.Key}' in '{sectionName}' could not be parsed; it was skipped: {exception.Message}");
					continue;
				}

				if (definitionSchema is null)
				{
					context.Diagnostics.Add($"Definition '{definition.Key}' in '{sectionName}' is not a schema object; it was skipped.");
					continue;
				}

				CollectReachable(definitionSchema, context);
			}
		}
	}

	private static void AddProperty(string name, JSchema propertySchema, BuildContext context)
	{
		// When a name occurs more than once, keep the first occurrence in traversal order.
		if (!context.PropertyNames.Add(name))
			return;

		context.Properties.Add(new JsonSchemaVocabularyPropertyDescriptor(
			name,
			ToPropertyTypes(propertySchema.Type),
			propertySchema.Description));
	}

	private static void AddConstant(string value, BuildContext context)
	{
		if (!context.ConstantNames.Add(value))
			return;

		context.Constants.Add(value);
	}

	private static List<JsonSchemaPropertyType> ToPropertyTypes(JSchemaType? type)
	{
		if (type is null)
			return [];

		// The types are emitted in the declaration order of JsonSchemaPropertyType so the list order
		// stays deterministic and matches the enum.
		var result = new List<JsonSchemaPropertyType>();

		if (type.Value.HasFlag(JSchemaType.Object))
			result.Add(JsonSchemaPropertyType.Object);

		if (type.Value.HasFlag(JSchemaType.Array))
			result.Add(JsonSchemaPropertyType.Array);

		if (type.Value.HasFlag(JSchemaType.String))
			result.Add(JsonSchemaPropertyType.String);

		if (type.Value.HasFlag(JSchemaType.Integer))
			result.Add(JsonSchemaPropertyType.Integer);

		if (type.Value.HasFlag(JSchemaType.Number))
			result.Add(JsonSchemaPropertyType.Number);

		if (type.Value.HasFlag(JSchemaType.Boolean))
			result.Add(JsonSchemaPropertyType.Boolean);

		if (type.Value.HasFlag(JSchemaType.Null))
			result.Add(JsonSchemaPropertyType.Null);

		return result;
	}

	/// <summary>
	/// Accumulates the state of one index build.
	/// </summary>
	private sealed class BuildContext
	{
		public List<JsonSchemaVocabularyPropertyDescriptor> Properties { get; } = [];

		public HashSet<string> PropertyNames { get; } = new(StringComparer.Ordinal);

		public List<string> Constants { get; } = [];

		public HashSet<string> ConstantNames { get; } = new(StringComparer.Ordinal);

		public List<string> Diagnostics { get; } = [];
	}
}

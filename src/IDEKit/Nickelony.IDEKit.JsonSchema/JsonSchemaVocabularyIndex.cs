namespace Nickelony.IDEKit.JsonSchema;

/// <summary>An immutable vocabulary extracted from a JSON Schema for completion, hover, and highlighting.</summary>
/// <remarks>
/// It contains property descriptors and string values collected from the root schema and supported nested schemas.
/// </remarks>
public sealed class JsonSchemaVocabularyIndex
{
	/// <summary>The property descriptors collected from the root and supported nested schemas.</summary>
	public IReadOnlyList<JsonSchemaVocabularyPropertyDescriptor> Properties { get; }

	/// <summary>The string values found in <c>const</c> and <c>enum</c> declarations.</summary>
	public IReadOnlyList<string> Constants { get; }

	/// <param name="properties">The schema-wide property vocabulary.</param>
	/// <param name="constants">The schema-derived constant string values.</param>
	/// <exception cref="ArgumentNullException"><paramref name="properties"/> or <paramref name="constants"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// The lists are copied in the order given, and duplicates are kept. <see cref="JsonSchemaVocabularyIndexBuilder"/> removes them.
	/// </remarks>
	public JsonSchemaVocabularyIndex(IReadOnlyList<JsonSchemaVocabularyPropertyDescriptor> properties, IReadOnlyList<string> constants)
	{
		ArgumentNullException.ThrowIfNull(properties);
		ArgumentNullException.ThrowIfNull(constants);

		// Keep read-only copies of the supplied lists.
		Properties = Array.AsReadOnly([.. properties]);
		Constants = Array.AsReadOnly([.. constants]);
	}
}

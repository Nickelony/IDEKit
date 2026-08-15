namespace Nickelony.IDEKit.JsonSchema;

/// <summary>
/// Immutable vocabulary extracted from a JSON Schema for completion, hover, and highlighting.
/// It contains property descriptors and distinct string values collected from the root schema and
/// supported nested schemas; the first occurrence of each name or value is retained.
/// </summary>
public sealed class JsonSchemaVocabularyIndex
{
	/// <summary>
	/// Gets the property descriptors collected from the root and supported nested schemas. Duplicate
	/// names are removed; each descriptor retains the first occurrence's types and description.
	/// </summary>
	public IReadOnlyList<JsonSchemaVocabularyPropertyDescriptor> Properties { get; }

	/// <summary>
	/// Gets the distinct string values found in <c>const</c> and <c>enum</c> declarations, in
	/// first-seen order.
	/// </summary>
	public IReadOnlyList<string> Constants { get; }

	/// <summary>
	/// Initializes a new instance of the <see cref="JsonSchemaVocabularyIndex"/> class.
	/// </summary>
	/// <param name="properties">The schema-wide property vocabulary.</param>
	/// <param name="constants">The schema-derived constant string values.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="properties"/> or <paramref name="constants"/> is <see langword="null"/>.
	/// </exception>
	public JsonSchemaVocabularyIndex(IReadOnlyList<JsonSchemaVocabularyPropertyDescriptor> properties, IReadOnlyList<string> constants)
	{
		ArgumentNullException.ThrowIfNull(properties);
		ArgumentNullException.ThrowIfNull(constants);

		// Keep read-only copies of the supplied lists.
		Properties = Array.AsReadOnly([.. properties]);
		Constants = Array.AsReadOnly([.. constants]);
	}
}

namespace Nickelony.IDEKit.JsonSchema;

/// <summary>An immutable description of a property in a JSON Schema vocabulary index.</summary>
public sealed class JsonSchemaVocabularyPropertyDescriptor
{
	public string Name { get; }

	/// <summary>The JSON types supplied for the property in the order given, or an empty list when none were supplied.</summary>
	/// <remarks>
	/// <see cref="JsonSchemaVocabularyIndexBuilder"/> supplies them in the declaration order of <see cref="JsonSchemaPropertyType"/>.
	/// </remarks>
	public IReadOnlyList<JsonSchemaPropertyType> Types { get; }

	/// <summary>The property description, or <see langword="null"/> when none was supplied.</summary>
	public string? Description { get; }

	/// <summary>Whether <see cref="Types"/> contains exactly one entry, the <see cref="JsonSchemaPropertyType.Array"/> type.</summary>
	/// <remarks>
	/// A union that also allows arrays (for example <c>["array", "null"]</c>) reports <see langword="false"/>.
	/// </remarks>
	public bool IsArrayOnly => Types.Count == 1 && Types[0] == JsonSchemaPropertyType.Array;

	/// <param name="name">The property name.</param>
	/// <param name="types">The declared JSON types.</param>
	/// <param name="description">The property description, which may be <see langword="null"/>.</param>
	/// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="types"/> is <see langword="null"/>.</exception>
	public JsonSchemaVocabularyPropertyDescriptor(string name, IReadOnlyList<JsonSchemaPropertyType> types, string? description)
	{
		ArgumentNullException.ThrowIfNull(name);
		ArgumentNullException.ThrowIfNull(types);

		Name = name;

		// Keep a read-only copy of the supplied types.
		Types = Array.AsReadOnly([.. types]);
		Description = description;
	}
}

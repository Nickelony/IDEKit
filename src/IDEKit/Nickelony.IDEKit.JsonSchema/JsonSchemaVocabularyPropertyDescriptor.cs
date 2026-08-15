namespace Nickelony.IDEKit.JsonSchema;

/// <summary>
/// Describes a property in a JSON Schema vocabulary index. The descriptor is immutable and
/// includes the property's name, JSON types, and description.
/// </summary>
public sealed class JsonSchemaVocabularyPropertyDescriptor
{
	/// <summary>
	/// Gets the property name.
	/// </summary>
	public string Name { get; }

	/// <summary>
	/// Gets the JSON types supplied for the property, or an empty list when none were supplied. The
	/// list follows the declaration order of <see cref="JsonSchemaPropertyType"/>.
	/// </summary>
	public IReadOnlyList<JsonSchemaPropertyType> Types { get; }

	/// <summary>
	/// Gets the property description, or <see langword="null"/> when none was supplied.
	/// </summary>
	public string? Description { get; }

	/// <summary>
	/// Gets a value indicating whether <see cref="Types"/> contains exactly one entry, the
	/// <see cref="JsonSchemaPropertyType.Array"/> type. A union that also allows arrays (for example
	/// <c>["array", "null"]</c>) reports <see langword="false"/>.
	/// </summary>
	public bool IsArrayOnly => Types.Count == 1 && Types[0] == JsonSchemaPropertyType.Array;

	/// <summary>
	/// Initializes a new instance of the <see cref="JsonSchemaVocabularyPropertyDescriptor"/> class.
	/// </summary>
	/// <param name="name">The property name.</param>
	/// <param name="types">The declared JSON types.</param>
	/// <param name="description">The property description, which may be <see langword="null"/>.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="name"/> or <paramref name="types"/> is <see langword="null"/>.
	/// </exception>
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

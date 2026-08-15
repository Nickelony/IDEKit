using System.Diagnostics.CodeAnalysis;

namespace Nickelony.IDEKit.JsonSchema;

/// <summary>
/// The JSON data types that a property may declare with the JSON Schema <c>type</c> keyword.
/// </summary>
public enum JsonSchemaPropertyType
{
	/// <summary>
	/// The JSON object type.
	/// </summary>
	[SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Public member names mirror JSON Schema type names.")]
	Object,

	/// <summary>
	/// The JSON array type.
	/// </summary>
	Array,

	/// <summary>
	/// The JSON string type.
	/// </summary>
	[SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Public member names mirror JSON Schema type names.")]
	String,

	/// <summary>
	/// The JSON integer type.
	/// </summary>
	[SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Public member names mirror JSON Schema type names.")]
	Integer,

	/// <summary>
	/// The JSON number type.
	/// </summary>
	Number,

	/// <summary>
	/// The JSON boolean type.
	/// </summary>
	Boolean,

	/// <summary>
	/// The JSON <see langword="null"/> type.
	/// </summary>
	Null
}

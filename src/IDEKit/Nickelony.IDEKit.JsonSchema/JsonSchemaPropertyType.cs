using System.Diagnostics.CodeAnalysis;

namespace Nickelony.IDEKit.JsonSchema;

/// <summary>The JSON data types that a property may declare with the JSON Schema <c>type</c> keyword.</summary>
public enum JsonSchemaPropertyType
{
	[SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Public member names mirror JSON Schema type names.")]
	Object,

	Array,

	[SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Public member names mirror JSON Schema type names.")]
	String,

	[SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Public member names mirror JSON Schema type names.")]
	Integer,

	Number,

	Boolean,

	Null,
}

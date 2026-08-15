using System.Diagnostics.CodeAnalysis;

namespace Nickelony.IDEKit.IntelliSense.SemanticTokens;

/// <summary>
/// Well-known semantic token types mirroring the LSP names.
/// </summary>
/// <remarks>
/// A token type is a semantic category, not a style: hosts map a type to their own theme styles.
/// The set mirrors the LSP 3.17 token type names plus the LSP 3.18 <see cref="Label"/> addition, so
/// language providers can round-trip tokens without translation; producers may use additional
/// custom type names.
/// </remarks>
public static class TextSemanticTokenTypes
{
	/// <summary>A namespace token.</summary>
	public const string Namespace = "namespace";

	/// <summary>A type token.</summary>
	public const string Type = "type";

	/// <summary>A class token.</summary>
	public const string Class = "class";

	/// <summary>An enum token.</summary>
	public const string Enum = "enum";

	/// <summary>An interface token.</summary>
	public const string Interface = "interface";

	/// <summary>A struct token.</summary>
	public const string Struct = "struct";

	/// <summary>A type parameter token.</summary>
	public const string TypeParameter = "typeParameter";

	/// <summary>A parameter token.</summary>
	public const string Parameter = "parameter";

	/// <summary>A variable token.</summary>
	public const string Variable = "variable";

	/// <summary>A property token.</summary>
	public const string Property = "property";

	/// <summary>An enum member token.</summary>
	public const string EnumMember = "enumMember";

	/// <summary>An event token.</summary>
	public const string Event = "event";

	/// <summary>A function token.</summary>
	public const string Function = "function";

	/// <summary>A method token.</summary>
	public const string Method = "method";

	/// <summary>A macro token.</summary>
	public const string Macro = "macro";

	/// <summary>A keyword token.</summary>
	public const string Keyword = "keyword";

	/// <summary>A modifier token.</summary>
	public const string Modifier = "modifier";

	/// <summary>A comment token.</summary>
	public const string Comment = "comment";

	/// <summary>A string token.</summary>
	[SuppressMessage(
		"Design",
		"CA1720:Identifier contains type name",
		Justification = "The member name mirrors the LSP semantic token contract.")]
	public const string String = "string";

	/// <summary>A number token.</summary>
	public const string Number = "number";

	/// <summary>A regular-expression token.</summary>
	public const string Regexp = "regexp";

	/// <summary>An operator token.</summary>
	public const string Operator = "operator";

	/// <summary>A decorator token.</summary>
	public const string Decorator = "decorator";

	/// <summary>A label token (LSP 3.18).</summary>
	public const string Label = "label";
}

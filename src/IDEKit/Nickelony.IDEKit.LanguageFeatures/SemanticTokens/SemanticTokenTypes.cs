using System.Diagnostics.CodeAnalysis;

namespace Nickelony.IDEKit.LanguageFeatures.SemanticTokens;

/// <summary>Well-known semantic token types mirroring the LSP names.</summary>
/// <remarks>
/// A type is a semantic category, not a style: hosts map it to their own theme styles. Producers may supply additional custom type names.
/// </remarks>
public static class SemanticTokenTypes
{
	public const string Namespace = "namespace";

	public const string Type = "type";

	public const string Class = "class";

	public const string Enum = "enum";

	public const string Interface = "interface";

	public const string Struct = "struct";

	public const string TypeParameter = "typeParameter";

	public const string Parameter = "parameter";

	public const string Variable = "variable";

	public const string Property = "property";

	public const string EnumMember = "enumMember";

	public const string Event = "event";

	public const string Function = "function";

	public const string Method = "method";

	public const string Macro = "macro";

	public const string Keyword = "keyword";

	public const string Modifier = "modifier";

	public const string Comment = "comment";

	[SuppressMessage("Design", "CA1720:Identifier contains type name", Justification = "The member name mirrors the LSP semantic token contract.")]
	public const string String = "string";

	public const string Number = "number";

	public const string Regexp = "regexp";

	public const string Operator = "operator";

	public const string Decorator = "decorator";

	public const string Label = "label";
}

namespace Nickelony.IDEKit.LanguageFeatures.SemanticTokens;

/// <summary>Well-known semantic token modifiers mirroring the LSP names.</summary>
/// <remarks>
/// A modifier is an optional qualifier attached to a <see cref="SemanticToken"/>. Hosts and language packages may declare additional custom
/// modifier names.
/// </remarks>
public static class SemanticTokenModifiers
{
	public const string Declaration = "declaration";

	public const string Definition = "definition";

	public const string Readonly = "readonly";

	public const string Static = "static";

	public const string Deprecated = "deprecated";

	public const string Abstract = "abstract";

	public const string Async = "async";

	public const string Modification = "modification";

	public const string Documentation = "documentation";

	public const string DefaultLibrary = "defaultLibrary";
}

namespace Nickelony.IDEKit.IntelliSense.SemanticTokens;

/// <summary>
/// Well-known semantic token modifiers mirroring the LSP names.
/// </summary>
/// <remarks>
/// A modifier is an optional qualifier attached to a <see cref="TextSemanticToken"/>, such as
/// <see cref="Deprecated"/> or <see cref="Declaration"/>. The names mirror the LSP modifier names;
/// hosts and language packages may declare additional custom modifier names.
/// </remarks>
public static class TextSemanticTokenModifiers
{
	/// <summary>The token declares a symbol.</summary>
	public const string Declaration = "declaration";

	/// <summary>The token defines a symbol.</summary>
	public const string Definition = "definition";

	/// <summary>The token is read-only.</summary>
	public const string Readonly = "readonly";

	/// <summary>The token is static.</summary>
	public const string Static = "static";

	/// <summary>The token is deprecated.</summary>
	public const string Deprecated = "deprecated";

	/// <summary>The token is abstract.</summary>
	public const string Abstract = "abstract";

	/// <summary>The token is asynchronous.</summary>
	public const string Async = "async";

	/// <summary>The token is a modification.</summary>
	public const string Modification = "modification";

	/// <summary>The token is documentation.</summary>
	public const string Documentation = "documentation";

	/// <summary>The token comes from a default library.</summary>
	public const string DefaultLibrary = "defaultLibrary";
}

using Nickelony.IDEKit.LanguageFeatures.DocumentSymbols;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.DocumentSymbols;

/// <summary>Maps a symbol kind to the icon key that the symbol modes of the quick pick put on their items.</summary>
public static class DocumentSymbolKindExtensions
{
	/// <summary>Gets the icon key of a symbol kind, which a host maps to an image.</summary>
	/// <param name="kind">The kind of a document or workspace symbol.</param>
	/// <returns>
	/// The key, which is the codicon identifier that VS Code uses for the kind, such as <c>symbol-method</c>. A host maps the keys it draws and falls
	/// back to one image for the rest.
	/// </returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not a defined <see cref="DocumentSymbolKind"/> value.</exception>
	public static string ToIconKey(this DocumentSymbolKind kind)
	{
		return kind switch
		{
			DocumentSymbolKind.File => "symbol-file",
			DocumentSymbolKind.Module => "symbol-module",
			DocumentSymbolKind.Namespace => "symbol-namespace",
			DocumentSymbolKind.Package => "symbol-package",
			DocumentSymbolKind.Class => "symbol-class",
			DocumentSymbolKind.Method => "symbol-method",
			DocumentSymbolKind.Property => "symbol-property",
			DocumentSymbolKind.Field => "symbol-field",
			DocumentSymbolKind.Constructor => "symbol-constructor",
			DocumentSymbolKind.Enum => "symbol-enum",
			DocumentSymbolKind.Interface => "symbol-interface",
			DocumentSymbolKind.Function => "symbol-function",
			DocumentSymbolKind.Variable => "symbol-variable",
			DocumentSymbolKind.Constant => "symbol-constant",
			DocumentSymbolKind.String => "symbol-string",
			DocumentSymbolKind.Number => "symbol-number",
			DocumentSymbolKind.Boolean => "symbol-boolean",
			DocumentSymbolKind.Array => "symbol-array",
			DocumentSymbolKind.Object => "symbol-object",
			DocumentSymbolKind.Key => "symbol-key",
			DocumentSymbolKind.Null => "symbol-null",
			DocumentSymbolKind.EnumMember => "symbol-enum-member",
			DocumentSymbolKind.Struct => "symbol-struct",
			DocumentSymbolKind.Event => "symbol-event",
			DocumentSymbolKind.Operator => "symbol-operator",
			DocumentSymbolKind.TypeParameter => "symbol-type-parameter",
			_ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "The kind is not a defined symbol kind."),
		};
	}
}

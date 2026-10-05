using Nickelony.IDEKit.LanguageFeatures.DocumentSymbols;
using Lsp = Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider;

/// <summary>Maps the protocol symbol kind onto the shared <see cref="DocumentSymbolKind"/> vocabulary.</summary>
/// <remarks>
/// The mapping is explicit instead of a cast, so a value that a newer server sends and the protocol version of this package does not define
/// cannot leak an undefined member. The protocol defines no default symbol kind, so a caller chooses its own fallback instead of receiving
/// one silently.
/// </remarks>
internal static class DocumentSymbolKindConversion
{
	/// <summary>Attempts to resolve a protocol symbol kind to the corresponding member.</summary>
	/// <param name="kind">The protocol symbol kind.</param>
	/// <param name="mapped">Receives the matching member when the method returns <see langword="true"/>; otherwise, the default value.</param>
	/// <returns><see langword="true"/> when <paramref name="kind"/> is a defined protocol symbol kind; otherwise, <see langword="false"/>.</returns>
	internal static bool TryFromProtocol(Lsp.SymbolKind kind, out DocumentSymbolKind mapped)
	{
		DocumentSymbolKind? result = kind switch
		{
			Lsp.SymbolKind.File => DocumentSymbolKind.File,
			Lsp.SymbolKind.Module => DocumentSymbolKind.Module,
			Lsp.SymbolKind.Namespace => DocumentSymbolKind.Namespace,
			Lsp.SymbolKind.Package => DocumentSymbolKind.Package,
			Lsp.SymbolKind.Class => DocumentSymbolKind.Class,
			Lsp.SymbolKind.Method => DocumentSymbolKind.Method,
			Lsp.SymbolKind.Property => DocumentSymbolKind.Property,
			Lsp.SymbolKind.Field => DocumentSymbolKind.Field,
			Lsp.SymbolKind.Constructor => DocumentSymbolKind.Constructor,
			Lsp.SymbolKind.Enum => DocumentSymbolKind.Enum,
			Lsp.SymbolKind.Interface => DocumentSymbolKind.Interface,
			Lsp.SymbolKind.Function => DocumentSymbolKind.Function,
			Lsp.SymbolKind.Variable => DocumentSymbolKind.Variable,
			Lsp.SymbolKind.Constant => DocumentSymbolKind.Constant,
			Lsp.SymbolKind.String => DocumentSymbolKind.String,
			Lsp.SymbolKind.Number => DocumentSymbolKind.Number,
			Lsp.SymbolKind.Boolean => DocumentSymbolKind.Boolean,
			Lsp.SymbolKind.Array => DocumentSymbolKind.Array,
			Lsp.SymbolKind.Object => DocumentSymbolKind.Object,
			Lsp.SymbolKind.Key => DocumentSymbolKind.Key,
			Lsp.SymbolKind.Null => DocumentSymbolKind.Null,
			Lsp.SymbolKind.EnumMember => DocumentSymbolKind.EnumMember,
			Lsp.SymbolKind.Struct => DocumentSymbolKind.Struct,
			Lsp.SymbolKind.Event => DocumentSymbolKind.Event,
			Lsp.SymbolKind.Operator => DocumentSymbolKind.Operator,
			Lsp.SymbolKind.TypeParameter => DocumentSymbolKind.TypeParameter,
			_ => null,
		};

		mapped = result.GetValueOrDefault();
		return result.HasValue;
	}
}

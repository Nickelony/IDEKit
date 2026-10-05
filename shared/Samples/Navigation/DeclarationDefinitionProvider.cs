using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Navigation;

namespace Nickelony.Samples;

/// <summary>
/// Answers a definition request with the first declaration of the name at the request offset: a function, a parameter or a variable.
/// </summary>
internal sealed class DeclarationDefinitionProvider : IDefinitionProvider
{
	public Task<DefinitionLocation?> GetDefinitionAsync(DefinitionRequest request, CancellationToken cancellationToken = default)
	{
		string text = request.DocumentText;

		// The offset names the symbol where the request carries one, as a language server needs; the name is the fallback.
		string name =
			request.SymbolOffset is int offset && IdentifierScanner.FindName(text, offset) is { } token ? token.GetText(text) : request.SymbolName;

		if (name.Length == 0 || DeclarationScanner.FindDeclaration(text, name) is not { } declaration)
			return Task.FromResult<DefinitionLocation?>(null);

		var lineMap = TextLineMap.Build(text);
		var range = new TextPositionRange(lineMap.GetPosition(declaration.Offset), lineMap.GetPosition(declaration.EndOffset));

		// A location without a document identifier lies in the document of the request.
		return Task.FromResult<DefinitionLocation?>(new DefinitionLocation(range));
	}
}

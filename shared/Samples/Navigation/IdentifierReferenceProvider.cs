using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Navigation;

namespace Nickelony.Samples;

/// <summary>Answers a reference request with every whole-name occurrence of the name at the request offset in the document of the request.</summary>
internal sealed class IdentifierReferenceProvider : IReferenceProvider
{
	public Task<IReadOnlyList<ReferenceLocation>> GetReferencesAsync(ReferenceRequest request, CancellationToken cancellationToken = default)
	{
		string text = request.DocumentText;

		if (IdentifierScanner.FindName(text, request.SymbolOffset) is not { } token)
			return Task.FromResult<IReadOnlyList<ReferenceLocation>>([]);

		string name = token.GetText(text);
		TextRange? declaration = request.IncludeDeclaration ? null : DeclarationScanner.FindDeclaration(text, name);

		var lineMap = TextLineMap.Build(text);

		List<ReferenceLocation> references =
		[
			.. IdentifierScanner
				.FindOccurrences(text, name)
				.Where(occurrence => occurrence != declaration)
				.Select(occurrence => new ReferenceLocation(
					request.DocumentId,
					new TextPositionRange(lineMap.GetPosition(occurrence.Offset), lineMap.GetPosition(occurrence.EndOffset))
				)),
		];

		return Task.FromResult<IReadOnlyList<ReferenceLocation>>(references);
	}
}

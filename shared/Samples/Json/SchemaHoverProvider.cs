using Nickelony.IDEKit.Core.Identifiers;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.JsonSchema;
using Nickelony.IDEKit.LanguageFeatures.Hover;

namespace Nickelony.Samples;

/// <summary>Answers a hover over a property name with the description that the JSON Schema declares for it.</summary>
internal sealed class SchemaHoverProvider : IHoverProvider
{
	private readonly JsonSchemaVocabularyIndex _index;

	/// <param name="index">The vocabulary whose descriptions the hover shows.</param>
	internal SchemaHoverProvider(JsonSchemaVocabularyIndex index) => _index = index;

	public Task<HoverInformation?> GetHoverAsync(HoverRequest request, CancellationToken cancellationToken = default)
	{
		var snapshot = new StringTextSnapshot(request.DocumentText);
		TextRange? token = IdentifierOperations.FindTokenSpan(snapshot, request.HoveredOffset, IdentifierCharacterPolicy.Default);

		if (token is null)
			return Task.FromResult<HoverInformation?>(null);

		string name = token.Value.GetText(request.DocumentText);
		JsonSchemaVocabularyPropertyDescriptor? property = _index.Properties.FirstOrDefault(candidate => candidate.Name == name);

		return Task.FromResult(
			property?.Description is { } description ? new HoverInformation(description) { Range = token, SymbolName = name } : null
		);
	}
}

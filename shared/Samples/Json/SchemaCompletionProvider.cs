using Nickelony.IDEKit.JsonSchema;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Markup;

namespace Nickelony.Samples;

/// <summary>Offers the property names and the enumerated values of a JSON Schema vocabulary as completion items.</summary>
internal sealed class SchemaCompletionProvider : ICompletionProvider
{
	private readonly JsonSchemaVocabularyIndex _index;

	/// <param name="index">The vocabulary the items come from.</param>
	internal SchemaCompletionProvider(JsonSchemaVocabularyIndex index) => _index = index;

	public Task<IReadOnlyList<CompletionItem>> GetCompletionItemsAsync(CompletionRequest request, CancellationToken cancellationToken = default)
	{
		List<CompletionItem> items = [];

		foreach (JsonSchemaVocabularyPropertyDescriptor property in _index.Properties)
		{
			items.Add(
				new CompletionItem(property.Name)
				{
					Kind = CompletionItemKind.Property,
					Detail = string.Join(" | ", property.Types.Select(type => type.ToString().ToLowerInvariant())),
					Documentation = property.Description,
					InsertText = $"\"{property.Name}\": ",
				}
			);
		}

		foreach (string constant in _index.Constants)
			items.Add(new CompletionItem(constant) { Kind = CompletionItemKind.EnumMember, InsertText = $"\"{constant}\"" });

		return Task.FromResult<IReadOnlyList<CompletionItem>>(items);
	}
}

using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Markup;

namespace Nickelony.Samples;

/// <summary>Offers the snippets, the keywords, the library functions and the words already in the document as completion items.</summary>
internal sealed class KeywordCompletionProvider : ICompletionProvider, ICompletionTriggerCharacters
{
	// A dot completes a member, such as a function of the string library.
	public IReadOnlyList<string> TriggerCharacters { get; } = ["."];

	// The editor binding inserts a snippet when the item commits: the first placeholder is selected, Tab steps to the next, and Enter ends at $0.
	private static readonly CompletionItem[] s_snippets =
	[
		Snippet("function", "function ${1:name}(${2:arguments})\n    $0\nend", "A function declaration"),
		Snippet("for", "for ${1:index} = ${2:1}, ${3:10} do\n    $0\nend", "A numeric for loop"),
		Snippet("if", "if ${1:condition} then\n    $0\nend", "An if statement"),
	];

	public Task<IReadOnlyList<CompletionItem>> GetCompletionItemsAsync(CompletionRequest request, CancellationToken cancellationToken = default)
	{
		List<CompletionItem> items = [.. s_snippets];

		foreach (string keyword in SampleKeywords.Keywords)
			items.Add(new CompletionItem(keyword) { Kind = CompletionItemKind.Keyword, Detail = "keyword" });

		foreach (SampleFunction function in SampleKeywords.Functions.Values)
		{
			items.Add(
				new CompletionItem(function.Name)
				{
					Kind = CompletionItemKind.Function,
					Detail = "function",

					Documentation = SampleKeywords.Describe(function.Name) is { } description
						? new MarkupContent(description, MarkupKind.Markdown)
						: null,

					InsertText = function.Name + "()",
				}
			);
		}

		HashSet<string> known = [.. items.Select(item => item.Label)];

		foreach (string word in IdentifierScanner.FindIdentifiers(request.DocumentText))
		{
			if (known.Add(word))
				items.Add(new CompletionItem(word) { Kind = CompletionItemKind.Text, Detail = "in this document" });
		}

		return Task.FromResult<IReadOnlyList<CompletionItem>>(items);
	}

	private static CompletionItem Snippet(string label, string body, string detail)
	{
		return new CompletionItem(label)
		{
			Kind = CompletionItemKind.Snippet,
			Detail = detail,
			InsertText = body,
			InsertTextFormat = CompletionInsertTextFormat.Snippet,
			FilterText = label,
		};
	}
}

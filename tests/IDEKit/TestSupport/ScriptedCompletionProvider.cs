using Nickelony.IDEKit.LanguageFeatures.Completion;

namespace Nickelony.IDEKit.Testing;

/// <summary>A completion provider that answers each request through its script, records the requests, and names trigger characters.</summary>
internal sealed class ScriptedCompletionProvider : ICompletionProvider, ICompletionTriggerCharacters
{
	private readonly List<CompletionRequest> _requests = [];

	/// <summary>Answers a request; the default answers no items.</summary>
	internal Func<CompletionRequest, CancellationToken, Task<IReadOnlyList<CompletionItem>>> Answer { get; set; } =
		static (_, _) => Task.FromResult<IReadOnlyList<CompletionItem>>([]);

	public IReadOnlyList<string> TriggerCharacters { get; set; } = [];

	/// <summary>The requests in the order they arrived.</summary>
	internal IReadOnlyList<CompletionRequest> Requests => _requests;

	/// <summary>Creates a provider that answers every request with items of the given labels.</summary>
	/// <param name="labels">The labels of the items.</param>
	internal static ScriptedCompletionProvider Answering(params string[] labels) =>
		new() { Answer = (_, _) => Task.FromResult<IReadOnlyList<CompletionItem>>([.. labels.Select(label => new CompletionItem(label))]) };

	public Task<IReadOnlyList<CompletionItem>> GetCompletionItemsAsync(CompletionRequest request, CancellationToken cancellationToken = default)
	{
		_requests.Add(request);
		return Answer(request, cancellationToken);
	}
}

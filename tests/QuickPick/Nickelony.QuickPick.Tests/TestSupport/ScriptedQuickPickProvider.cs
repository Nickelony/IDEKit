namespace Nickelony.QuickPick.Tests;

/// <summary>
/// A quick-pick provider whose answers a test scripts, and which records every query, accept and preview call in the order the controller made them.
/// </summary>
/// <remarks>
/// By default a query answers <see cref="Items"/> at once. A test replaces <see cref="Answer"/> to hold an answer
/// back or to fail it, and sets a fault to make a member that describes the mode throw.
/// </remarks>
internal sealed class ScriptedQuickPickProvider(string prefix = "", string name = "Scripted") : IQuickPickProvider
{
	private QuickPickFilterMode _filterMode = QuickPickFilterMode.Fuzzy;
	private QuickPickGroup? _recentGroup;
	private string _emptyText = "No matching results";

	/// <summary>The items the default answer returns.</summary>
	public List<QuickPickItem> Items { get; } = [];

	/// <summary>The requests the controller made, in order.</summary>
	public List<QuickPickRequest> Requests { get; } = [];

	/// <summary>The cancellation tokens of the requests, in order.</summary>
	public List<CancellationToken> Tokens { get; } = [];

	/// <summary>The managed thread identifiers the requests were started on, in order.</summary>
	public List<int> RequestThreadIds { get; } = [];

	/// <summary>The items the controller asked the provider to accept, in order.</summary>
	public List<QuickPickItem> Accepted { get; } = [];

	/// <summary>The managed thread identifiers the accepts were started on, in order.</summary>
	public List<int> AcceptThreadIds { get; } = [];

	/// <summary>The text typed at each accept, in order.</summary>
	public List<string> AcceptedQueries { get; } = [];

	/// <summary>Gets the part of the query that the controller filters by; defaults to the whole query.</summary>
	public Func<string, string>? FilterText { get; set; }

	/// <summary>The cancellation tokens of the accepts, in order.</summary>
	public List<CancellationToken> AcceptTokens { get; } = [];

	/// <summary>The preview calls in order: <c>preview:</c> and the key of an item, or <c>cancel</c>.</summary>
	public List<string> PreviewLog { get; } = [];

	/// <summary>The managed thread identifiers the preview calls ran on, in the order of <see cref="PreviewLog"/>.</summary>
	public List<int> PreviewThreadIds { get; } = [];

	/// <summary>The calls of <see cref="OnDeactivated"/> in order, each with the number of accepts the provider had received by then.</summary>
	public List<int> DeactivationLog { get; } = [];

	/// <summary>The managed thread identifiers the calls of <see cref="OnDeactivated"/> ran on, in order.</summary>
	public List<int> DeactivationThreadIds { get; } = [];

	/// <summary>The exception <see cref="OnDeactivated"/> throws, or <see langword="null"/> to return normally.</summary>
	public Exception? DeactivationFault { get; set; }

	/// <summary>The rest before typed text starts a query, or <see langword="null"/> for the delay of the controller.</summary>
	public TimeSpan? QueryDelay { get; set; }

	/// <summary>The exception <see cref="QueryDebounceDelay"/> throws, or <see langword="null"/> to answer normally.</summary>
	public Exception? QueryDelayFault { get; set; }

	public TimeSpan? QueryDebounceDelay => QueryDelayFault is null ? QueryDelay : throw QueryDelayFault;

	/// <summary>Computes the answer to a request; defaults to <see cref="Items"/>.</summary>
	public Func<QuickPickRequest, CancellationToken, Task<IReadOnlyList<QuickPickItem>>>? Answer { get; set; }

	/// <summary>Runs when the controller accepts an item, after the item is recorded; defaults to completing at once.</summary>
	public Func<QuickPickItem, Task>? OnAccept { get; set; }

	/// <summary>The exception <see cref="Placeholder"/> throws, or <see langword="null"/> to answer normally.</summary>
	public Exception? PlaceholderFault { get; set; }

	/// <summary>The exception <see cref="FilterMode"/> throws, or <see langword="null"/> to answer normally.</summary>
	public Exception? FilterModeFault { get; set; }

	/// <summary>The exception <see cref="RecentGroup"/> throws, or <see langword="null"/> to answer normally.</summary>
	public Exception? RecentGroupFault { get; set; }

	/// <summary>The exception <see cref="EmptyText"/> throws, or <see langword="null"/> to answer normally.</summary>
	public Exception? EmptyTextFault { get; set; }

	public string Prefix { get; } = prefix;

	public string Name { get; } = name;

	public string Placeholder => PlaceholderFault is null ? $"Placeholder of {Name}" : throw PlaceholderFault;

	public QuickPickFilterMode FilterMode
	{
		get => FilterModeFault is null ? _filterMode : throw FilterModeFault;
		set => _filterMode = value;
	}

	public string EmptyText
	{
		get => EmptyTextFault is null ? _emptyText : throw EmptyTextFault;
		set => _emptyText = value;
	}

	public QuickPickGroup? RecentGroup
	{
		get => RecentGroupFault is null ? _recentGroup : throw RecentGroupFault;
		set => _recentGroup = value;
	}

	public Task<IReadOnlyList<QuickPickItem>> GetItemsAsync(QuickPickRequest request, CancellationToken cancellationToken = default)
	{
		Requests.Add(request);
		Tokens.Add(cancellationToken);
		RequestThreadIds.Add(Environment.CurrentManagedThreadId);

		return Answer is null ? Task.FromResult<IReadOnlyList<QuickPickItem>>([.. Items]) : Answer(request, cancellationToken);
	}

	public string GetFilterText(string query) => FilterText?.Invoke(query) ?? query;

	public Task AcceptAsync(QuickPickAcceptRequest request, CancellationToken cancellationToken = default)
	{
		Accepted.Add(request.Item);
		AcceptThreadIds.Add(Environment.CurrentManagedThreadId);
		AcceptedQueries.Add(request.Query);
		AcceptTokens.Add(cancellationToken);

		return OnAccept?.Invoke(request.Item) ?? Task.CompletedTask;
	}

	public void Preview(QuickPickItem item)
	{
		PreviewLog.Add($"preview:{item.Key}");
		PreviewThreadIds.Add(Environment.CurrentManagedThreadId);
	}

	public void CancelPreview()
	{
		PreviewLog.Add("cancel");
		PreviewThreadIds.Add(Environment.CurrentManagedThreadId);
	}

	public void OnDeactivated()
	{
		DeactivationLog.Add(Accepted.Count);
		DeactivationThreadIds.Add(Environment.CurrentManagedThreadId);

		if (DeactivationFault is { } fault)
			throw fault;
	}

	/// <summary>Creates an item with the key as its label.</summary>
	public static QuickPickItem Item(string key, QuickPickGroup? group = null, bool isEnabled = true) =>
		new(key, key) { Group = group, IsEnabled = isEnabled };
}

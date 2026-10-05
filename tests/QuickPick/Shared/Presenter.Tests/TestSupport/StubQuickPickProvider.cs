#if AVALONIAQUICKPICK

namespace Nickelony.QuickPick.Avalonia.Tests;

#else

namespace Nickelony.QuickPick.Wpf.Tests;

#endif

/// <summary>
/// A quick-pick provider that answers a fixed list of items and records the preview and accept calls in the order the controller made them.
/// </summary>
internal sealed class StubQuickPickProvider(string prefix = "") : IQuickPickProvider
{
	/// <summary>The items every query answers; the controller filters them.</summary>
	public List<QuickPickItem> Items { get; } = [];

	/// <summary>The calls in order: <c>preview:</c>, <c>accept:</c> and the key of an item, or <c>cancel</c>.</summary>
	public List<string> Log { get; } = [];

	/// <summary>The queries the controller asked, in order.</summary>
	public List<string> Queries { get; } = [];

	/// <summary>Computes the answer to a request; defaults to <see cref="Items"/>.</summary>
	public Func<QuickPickRequest, CancellationToken, Task<IReadOnlyList<QuickPickItem>>>? Answer { get; set; }

	public string Prefix { get; } = prefix;

	public string Name => "Stub";

	public string Placeholder => "Type to filter";

	public Task<IReadOnlyList<QuickPickItem>> GetItemsAsync(QuickPickRequest request, CancellationToken cancellationToken = default)
	{
		Queries.Add(request.Query);
		return Answer is null ? Task.FromResult<IReadOnlyList<QuickPickItem>>([.. Items]) : Answer(request, cancellationToken);
	}

	public Task AcceptAsync(QuickPickAcceptRequest request, CancellationToken cancellationToken = default)
	{
		Log.Add($"accept:{request.Item.Key}");
		return Task.CompletedTask;
	}

	public void Preview(QuickPickItem item) => Log.Add($"preview:{item.Key}");

	public void CancelPreview() => Log.Add("cancel");

	/// <summary>Creates an item whose label is its key.</summary>
	public static QuickPickItem Item(string key, QuickPickGroup? group = null, bool isEnabled = true) =>
		new(key, key) { Group = group, IsEnabled = isEnabled };
}

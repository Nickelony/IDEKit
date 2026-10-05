namespace Nickelony.QuickPick.CommandsOnly.Tests;

/// <summary>
/// The only mode of a command palette: it lists fixed command names under the empty prefix and records the command that the user accepts.
/// </summary>
/// <param name="names">The names of the commands, which the controller filters by the typed text.</param>
internal sealed class RecordingCommandProvider(params string[] names) : IQuickPickProvider
{
	/// <summary>The commands the controller accepted, in order.</summary>
	public List<string> Executed { get; } = [];

	public string Prefix => string.Empty;

	public string Name => "Commands";

	public string Placeholder => "Type a command";

	public Task<IReadOnlyList<QuickPickItem>> GetItemsAsync(QuickPickRequest request, CancellationToken cancellationToken = default) =>
		Task.FromResult<IReadOnlyList<QuickPickItem>>([.. names.Select(name => new QuickPickItem(name, name))]);

	public Task AcceptAsync(QuickPickAcceptRequest request, CancellationToken cancellationToken = default)
	{
		Executed.Add(request.Item.Key);
		return Task.CompletedTask;
	}
}

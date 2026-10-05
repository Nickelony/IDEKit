using Nickelony.KeyBindings;

namespace Nickelony.QuickPick.CommandsOnly.Tests;

/// <summary>The command mode of the command palette recipe in the quick pick guide, over a <see cref="CommandCatalog{TCommandId}"/>.</summary>
/// <typeparam name="TCommand">The command identity type.</typeparam>
/// <param name="catalog">The commands the mode lists.</param>
/// <param name="service">The key bindings, which supply the shortcut text of each command.</param>
/// <param name="canRunNow">The host's predicate for the commands that apply now.</param>
/// <param name="run">The host's way to run a command.</param>
internal sealed class FakeCommandQuickPickProvider<TCommand>(
	CommandCatalog<TCommand> catalog,
	IKeyBindingService<TCommand> service,
	Func<TCommand, bool> canRunNow,
	Action<TCommand> run
) : IQuickPickProvider
	where TCommand : notnull
{
	public string Prefix => ">";

	public string Name => "Commands";

	public string Placeholder => "Type a command";

	public QuickPickFilterMode FilterMode => QuickPickFilterMode.Words;

	public string EmptyText => "No matching commands";

	public QuickPickGroup? RecentGroup => new("recent", "recently used");

	public Task<IReadOnlyList<QuickPickItem>> GetItemsAsync(QuickPickRequest request, CancellationToken cancellationToken = default)
	{
		var otherCommands = new QuickPickGroup("other", "other commands");

		IEnumerable<QuickPickItem> items = catalog.Descriptors.Select(descriptor => new QuickPickItem(
			descriptor.SerializedId,
			descriptor.Title ?? descriptor.SerializedId
		)
		{
			Description = descriptor.Category,
			TrailingText = service.GetDisplayText(descriptor.Command),
			IsEnabled = canRunNow(descriptor.Command),
			Group = otherCommands,
			Tag = descriptor.Command,
		});

		return Task.FromResult<IReadOnlyList<QuickPickItem>>([.. items.OrderBy(item => item.Label, StringComparer.OrdinalIgnoreCase)]);
	}

	public Task AcceptAsync(QuickPickAcceptRequest request, CancellationToken cancellationToken = default)
	{
		if (request.Item.Tag is TCommand command)
			run(command);

		return Task.CompletedTask;
	}
}

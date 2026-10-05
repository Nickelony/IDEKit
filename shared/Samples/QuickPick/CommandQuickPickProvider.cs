using Nickelony.QuickPick;

namespace Nickelony.Samples;

/// <summary>Lists the commands of the sample in the command palette, and runs the one the user accepts.</summary>
/// <remarks>
/// The catalog of the key bindings holds the title, the category and the shortcut of each command, so the provider only maps
/// them to items. An item carries its command in <see cref="QuickPickItem.Tag"/>, so accepting it needs no lookup from a key. The provider returns
/// the commands in the order of their titles and filters them as the command palette of VS Code does, so the recently used commands come first
/// and the others follow by title.
/// </remarks>
internal sealed class CommandQuickPickProvider : IQuickPickProvider
{
	/// <summary>The text that picks the command mode.</summary>
	internal const string ModePrefix = ">";

	private static readonly QuickPickGroup s_otherCommands = new("other", "other commands");

	private readonly SampleKeyBindings _keyBindings;
	private readonly Func<string, bool> _canRunNow;
	private readonly Action<string> _run;

	/// <param name="keyBindings">Holds the commands with their titles, categories and shortcuts.</param>
	/// <param name="canRunNow">Tells whether a command applies to the current state of the window. It runs on the dispatcher thread.</param>
	/// <param name="run">Runs a command through the same path as its menu item and its shortcut. It runs on the dispatcher thread.</param>
	internal CommandQuickPickProvider(SampleKeyBindings keyBindings, Func<string, bool> canRunNow, Action<string> run)
	{
		_keyBindings = keyBindings;
		_canRunNow = canRunNow;
		_run = run;
	}

	public string Prefix => ModePrefix;

	public string Name => "Commands";

	public string Placeholder => "Type the name of a command to run";

	public QuickPickFilterMode FilterMode => QuickPickFilterMode.Words;

	public string EmptyText => "No matching commands";

	public QuickPickGroup? RecentGroup { get; } = new("recent", "recently used");

	public Task<IReadOnlyList<QuickPickItem>> GetItemsAsync(QuickPickRequest request, CancellationToken cancellationToken = default)
	{
		IEnumerable<QuickPickItem> items = _keyBindings.Descriptors.Select(descriptor => new QuickPickItem(
			descriptor.SerializedId,
			descriptor.Title ?? descriptor.SerializedId
		)
		{
			Description = descriptor.Category,
			IconKey = descriptor.Category,
			TrailingText = _keyBindings.GetDisplayText(descriptor.Command),
			IsEnabled = _canRunNow(descriptor.Command),
			Group = s_otherCommands,
			Tag = descriptor.Command,
		});

		return Task.FromResult<IReadOnlyList<QuickPickItem>>([.. items.OrderBy(item => item.Label, StringComparer.OrdinalIgnoreCase)]);
	}

	public Task AcceptAsync(QuickPickAcceptRequest request, CancellationToken cancellationToken = default)
	{
		if (request.Item.Tag is string command)
			_run(command);

		return Task.CompletedTask;
	}
}

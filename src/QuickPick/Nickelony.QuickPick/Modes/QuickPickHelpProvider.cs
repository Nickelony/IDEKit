namespace Nickelony.QuickPick;

/// <summary>Lists the modes of a quick pick, which the user reaches by typing the reserved prefix <c>?</c>.</summary>
/// <remarks>
/// Each row names a mode and carries its prefix as the trailing text. The controller handles an accepted row itself, by typing the prefix of the mode
/// into the input, so the provider has nothing to accept.
/// </remarks>
internal sealed class QuickPickHelpProvider : IQuickPickProvider
{
	/// <summary>The prefix that selects the list of modes, which no mode may use while the help mode is on.</summary>
	internal const string ReservedPrefix = "?";

	private readonly IReadOnlyList<(string Prefix, IQuickPickProvider Provider)> _modes;
	private readonly QuickPickControllerOptions _options;

	/// <param name="modes">The registered providers with the prefixes they were offered under, whose modes the list shows.</param>
	/// <param name="options">The options of the controller, which supply the placeholder and the empty text of the list.</param>
	internal QuickPickHelpProvider(IReadOnlyList<(string Prefix, IQuickPickProvider Provider)> modes, QuickPickControllerOptions options)
	{
		_modes = modes;
		_options = options;
	}

	public string Prefix => ReservedPrefix;

	public string Name => "Modes";

	public string Placeholder => _options.HelpModePlaceholder;

	public string EmptyText => _options.HelpModeEmptyText;

	public Task<IReadOnlyList<QuickPickItem>> GetItemsAsync(QuickPickRequest request, CancellationToken cancellationToken = default)
	{
		List<QuickPickItem> items = new(_modes.Count);

		foreach ((string prefix, IQuickPickProvider mode) in _modes)
		{
			items.Add(
				new QuickPickItem(prefix, mode.Name) { Description = mode.Placeholder, TrailingText = prefix.Length == 0 ? null : prefix.TrimEnd() }
			);
		}

		return Task.FromResult<IReadOnlyList<QuickPickItem>>(items);
	}

	public Task AcceptAsync(QuickPickAcceptRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

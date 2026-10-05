using Nickelony.QuickPick;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

/// <summary>A mode that lists one item and accepts nothing, which a test registers beside a mode under test to switch to.</summary>
/// <param name="prefix">The prefix that selects the mode.</param>
internal sealed class ScriptedModeProvider(string prefix) : IQuickPickProvider
{
	public string Prefix { get; } = prefix;

	public string Name => "Scripted";

	public string Placeholder => "Scripted";

	public Task<IReadOnlyList<QuickPickItem>> GetItemsAsync(QuickPickRequest request, CancellationToken cancellationToken = default) =>
		Task.FromResult<IReadOnlyList<QuickPickItem>>([new QuickPickItem("only", "only")]);

	public Task AcceptAsync(QuickPickAcceptRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

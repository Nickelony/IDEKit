namespace Nickelony.QuickPick;

/// <summary>Offers a provider to a quick pick under a prefix: the text the user types first to pick the provider's mode.</summary>
/// <remarks>
/// A host offers the modes it wants, each under the prefix it wants, so a command palette alone is one mode under the empty prefix, and a host
/// that offers files and commands registers files under the empty prefix and commands under <c>&gt;</c>. The controller keeps the recent items
/// of a mode by its prefix.
/// </remarks>
public sealed record QuickPickMode
{
	/// <param name="provider">The provider that supplies the items of the mode and acts on the one the user chooses.</param>
	/// <param name="prefix">
	/// The text that picks the mode, or <see langword="null"/> to use <see cref="IQuickPickProvider.Prefix"/> of the provider. An empty string makes
	/// the mode the default one.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="provider"/> is <see langword="null"/>.</exception>
	public QuickPickMode(IQuickPickProvider provider, string? prefix = null)
	{
		ArgumentNullException.ThrowIfNull(provider);

		Provider = provider;
		Prefix = prefix;
	}

	/// <summary>The provider that supplies the items of the mode and acts on the one the user chooses.</summary>
	public IQuickPickProvider Provider { get; }

	/// <summary>The text that picks the mode, or <see langword="null"/> to use <see cref="IQuickPickProvider.Prefix"/> of the provider.</summary>
	public string? Prefix { get; }
}

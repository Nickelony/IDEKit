using Nickelony.IDEKit.Infrastructure;

namespace Nickelony.IDEKit.IntelliSense.CodeActions;

/// <summary>
/// Represents one code action as presented by a host's code-action UI.
/// </summary>
/// <remarks>
/// The item carries presentation data only: the action's title, the optional provider-defined kind,
/// and the preferred flag (hosts typically emphasize the preferred action). <see cref="Payload"/> is
/// opaque host data - for example the language-server action a host adapter keeps - and is handed
/// back unchanged to the host's execute callback when the user invokes the action; the library never
/// inspects it.
/// </remarks>
public sealed record TextCodeActionItem
{
	/// <summary>
	/// Initializes a new instance of the <see cref="TextCodeActionItem"/> record.
	/// </summary>
	/// <param name="title">The action title presented to the user.</param>
	/// <param name="kind">
	/// The provider-defined action kind (for example a diagnostic-fix category), or
	/// <see langword="null"/> when the action carries no kind; blank values are treated as absent
	/// and other values are trimmed.
	/// </param>
	/// <param name="isPreferred">Whether the action is the provider-preferred action for the context.</param>
	/// <param name="payload">
	/// Opaque host data handed back to the host's execute callback; the library never inspects it.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="title"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException"><paramref name="title"/> is blank.</exception>
	public TextCodeActionItem(string title, string? kind = null, bool isPreferred = false, object? payload = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(title);

		Title = title;
		Kind = OptionalText.Normalize(kind);
		IsPreferred = isPreferred;
		Payload = payload;
	}

	/// <summary>Gets the action title presented to the user.</summary>
	public string Title { get; }

	/// <summary>
	/// Gets the provider-defined action kind (for example a diagnostic-fix category), or
	/// <see langword="null"/> when the action carries no kind. A blank value is treated as absent.
	/// </summary>
	public string? Kind { get; }

	/// <summary>
	/// Gets a value indicating whether the action is the provider-preferred action for the context.
	/// </summary>
	public bool IsPreferred { get; }

	/// <summary>Gets the opaque host data handed back to the host's execute callback.</summary>
	public object? Payload { get; }
}

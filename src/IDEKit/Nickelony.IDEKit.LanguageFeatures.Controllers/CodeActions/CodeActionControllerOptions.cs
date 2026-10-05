using Nickelony.IDEKit.Core.Validation;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.CodeActions;

/// <summary>Stores the debounce delay used by a code-action controller.</summary>
/// <remarks>
/// Start from <see cref="Default"/> and override only the options needed with a <c>with</c> expression; each option validates itself when assigned,
/// so an invalid value fails at the offending expression rather than at controller construction. The record carries timing policy only -
/// the menu's presentation sizing lives with the presenter that creates the menu.
/// </remarks>
public sealed record CodeActionControllerOptions
{
	private readonly TimeSpan _requestDebounceDelay = TimeSpan.FromMilliseconds(250.0);

	/// <summary>The debounce delay before a context change schedules a request.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is negative.</exception>
	/// <remarks>
	/// Caret, selection, and document changes restart the delay, so holding an arrow key or typing settles before the controller asks the host
	/// for actions.
	/// </remarks>
	public TimeSpan RequestDebounceDelay
	{
		get => _requestDebounceDelay;
		init => _requestDebounceDelay = NumericValidation.NonNegative(value, nameof(RequestDebounceDelay));
	}

	/// <summary>The default options: a 250-millisecond request debounce delay.</summary>
	public static CodeActionControllerOptions Default { get; } = new();
}

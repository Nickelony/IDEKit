using Nickelony.IDEKit.Core.Validation;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

/// <summary>Stores the request timing and trigger policy used by a <see cref="CompletionController"/>.</summary>
/// <remarks>
/// Start from <see cref="Default"/> and override only the options needed with a <c>with</c> expression; each option validates
/// itself when assigned, so an invalid value fails at the offending expression rather than at controller construction. The record carries
/// timing and trigger policy only: the presenter that shows the window reads the window geometry (<see cref="CompletionWindowOptions"/>,
/// which sits beside this record), the tooltip delay and the commit-character setting, and an editor binding carries
/// them in its presentation options.
/// </remarks>
public sealed record CompletionControllerOptions
{
	private readonly TimeSpan _requestDebounceDelay = TimeSpan.FromMilliseconds(120.0);

	private readonly CompletionTriggerPolicy _triggers = CompletionTriggerPolicy.Default;

	/// <summary>The debounce delay before a scheduled completion request runs; defaults to 120 milliseconds.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is negative.</exception>
	/// <remarks>
	/// The debounce collapses a burst of keystrokes into one provider round trip; a host can lower the delay for a snappier popup at the cost
	/// of more requests.
	/// </remarks>
	public TimeSpan RequestDebounceDelay
	{
		get => _requestDebounceDelay;
		init => _requestDebounceDelay = NumericValidation.NonNegative(value, nameof(RequestDebounceDelay));
	}

	/// <summary>The rule that decides which typed characters open completion; defaults to <see cref="CompletionTriggerPolicy.Default"/>.</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	/// <remarks>
	/// A host that starts every request itself sets <see cref="CompletionTriggerPolicy.Manual"/>; one that turns typing triggers off at run time
	/// answers <see cref="CompletionScopes.None"/> from the controller's scope hook.
	/// </remarks>
	public CompletionTriggerPolicy Triggers
	{
		get => _triggers;
		init => _triggers = value ?? throw new ArgumentNullException(nameof(Triggers));
	}

	public static CompletionControllerOptions Default { get; } = new();
}

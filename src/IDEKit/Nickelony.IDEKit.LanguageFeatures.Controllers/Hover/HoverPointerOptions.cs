using Nickelony.IDEKit.Core.Validation;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Hover;

/// <summary>Selects when the pointer tracking of an editor asks a <see cref="HoverController"/> for a hover.</summary>
public sealed record HoverPointerOptions
{
	private readonly TimeSpan? _restDelay = TimeSpan.FromMilliseconds(400);

	/// <summary>The options that request a hover once the pointer rests for 400 milliseconds.</summary>
	public static HoverPointerOptions Default { get; } = new();

	/// <summary>
	/// How long the pointer rests over the text before the controller requests a hover, or <see langword="null"/> to request one only when the host
	/// calls <see cref="HoverController.RequestAsync"/>.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is negative.</exception>
	/// <remarks>
	/// A move restarts the wait. When the pointer leaves the text area, the wait ends and the controller evaluates
	/// the pointer at once, which finds no hover target and asks the host to hide its tooltip.
	/// </remarks>
	public TimeSpan? RestDelay
	{
		get => _restDelay;
		init => _restDelay = value is { } delay ? NumericValidation.NonNegative(delay, nameof(value)) : null;
	}
}

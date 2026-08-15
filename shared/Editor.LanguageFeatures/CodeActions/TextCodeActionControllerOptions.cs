using Nickelony.IDEKit.Infrastructure;

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.CodeActions;
#else
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.CodeActions;
#endif

/// <summary>
/// Stores the timing options used by a code-action controller.
/// </summary>
/// <remarks>
/// <para>
/// A host can start from <see cref="Default"/> and override only the options it needs with a
/// <c>with</c> expression; every option validates itself when it is assigned, so an invalid value is
/// rejected at the offending <c>with</c> expression or object initializer instead of when the
/// controller is constructed.
/// </para>
/// <para>
/// The record carries timing policy only; editor-presentation sizing such as the menu's height cap
/// and anchor offsets lives with the menu presenter that creates the menu.
/// </para>
/// </remarks>
public sealed record TextCodeActionControllerOptions
{
	private readonly TimeSpan _requestDebounceDelay = TimeSpan.FromMilliseconds(250.0);

	/// <summary>
	/// Gets the debounce delay before a context change schedules a request. Defaults to 250 milliseconds.
	/// </summary>
	/// <remarks>
	/// Caret, selection, and document changes restart the delay, so holding an arrow key or typing
	/// settles before the controller asks the host for actions.
	/// </remarks>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is negative.</exception>
	public TimeSpan RequestDebounceDelay
	{
		get => _requestDebounceDelay;
		init => _requestDebounceDelay = NumericValidation.NonNegative(value, nameof(RequestDebounceDelay));
	}

	/// <summary>
	/// Gets the default options: a 250-millisecond request debounce delay.
	/// </summary>
	public static TextCodeActionControllerOptions Default { get; } = new();
}

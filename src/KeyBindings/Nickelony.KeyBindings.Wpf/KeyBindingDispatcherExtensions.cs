using System.Windows.Input;

namespace Nickelony.KeyBindings.Wpf;

/// <summary>
/// Adapts WPF key events to the host-neutral <see cref="KeyBindingDispatcher{TCommandId}"/>.
/// </summary>
public static class KeyBindingDispatcherExtensions
{
	/// <summary>
	/// Tries to dispatch a WPF key-down event to the command bound to the pressed key chord.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The method filters before it converts: an event that another handler already marked handled is never
	/// dispatched, and <paramref name="options"/> decides which further events are skipped (auto-repeat and
	/// AltGr by default). The conversion notes of <see cref="KeyEventArgsExtensions.ToKeyCombo"/> (IME and
	/// dead-character events) apply to what remains.
	/// </para>
	/// <para>
	/// The method does not mark the event handled, whether it dispatches or not: the caller marks
	/// <see cref="System.Windows.RoutedEventArgs.Handled"/> for both
	/// <see cref="KeyChordDispatchResult.Executed"/> and <see cref="KeyChordDispatchResult.Pending"/>, so a host
	/// that runs several handlers keeps control of the consumption order while a buffered chord still consumes
	/// the keystroke.
	/// </para>
	/// </remarks>
	/// <typeparam name="TCommandId">The command identity type.</typeparam>
	/// <param name="dispatcher">The dispatcher that resolves and executes the bound command.</param>
	/// <param name="e">The WPF key-down event to inspect.</param>
	/// <param name="context">
	/// The active context token, or <see langword="null"/> for the always-active context. Pass the host's
	/// current token, for example the view mode of the control that raises the event; a host with no
	/// contexts passes <see langword="null"/>.
	/// </param>
	/// <param name="options">
	/// The filter options, or <see langword="null"/> for <see cref="KeyDownHandlingOptions.Default"/>.
	/// </param>
	/// <returns>
	/// <see cref="KeyChordDispatchResult.Executed"/> when a key chord was recognized, the command can execute, and
	/// the command was invoked; <see cref="KeyChordDispatchResult.Pending"/> when the key continued or started a
	/// chord; otherwise, <see cref="KeyChordDispatchResult.NotHandled"/> for events that are already handled,
	/// auto-repeat or AltGr events the options ignore, modifier-only keystrokes, IME-processed keys, and
	/// keystrokes that neither complete nor continue a bound chord.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="dispatcher"/> or <paramref name="e"/> is <see langword="null"/>.</exception>
	public static KeyChordDispatchResult HandleKeyDown<TCommandId>(
		this KeyBindingDispatcher<TCommandId> dispatcher,
		KeyEventArgs e,
		string? context,
		KeyDownHandlingOptions? options = null)
		where TCommandId : notnull
	{
		ArgumentNullException.ThrowIfNull(dispatcher);
		ArgumentNullException.ThrowIfNull(e);

		// An event another handler already consumed must not be dispatched a second time; the caller owns
		// the input route, so respecting its decision is the only safe policy.
		if (e.Handled)
			return KeyChordDispatchResult.NotHandled;

		KeyDownHandlingOptions effectiveOptions = options ?? KeyDownHandlingOptions.Default;

		if (effectiveOptions.IgnoreAutoRepeat && e.IsRepeat)
			return KeyChordDispatchResult.NotHandled;

		if (effectiveOptions.IgnoreAltGr && e.IsAltGr())
			return KeyChordDispatchResult.NotHandled;

		KeyCombo? keyCombo = e.ToKeyCombo();

		return keyCombo is null ? KeyChordDispatchResult.NotHandled : dispatcher.Dispatch(keyCombo.Value, context);
	}
}

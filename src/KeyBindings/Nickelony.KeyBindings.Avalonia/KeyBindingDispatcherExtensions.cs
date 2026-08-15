using Avalonia.Input;

namespace Nickelony.KeyBindings.Avalonia;

/// <summary>
/// Adapts Avalonia key events to the host-neutral <see cref="KeyBindingDispatcher{TCommandId}"/>.
/// </summary>
/// <remarks>
/// <para>
/// The method converts with <see cref="KeyEventArgsExtensions.ToKeyCombo"/> and dispatches the result.
/// An event that another handler already marked handled is never dispatched, because the caller owns
/// the input route and respecting its decision is the only safe policy.
/// </para>
/// <para>
/// Avalonia's <see cref="KeyEventArgs"/> exposes neither an auto-repeat flag nor left/right key state,
/// so this adapter applies no auto-repeat or AltGr filter; a host that needs one applies it before it
/// calls the adapter. The method does not mark the event handled, whether it dispatches or not: the
/// caller marks <c>Handled</c> for both
/// <see cref="KeyChordDispatchResult.Executed"/> and <see cref="KeyChordDispatchResult.Pending"/>, so a
/// host that runs several handlers keeps control of the consumption order while a buffered chord still
/// consumes the keystroke.
/// </para>
/// </remarks>
public static class KeyBindingDispatcherExtensions
{
	/// <summary>
	/// Tries to dispatch an Avalonia key-down event to the command bound to the pressed key chord.
	/// </summary>
	/// <typeparam name="TCommandId">The command identity type.</typeparam>
	/// <param name="dispatcher">The dispatcher that resolves and executes the bound command.</param>
	/// <param name="e">The Avalonia key-down event to inspect.</param>
	/// <param name="context">
	/// The active context token, or <see langword="null"/> for the always-active context. Pass the host's
	/// current token, for example the view mode of the control that raises the event; a host with no
	/// contexts passes <see langword="null"/>.
	/// </param>
	/// <returns>
	/// <see cref="KeyChordDispatchResult.Executed"/> when a key chord was recognized, the command can execute, and
	/// the command was invoked; <see cref="KeyChordDispatchResult.Pending"/> when the key continued or started a
	/// chord; otherwise, <see cref="KeyChordDispatchResult.NotHandled"/> for events that are already handled,
	/// modifier-only keystrokes, IME-processed keys, and keystrokes that neither complete nor continue a bound
	/// chord.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="dispatcher"/> or <paramref name="e"/> is <see langword="null"/>.</exception>
	public static KeyChordDispatchResult HandleKeyDown<TCommandId>(
		this KeyBindingDispatcher<TCommandId> dispatcher,
		KeyEventArgs e,
		string? context)
		where TCommandId : notnull
	{
		ArgumentNullException.ThrowIfNull(dispatcher);
		ArgumentNullException.ThrowIfNull(e);

		// An event another handler already consumed must not be dispatched a second time; the caller owns
		// the input route, so respecting its decision is the only safe policy.
		if (e.Handled)
			return KeyChordDispatchResult.NotHandled;

		KeyCombo? keyCombo = e.ToKeyCombo();

		return keyCombo is null ? KeyChordDispatchResult.NotHandled : dispatcher.Dispatch(keyCombo.Value, context);
	}
}

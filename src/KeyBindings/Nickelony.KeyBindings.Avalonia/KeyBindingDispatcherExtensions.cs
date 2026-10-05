using Avalonia.Input;

namespace Nickelony.KeyBindings.Avalonia;

/// <summary>
/// Adapts Avalonia key events to the toolkit-neutral <see cref="IKeyBindingDispatcher"/>, which a single dispatcher and a dispatcher chain
/// both implement.
/// </summary>
public static class KeyBindingDispatcherExtensions
{
	/// <summary>Tries to dispatch an Avalonia key-down event to the command bound to the pressed key chord.</summary>
	/// <param name="dispatcher">The dispatcher or chain that resolves and executes the bound command.</param>
	/// <param name="e">The Avalonia key-down event to inspect.</param>
	/// <param name="context">
	/// The active context token, or <see langword="null"/> for the always-active context. Pass the host's current token, for example the view mode
	/// of the control that raises the event; a host with no contexts passes <see langword="null"/>.
	/// </param>
	/// <param name="repeatGate">
	/// The gate that tells a new press from the repeat of a key still held, or <see langword="null"/> to dispatch every key-down event.
	/// </param>
	/// <returns>
	/// <see cref="KeyStrokeDispatchOutcome.Executed"/> when a key
	/// chord was recognized, the command can execute, and the command was invoked; <see cref="KeyStrokeDispatchOutcome.Pending"/> when the key
	/// continued or started a chord; otherwise, <see cref="KeyStrokeDispatchOutcome.NotHandled"/> for events that are already handled,
	/// repeats the gate ignores, modifier-only strokes, IME-processed keys, and strokes that neither complete nor continue a bound chord.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="dispatcher"/> or <paramref name="e"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// <para>
	/// The method converts with <see cref="KeyEventArgsExtensions.ToKeyStroke"/> and dispatches the stroke. An event that another handler
	/// already marked handled is never dispatched, because the caller owns the input route and respecting its decision is the only safe policy.
	/// </para>
	/// <para>
	/// Avalonia's <see cref="KeyEventArgs"/> exposes no auto-repeat flag, so the adapter ignores repeats only through <paramref name="repeatGate"/>.
	/// It does not detect AltGr and applies no AltGr filter; a host that needs one applies it before it calls the adapter.
	/// <include file="../../../shared/docs/KeyBindingDispatcherExtensions.xml" path="doc/members/member[@name='KeyBindingDispatcherExtensions.HandleKeyDown.Remarks.Handled']/*"/>
	/// </para>
	/// </remarks>
	public static KeyStrokeDispatchOutcome HandleKeyDown(
		this IKeyBindingDispatcher dispatcher,
		KeyEventArgs e,
		string? context,
		KeyRepeatGate? repeatGate = null
	)
	{
		ArgumentNullException.ThrowIfNull(dispatcher);
		ArgumentNullException.ThrowIfNull(e);

		if (e.Handled)
			return KeyStrokeDispatchOutcome.NotHandled;

		if (repeatGate is not null && !repeatGate.Admit(e))
			return KeyStrokeDispatchOutcome.NotHandled;

		var keyStroke = e.ToKeyStroke();

		return keyStroke is null ? KeyStrokeDispatchOutcome.NotHandled : dispatcher.Dispatch(keyStroke.Value, context);
	}
}

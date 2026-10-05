using System.Windows.Input;

namespace Nickelony.KeyBindings.Wpf;

/// <summary>
/// Adapts WPF key events to the toolkit-neutral <see cref="IKeyBindingDispatcher"/>, which a single dispatcher and a dispatcher chain both implement.
/// </summary>
public static class KeyBindingDispatcherExtensions
{
	/// <summary>Tries to dispatch a WPF key-down event to the command bound to the pressed key chord.</summary>
	/// <param name="dispatcher">The dispatcher or chain that resolves and executes the bound command.</param>
	/// <param name="e">The WPF key-down event to inspect.</param>
	/// <param name="context">
	/// The active context token, or <see langword="null"/> for the always-active context. Pass the host's current token, for example the view mode
	/// of the control that raises the event; a host with no contexts passes <see langword="null"/>.
	/// </param>
	/// <param name="options">The filter options, or <see langword="null"/> for <see cref="KeyDownHandlingOptions.Default"/>.</param>
	/// <returns>
	/// <see cref="KeyStrokeDispatchOutcome.Executed"/> when a key chord was recognized,
	/// the command can execute, and the command was invoked; <see cref="KeyStrokeDispatchOutcome.Pending"/> when the key continued
	/// or started a chord; otherwise, <see cref="KeyStrokeDispatchOutcome.NotHandled"/> for events that are already handled, auto-repeat or AltGr
	/// events the options ignore, modifier-only strokes, IME-processed keys, and strokes that neither complete nor continue a bound chord.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="dispatcher"/> or <paramref name="e"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// <para>
	/// The method filters before it converts: an event that another handler already marked
	/// handled is never dispatched, and <paramref name="options"/> decides which further events are skipped (auto-repeat and AltGr by default).
	/// The conversion notes of <see cref="KeyEventArgsExtensions.ToKeyStroke"/> (IME and dead-character events) apply to what remains.
	/// <include file="../../../shared/docs/KeyBindingDispatcherExtensions.xml" path="doc/members/member[@name='KeyBindingDispatcherExtensions.HandleKeyDown.Remarks.Handled']/*"/>
	/// </para>
	/// </remarks>
	public static KeyStrokeDispatchOutcome HandleKeyDown(
		this IKeyBindingDispatcher dispatcher,
		KeyEventArgs e,
		string? context,
		KeyDownHandlingOptions? options = null
	)
	{
		ArgumentNullException.ThrowIfNull(dispatcher);
		ArgumentNullException.ThrowIfNull(e);

		if (e.Handled)
			return KeyStrokeDispatchOutcome.NotHandled;

		KeyDownHandlingOptions effectiveOptions = options ?? KeyDownHandlingOptions.Default;

		if (effectiveOptions.IgnoreAutoRepeat && e.IsRepeat)
			return KeyStrokeDispatchOutcome.NotHandled;

		if (effectiveOptions.IgnoreAltGr && e.IsAltGr())
			return KeyStrokeDispatchOutcome.NotHandled;

		var keyStroke = e.ToKeyStroke();

		return keyStroke is null ? KeyStrokeDispatchOutcome.NotHandled : dispatcher.Dispatch(keyStroke.Value, context);
	}
}

using Avalonia.Input;

namespace Nickelony.KeyBindings.Avalonia;

/// <summary>
/// Converts Avalonia key events to <see cref="KeyCombo"/> values.
/// </summary>
/// <remarks>
/// <para>
/// The key is read from <see cref="KeyEventArgs.Key"/> and the modifiers from
/// <see cref="KeyEventArgs.KeyModifiers"/>. <see cref="KeyEventArgs.Key"/> is the layout-resolved
/// virtual key, so a binding follows the active keyboard layout; the WPF adapter resolves the same way,
/// so both adapters agree. IME-processed and dead-character events, the <see cref="Key.None"/> key, and
/// keys that cannot be bound return <see langword="null"/>.
/// </para>
/// <para>
/// Unlike WPF, Avalonia resolves the base virtual key itself and never raises
/// <see cref="Key.System"/> for an Alt-combined key, so no system-key normalization is needed.
/// Avalonia also exposes neither an auto-repeat flag nor left/right key state, so this conversion
/// (and <see cref="KeyBindingDispatcherExtensions.HandleKeyDown{TCommandId}"/>) applies no
/// auto-repeat or AltGr filter; a host that needs one supplies it before it calls the adapter.
/// </para>
/// </remarks>
public static class KeyEventArgsExtensions
{
	/// <summary>
	/// Creates a <see cref="KeyCombo"/> from an Avalonia key event.
	/// </summary>
	/// <param name="e">The Avalonia key event whose key and modifier keys are read.</param>
	/// <returns>The key combo, or <see langword="null"/> when the event does not describe a bindable combo.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="e"/> is <see langword="null"/>.</exception>
	public static KeyCombo? ToKeyCombo(this KeyEventArgs e)
	{
		ArgumentNullException.ThrowIfNull(e);

		if (e.Key is Key.ImeProcessed or Key.DeadCharProcessed or Key.None)
			return null;

		KeyCode? keyCode = KeyCodeMapper.ToKeyCode(e.Key);

		if (keyCode is null)
			return null;

		return new KeyCombo(keyCode.Value, KeyCodeMapper.ToKeyModifierSet(e.KeyModifiers));
	}
}

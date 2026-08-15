using System.Windows.Input;

namespace Nickelony.KeyBindings.Wpf;

/// <summary>
/// Converts WPF key events to <see cref="KeyCombo"/> values.
/// </summary>
public static class KeyEventArgsExtensions
{
	/// <summary>
	/// Creates a <see cref="KeyCombo"/> from a WPF key event.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <see cref="Key.System"/> is normalized to <see cref="KeyEventArgs.SystemKey"/>, and modifiers
	/// are read from the event's keyboard device rather than the global
	/// <see cref="Keyboard.Modifiers"/>. IME and dead-character events, modifier-only key events,
	/// and keys that cannot be bound return <see langword="null"/>.
	/// </para>
	/// <para>
	/// The conversion does not inspect <see cref="KeyEventArgs.IsRepeat"/> or AltGr;
	/// <see cref="KeyBindingDispatcherExtensions.HandleKeyDown{TCommandId}"/> applies those filters for
	/// hosts that dispatch the converted combo, and <see cref="IsAltGr"/> owns the AltGr detection.
	/// </para>
	/// </remarks>
	/// <param name="e">The WPF key event whose key and currently pressed modifier keys are read.</param>
	/// <returns>The key combo, or <see langword="null"/> when the event does not describe a bindable combo.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="e"/> is <see langword="null"/>.</exception>
	public static KeyCombo? ToKeyCombo(this KeyEventArgs e)
	{
		ArgumentNullException.ThrowIfNull(e);

		Key key = e.Key == Key.System ? e.SystemKey : e.Key;

		if (key is Key.ImeProcessed or Key.DeadCharProcessed)
			return null;

		KeyCode? keyCode = KeyCodeMapper.ToKeyCode(key);

		if (keyCode is null)
			return null;

		return new KeyCombo(keyCode.Value, ReadModifiers(e.KeyboardDevice));
	}

	/// <summary>
	/// Determines whether a key event was produced by AltGr, the right Alt key acting as a character-level
	/// modifier.
	/// </summary>
	/// <remarks>
	/// Windows synthesizes AltGr as a left Ctrl press followed by the right Alt key, so this reports
	/// <see langword="true"/> for exactly that pair while the key event is raised. A genuine Ctrl+Alt chord
	/// presses the left Alt key or the right Ctrl key and is not classified as AltGr. Because the pair is
	/// reported as Ctrl+Alt, a layout with AltGr would otherwise let a character-producing stroke match a
	/// Ctrl+Alt binding; <see cref="KeyBindingDispatcherExtensions.HandleKeyDown{TCommandId}"/> drops such
	/// strokes by default.
	/// </remarks>
	/// <param name="e">The WPF key event to inspect.</param>
	/// <returns><see langword="true"/> when the event was produced by AltGr; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="e"/> is <see langword="null"/>.</exception>
	public static bool IsAltGr(this KeyEventArgs e)
	{
		ArgumentNullException.ThrowIfNull(e);

		KeyboardDevice keyboardDevice = e.KeyboardDevice;

		return keyboardDevice.IsKeyDown(Key.RightAlt) && keyboardDevice.IsKeyDown(Key.LeftCtrl);
	}

	private static KeyModifierSet ReadModifiers(KeyboardDevice keyboardDevice)
	{
		KeyModifierSet modifiers = KeyModifierSet.None;

		if (keyboardDevice.IsKeyDown(Key.LeftCtrl) || keyboardDevice.IsKeyDown(Key.RightCtrl))
			modifiers |= KeyModifierSet.Control;

		if (keyboardDevice.IsKeyDown(Key.LeftShift) || keyboardDevice.IsKeyDown(Key.RightShift))
			modifiers |= KeyModifierSet.Shift;

		if (keyboardDevice.IsKeyDown(Key.LeftAlt) || keyboardDevice.IsKeyDown(Key.RightAlt))
			modifiers |= KeyModifierSet.Alt;

		if (keyboardDevice.IsKeyDown(Key.LWin) || keyboardDevice.IsKeyDown(Key.RWin))
			modifiers |= KeyModifierSet.Meta;

		return modifiers;
	}
}

using System.Windows.Input;

namespace Nickelony.KeyBindings.Wpf;

/// <summary>Converts WPF key events to <see cref="KeyStroke"/> values.</summary>
public static class KeyEventArgsExtensions
{
	/// <summary>Creates a <see cref="KeyStroke"/> from a WPF key event.</summary>
	/// <param name="e">The WPF key event whose key and currently pressed modifier keys are read.</param>
	/// <returns>The key stroke, or <see langword="null"/> when the event does not describe a bindable stroke.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="e"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// <para>
	/// <see cref="Key.System"/> is normalized to <see cref="KeyEventArgs.SystemKey"/>, and modifiers are read from the event's
	/// keyboard device rather than the global <see cref="Keyboard.Modifiers"/>. IME and dead-character events, modifier-only key events,
	/// and keys that cannot be bound return <see langword="null"/>.
	/// </para>
	/// <para>
	/// The conversion does not inspect <see cref="KeyEventArgs.IsRepeat"/> or AltGr;
	/// <see cref="KeyBindingDispatcherExtensions.HandleKeyDown"/> applies those filters for hosts that dispatch the converted stroke,
	/// and <see cref="IsAltGr"/> owns the AltGr detection.
	/// </para>
	/// </remarks>
	public static KeyStroke? ToKeyStroke(this KeyEventArgs e)
	{
		ArgumentNullException.ThrowIfNull(e);

		Key key = e.Key == Key.System ? e.SystemKey : e.Key;

		if (key is Key.ImeProcessed or Key.DeadCharProcessed)
			return null;

		var keyCode = KeyCodeConversion.ToKeyCode(key);

		if (keyCode is null)
			return null;

		return new KeyStroke(keyCode.Value, ReadModifiers(e.KeyboardDevice));
	}

	/// <summary>Determines whether a key event is an AltGr stroke, the right Alt key acting as a character-level modifier.</summary>
	/// <param name="e">The WPF key event to inspect.</param>
	/// <returns><see langword="true"/> when the event is an AltGr stroke; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="e"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// Windows synthesizes AltGr as a left Ctrl press followed by the right Alt key, and reports the pair as Ctrl+Alt. A stroke is classified
	/// as AltGr only when the pressed key also types a character (a letter, a main-row digit, or OEM punctuation). A genuine Ctrl+Alt chord,
	/// the left Alt key, the right Ctrl key, or a key that types no character is not AltGr. Because a character stroke would otherwise match
	/// a Ctrl+Alt binding, <see cref="KeyBindingDispatcherExtensions.HandleKeyDown"/> drops AltGr strokes by default.
	/// </remarks>
	public static bool IsAltGr(this KeyEventArgs e)
	{
		ArgumentNullException.ThrowIfNull(e);

		KeyboardDevice keyboardDevice = e.KeyboardDevice;

		return IsCharacterProducingKey(e.Key == Key.System ? e.SystemKey : e.Key)
			&& keyboardDevice.IsKeyDown(Key.RightAlt)
			&& keyboardDevice.IsKeyDown(Key.LeftCtrl);
	}

	/// <summary>Determines whether a key types a character on a standard layout: a letter, a main-row digit, or an OEM punctuation key.</summary>
	/// <param name="key">The key to test.</param>
	/// <returns><see langword="true"/> when the key types a character; otherwise, <see langword="false"/>.</returns>
	private static bool IsCharacterProducingKey(Key key)
	{
		return key
			is (>= Key.A and <= Key.Z)
				or (>= Key.D0 and <= Key.D9)
				or Key.OemPlus
				or Key.OemComma
				or Key.OemMinus
				or Key.OemPeriod
				or Key.OemQuestion
				or Key.OemTilde
				or Key.OemOpenBrackets
				or Key.OemPipe
				or Key.OemCloseBrackets
				or Key.OemQuotes
				or Key.Oem8
				or Key.OemBackslash
				or Key.OemClear;
	}

	/// <summary>
	/// Reads the modifier keys a keyboard device reports, mapping the Control, Shift, Alt, and Meta flags through the shared
	/// <see cref="KeyCodeConversion"/> authority.
	/// </summary>
	/// <param name="keyboardDevice">The keyboard device to read.</param>
	/// <returns>The modifier flags held with the event.</returns>
	private static KeyModifiers ReadModifiers(KeyboardDevice keyboardDevice)
	{
		var modifiers = KeyCodeConversion.ToKeyModifiers(keyboardDevice.Modifiers);

		// WPF's KeyboardDevice.Modifiers does not report the Windows key, so the Meta flag is the one modifier
		// the shared authority cannot supply and is added explicitly.
		if (keyboardDevice.IsKeyDown(Key.LWin) || keyboardDevice.IsKeyDown(Key.RWin))
			modifiers |= KeyModifiers.Meta;

		return modifiers;
	}
}

using Avalonia.Input;

namespace Nickelony.KeyBindings.Avalonia;

/// <summary>Converts Avalonia key events to <see cref="KeyStroke"/> values.</summary>
/// <remarks>
/// The key is read from <see cref="KeyEventArgs.Key"/> and the modifiers from <see cref="KeyEventArgs.KeyModifiers"/>.
/// <see cref="KeyEventArgs.Key"/> is the layout-resolved virtual key, so a binding follows the active keyboard layout.
/// IME-processed and dead-character events, and keys that cannot be bound, return <see langword="null"/>.
/// </remarks>
public static class KeyEventArgsExtensions
{
	// Avalonia resolves the base virtual key itself and never raises Key.System for an Alt-combined key,
	// so this conversion needs no system-key normalization.

	/// <summary>Creates a <see cref="KeyStroke"/> from an Avalonia key event.</summary>
	/// <param name="e">The Avalonia key event whose key and modifier keys are read.</param>
	/// <returns>The key stroke, or <see langword="null"/> when the event does not describe a bindable stroke.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="e"/> is <see langword="null"/>.</exception>
	public static KeyStroke? ToKeyStroke(this KeyEventArgs e)
	{
		ArgumentNullException.ThrowIfNull(e);

		if (e.Key is Key.ImeProcessed or Key.DeadCharProcessed)
			return null;

		var keyCode = KeyCodeConversion.ToKeyCode(e.Key);

		if (keyCode is null)
			return null;

		return new KeyStroke(keyCode.Value, KeyCodeConversion.ToKeyModifiers(e.KeyModifiers));
	}
}

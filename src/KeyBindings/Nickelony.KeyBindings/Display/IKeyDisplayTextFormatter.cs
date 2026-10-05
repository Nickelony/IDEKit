namespace Nickelony.KeyBindings;

/// <summary>Renders a key stroke or a key chord as display text for menus, toolbars, and shortcut editors.</summary>
/// <remarks>
/// <see cref="KeyDisplayTextFormatter"/> is the package default and carries no desktop conventions, so its text
/// is identical on every host; a host selects a formatter through <c>KeyBindingServiceOptions.DisplayTextFormatter</c>. A formatter that changes
/// only the labels, the modifier order, or the separators derives from that default and overrides its text hooks, keeping the stroke sequencing
/// and the uninitialized-stroke contract in one place.
/// </remarks>
public interface IKeyDisplayTextFormatter
{
	/// <summary>Returns the display text for a single key stroke.</summary>
	/// <param name="keyStroke">The key stroke to render.</param>
	/// <returns>The display text, for example <c>Control+Shift+S</c>.</returns>
	/// <exception cref="ArgumentException"><paramref name="keyStroke"/> is uninitialized.</exception>
	string GetDisplayText(KeyStroke keyStroke);

	/// <summary>Returns the display text for a key chord.</summary>
	/// <param name="keyChord">The key chord to render. A one-stroke chord renders as its single stroke.</param>
	/// <returns>The display text, for example <c>Control+K, S</c>.</returns>
	/// <exception cref="ArgumentException"><paramref name="keyChord"/> is uninitialized.</exception>
	/// <remarks>
	/// An implementation decides how the stroke texts are separated; the package default joins them with <c>, </c>.
	/// </remarks>
	string GetDisplayText(KeyChord keyChord);
}

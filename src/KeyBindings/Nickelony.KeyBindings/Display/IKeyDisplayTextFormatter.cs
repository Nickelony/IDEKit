namespace Nickelony.KeyBindings;

/// <summary>
/// Renders a key combo or a key chord as display text for menus, toolbars, and shortcut editors.
/// </summary>
/// <remarks>
/// <para>
/// The package default is <see cref="KeyDisplayTextFormatter"/>, which renders the canonical
/// <see cref="KeyCode"/> and <see cref="KeyModifierSet"/> names and carries no platform conventions.
/// A host that wants platform-convention or localized text supplies its own formatter through
/// <see cref="KeyBindingServiceOptions.DisplayTextFormatter"/>; the toolkit-free
/// <see cref="DesktopKeyDisplayTextFormatter"/> ships the desktop conventions as opt-in formatters.
/// </para>
/// <para>
/// A formatter that only relabels the keys and modifiers, or that changes the modifier order or the
/// separators, derives from <see cref="KeyDisplayTextFormatter"/> and overrides its text hooks, so the
/// stroke sequencing and the uninitialized-combo contract stay in one place.
/// </para>
/// </remarks>
public interface IKeyDisplayTextFormatter
{
	/// <summary>
	/// Returns the display text for a single key combo.
	/// </summary>
	/// <param name="keyCombo">The key combo to render.</param>
	/// <returns>The display text, for example <c>Control+Shift+S</c>.</returns>
	/// <exception cref="ArgumentException"><paramref name="keyCombo"/> is uninitialized.</exception>
	string GetDisplayText(KeyCombo keyCombo);

	/// <summary>
	/// Returns the display text for a key chord.
	/// </summary>
	/// <remarks>
	/// An implementation decides how the stroke texts are separated. The package default joins them
	/// with <c>, </c>, so <c>Control+K</c> followed by <c>S</c> renders as <c>Control+K, S</c>; a host
	/// that follows another convention, such as a space or an arrow, supplies its own formatter.
	/// </remarks>
	/// <param name="keyChord">The key chord to render. A one-stroke chord renders as its single combo.</param>
	/// <returns>The display text, for example <c>Control+K, S</c>.</returns>
	/// <exception cref="ArgumentException"><paramref name="keyChord"/> is uninitialized.</exception>
	string GetDisplayText(KeyChord keyChord);
}

namespace Nickelony.KeyBindings;

/// <summary>Provides helpers over <see cref="KeyModifiers"/> values.</summary>
public static class KeyModifiersExtensions
{
	private const KeyModifiers Defined = KeyModifiers.Alt | KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Meta;

	/// <summary>Determines whether the set carries a bit that the key model does not define.</summary>
	/// <param name="modifiers">The modifiers to test.</param>
	/// <returns>
	/// <see langword="true"/> when <paramref name="modifiers"/> includes a bit outside the Alt, Control, Shift,
	/// and Meta flags; otherwise, <see langword="false"/>.
	/// </returns>
	public static bool HasUndefinedFlags(this KeyModifiers modifiers) => (modifiers & ~Defined) != KeyModifiers.None;
}

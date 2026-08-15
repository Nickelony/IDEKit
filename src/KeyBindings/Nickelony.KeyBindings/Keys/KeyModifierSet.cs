namespace Nickelony.KeyBindings;

/// <summary>
/// Modifier keys that are pressed together with a <see cref="KeyCode"/>.
/// </summary>
/// <remarks>
/// The flag values are owned by this package: each modifier is a single bit, and the four defined flags
/// occupy the low nibble. A framework adapter translates through an explicit table rather than an
/// integer cast. <see cref="Meta"/> maps to the Windows key on Windows hosts, the Command key on
/// macOS, and the Super key on Linux.
/// </remarks>
[Flags]
public enum KeyModifierSet
{
	/// <summary>No modifier key.</summary>
	None = 0,

	/// <summary>The Alt key (Option on macOS).</summary>
	Alt = 1,

	/// <summary>The Control key.</summary>
	Control = 2,

	/// <summary>The Shift key.</summary>
	Shift = 4,

	/// <summary>The operating-system key (Windows, Command, or Super).</summary>
	Meta = 8
}

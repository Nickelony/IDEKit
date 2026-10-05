namespace Nickelony.KeyBindings;

/// <summary>Modifier keys that are pressed together with a <see cref="KeyCode"/>.</summary>
/// <remarks>
/// Each modifier is a single bit and the flags occupy the low nibble; the values are owned by this package, so a framework adapter translates
/// through an explicit table rather than an integer cast. <see cref="Meta"/> maps to the Windows key on Windows hosts, the Command key on macOS,
/// and the Super key on Linux.
/// </remarks>
[Flags]
public enum KeyModifiers
{
	None = 0,

	/// <summary>The Alt key (Option on macOS).</summary>
	Alt = 1,

	Control = 2,

	Shift = 4,

	/// <summary>The operating-system key.</summary>
	Meta = 8,
}

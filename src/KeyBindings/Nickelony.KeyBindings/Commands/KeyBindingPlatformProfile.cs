using System.Numerics;

namespace Nickelony.KeyBindings;

/// <summary>
/// Describes how a host's default bindings map onto a desktop platform's modifier conventions. A
/// profile names the modifier that stands for the platform's primary shortcut modifier - Control on
/// Windows and Linux, Command (Meta) on macOS - so a host can declare a default binding once and have
/// it resolve to the platform's convention.
/// </summary>
/// <remarks>
/// <para>
/// The profile is a value the host supplies while it builds its command catalog. The package never
/// inspects the operating system, so the neutral core stays testable and the host decides which
/// platform it targets; <see cref="Default"/> carries the Control convention, and a host that targets
/// another convention sets <see cref="PrimaryModifier"/> itself.
/// </para>
/// <para>
/// The primary modifier is a single <see cref="KeyModifierSet"/> flag. A host combines it with any other
/// modifiers through <see cref="PrimaryStroke"/>; a binding that is not primary-relative is declared
/// with an ordinary <see cref="KeyCombo"/> or <see cref="KeyChord"/>.
/// </para>
/// </remarks>
public sealed record KeyBindingPlatformProfile
{
	private KeyModifierSet _primaryModifier = KeyModifierSet.Control;

	/// <summary>
	/// Gets or initializes the modifier a primary shortcut uses on the platform: Control for the default
	/// convention (the Windows and Linux convention) and Meta (Command) for macOS.
	/// </summary>
	/// <exception cref="ArgumentException">The assigned value is not exactly one defined modifier.</exception>
	public KeyModifierSet PrimaryModifier
	{
		get => _primaryModifier;
		init
		{
			if (
				value == KeyModifierSet.None
				|| (value & ~KeyModifierMasks.Defined) != KeyModifierSet.None
				|| !BitOperations.IsPow2((uint)value)
			)
			{
				throw new ArgumentException(
					"The primary modifier must be exactly one defined modifier.",
					nameof(PrimaryModifier)
				);
			}

			_primaryModifier = value;
		}
	}

	/// <summary>
	/// Gets the default profile, whose primary modifier is Control (the Windows and Linux convention). A
	/// host that targets another convention sets <see cref="PrimaryModifier"/> instead, for example Meta
	/// (Command) on macOS.
	/// </summary>
	public static KeyBindingPlatformProfile Default { get; } = new();

	/// <summary>
	/// Builds a stroke whose key carries the profile's primary modifier together with any additional
	/// modifiers.
	/// </summary>
	/// <param name="key">The stroke's primary key.</param>
	/// <param name="additionalModifiers">Any further modifiers to hold with the primary modifier.</param>
	/// <returns>The stroke, which converts implicitly to a one-stroke <see cref="KeyChord"/>.</returns>
	/// <exception cref="ArgumentException">
	/// <paramref name="key"/> is not a defined <see cref="KeyCode"/> member, or
	/// <paramref name="additionalModifiers"/> sets bits outside the defined <see cref="KeyModifierSet"/>
	/// flags.
	/// </exception>
	public KeyCombo PrimaryStroke(KeyCode key, KeyModifierSet additionalModifiers = KeyModifierSet.None)
		=> new(key, PrimaryModifier | additionalModifiers);
}

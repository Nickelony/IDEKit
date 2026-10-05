using System.Collections.Frozen;
using System.Windows.Input;

namespace Nickelony.KeyBindings.Wpf;

/// <summary>Converts between WPF <see cref="Key"/> values and the toolkit-neutral <see cref="KeyCode"/> values.</summary>
/// <remarks>
/// <include file="../../../shared/docs/KeyCodeConversion.xml" path="doc/members/member[@name='KeyCodeConversion.Remarks']/*"/>
/// </remarks>
public static class KeyCodeConversion
{
	private const ModifierKeys DefinedModifierKeys = ModifierKeys.Alt | ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Windows;

	private static readonly (Key Key, KeyCode Code)[] s_pairs = KeyCodeConversionTable.Pairs;

	private static readonly FrozenDictionary<Key, KeyCode> s_keyToCode = s_pairs.ToFrozenDictionary(pair => pair.Key, pair => pair.Code);

	private static readonly FrozenDictionary<KeyCode, Key> s_codeToKey = s_pairs.ToFrozenDictionary(pair => pair.Code, pair => pair.Key);

	/// <summary>Converts a WPF key to the matching <see cref="KeyCode"/>.</summary>
	/// <param name="key">The WPF key to convert.</param>
	/// <returns>The matching key code, or <see langword="null"/> when the key cannot be the primary key of a binding.</returns>
	public static KeyCode? ToKeyCode(Key key) => s_keyToCode.TryGetValue(key, out KeyCode code) ? code : null;

	/// <summary>Converts a <see cref="KeyCode"/> to the matching WPF key.</summary>
	/// <param name="keyCode">The key code to convert.</param>
	/// <returns>The matching WPF key.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="keyCode"/> is not a defined <see cref="KeyCode"/> member.</exception>
	public static Key ToWpfKey(KeyCode keyCode)
	{
		return s_codeToKey.TryGetValue(keyCode, out Key key)
			? key
			: throw new ArgumentOutOfRangeException(nameof(keyCode), keyCode, "The key code must be a defined KeyCode member.");
	}

	/// <summary>Converts <see cref="KeyModifiers"/> flags to the matching WPF <see cref="ModifierKeys"/> flags.</summary>
	/// <param name="modifiers">The modifier flags to convert.</param>
	/// <returns>The matching WPF modifier flags.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="modifiers"/> sets bits outside the defined <see cref="KeyModifiers"/> flags.
	/// </exception>
	public static ModifierKeys ToWpfModifierKeys(KeyModifiers modifiers)
	{
		if (modifiers.HasUndefinedFlags())
			throw new ArgumentOutOfRangeException(nameof(modifiers), modifiers, "The modifier flags must be defined KeyModifiers flags.");

		ModifierKeys result = ModifierKeys.None;

		if ((modifiers & KeyModifiers.Alt) != KeyModifiers.None)
			result |= ModifierKeys.Alt;

		if ((modifiers & KeyModifiers.Control) != KeyModifiers.None)
			result |= ModifierKeys.Control;

		if ((modifiers & KeyModifiers.Shift) != KeyModifiers.None)
			result |= ModifierKeys.Shift;

		if ((modifiers & KeyModifiers.Meta) != KeyModifiers.None)
			result |= ModifierKeys.Windows;

		return result;
	}

	/// <summary>Converts WPF <see cref="ModifierKeys"/> flags to the matching <see cref="KeyModifiers"/> flags.</summary>
	/// <param name="modifiers">The WPF modifier flags to convert.</param>
	/// <returns>The matching modifier flags.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="modifiers"/> sets bits outside the defined <see cref="ModifierKeys"/> flags.
	/// </exception>
	public static KeyModifiers ToKeyModifiers(ModifierKeys modifiers)
	{
		if ((modifiers & ~DefinedModifierKeys) != 0)
			throw new ArgumentOutOfRangeException(nameof(modifiers), modifiers, "The modifier flags must be defined ModifierKeys flags.");

		KeyModifiers result = KeyModifiers.None;

		if ((modifiers & ModifierKeys.Alt) != 0)
			result |= KeyModifiers.Alt;

		if ((modifiers & ModifierKeys.Control) != 0)
			result |= KeyModifiers.Control;

		if ((modifiers & ModifierKeys.Shift) != 0)
			result |= KeyModifiers.Shift;

		if ((modifiers & ModifierKeys.Windows) != 0)
			result |= KeyModifiers.Meta;

		return result;
	}
}

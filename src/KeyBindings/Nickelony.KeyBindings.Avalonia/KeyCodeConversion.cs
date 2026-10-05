using System.Collections.Frozen;
using Avalonia.Input;
using AvaloniaKeyModifiers = Avalonia.Input.KeyModifiers;

namespace Nickelony.KeyBindings.Avalonia;

/// <summary>
/// Converts Avalonia <see cref="Key"/> values and modifier flags to the toolkit-neutral <see cref="KeyCode"/> and <see cref="KeyModifiers"/> values.
/// </summary>
/// <remarks>
/// <include file="../../../shared/docs/KeyCodeConversion.xml" path="doc/members/member[@name='KeyCodeConversion.Remarks']/*"/>
/// </remarks>
public static class KeyCodeConversion
{
	private const AvaloniaKeyModifiers DefinedModifierKeys =
		AvaloniaKeyModifiers.Alt | AvaloniaKeyModifiers.Control | AvaloniaKeyModifiers.Shift | AvaloniaKeyModifiers.Meta;

	private static readonly FrozenDictionary<Key, KeyCode> s_keyToCode = KeyCodeConversionTable.Pairs.ToFrozenDictionary(
		pair => pair.Key,
		pair => pair.Code
	);

	/// <summary>Converts an Avalonia key to the matching <see cref="KeyCode"/>.</summary>
	/// <param name="key">The Avalonia key to convert.</param>
	/// <returns>The matching key code, or <see langword="null"/> when the key cannot be the primary key of a binding.</returns>
	public static KeyCode? ToKeyCode(Key key) => s_keyToCode.TryGetValue(key, out KeyCode code) ? code : null;

	/// <summary>Converts Avalonia <c>Avalonia.Input.KeyModifiers</c> flags to the matching <see cref="KeyModifiers"/> flags.</summary>
	/// <param name="modifiers">The Avalonia modifier flags to convert.</param>
	/// <returns>The matching modifier flags.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="modifiers"/> sets bits outside the defined Avalonia <c>Avalonia.Input.KeyModifiers</c> flags.
	/// </exception>
	public static KeyModifiers ToKeyModifiers(AvaloniaKeyModifiers modifiers)
	{
		if ((modifiers & ~DefinedModifierKeys) != 0)
			throw new ArgumentOutOfRangeException(nameof(modifiers), modifiers, "The modifier flags must be defined Avalonia modifier flags.");

		KeyModifiers result = KeyModifiers.None;

		if ((modifiers & AvaloniaKeyModifiers.Alt) != 0)
			result |= KeyModifiers.Alt;

		if ((modifiers & AvaloniaKeyModifiers.Control) != 0)
			result |= KeyModifiers.Control;

		if ((modifiers & AvaloniaKeyModifiers.Shift) != 0)
			result |= KeyModifiers.Shift;

		if ((modifiers & AvaloniaKeyModifiers.Meta) != 0)
			result |= KeyModifiers.Meta;

		return result;
	}
}

using System.Collections.Frozen;

namespace Nickelony.KeyBindings;

/// <summary>Represents one stroke: a <see cref="KeyCode"/> pressed while the <see cref="KeyModifiers"/> is held.</summary>
/// <remarks>
/// Instances created through the constructor always name a defined <see cref="KeyCode"/> member; the <see langword="default"/> value of this record
/// struct does not, so <see cref="IsInitialized"/> reports <see langword="false"/> for it.
/// </remarks>
public readonly record struct KeyStroke
{
	/// <summary>The modifier names the typed-text grammar accepts.</summary>
	/// <remarks>
	/// Aliases are accepted for typed input only and are never emitted.
	/// </remarks>
	private static readonly FrozenDictionary<string, KeyModifiers> s_modifierNames = new Dictionary<string, KeyModifiers>(
		StringComparer.OrdinalIgnoreCase
	)
	{
		["alt"] = KeyModifiers.Alt,
		["ctrl"] = KeyModifiers.Control,
		["control"] = KeyModifiers.Control,
		["shift"] = KeyModifiers.Shift,
		["cmd"] = KeyModifiers.Meta,
		["command"] = KeyModifiers.Meta,
		["meta"] = KeyModifiers.Meta,
		["super"] = KeyModifiers.Meta,
		["win"] = KeyModifiers.Meta,
		["windows"] = KeyModifiers.Meta,
	}.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// The key names the typed-text grammar accepts: every <see cref="KeyCode"/> member name plus the main-row digit characters the default
	/// formatter renders.
	/// </summary>
	private static readonly FrozenDictionary<string, KeyCode> s_keyNames = CreateKeyNames();

	private static FrozenDictionary<string, KeyCode> CreateKeyNames()
	{
		var names = new Dictionary<string, KeyCode>(StringComparer.OrdinalIgnoreCase);

		foreach (KeyCode keyCode in Enum.GetValues<KeyCode>())
			names[Enum.GetName(keyCode)!] = keyCode;

		// The default formatter renders the main-row digits as their character, so the grammar accepts
		// the form the user sees in addition to the member name.
		names["0"] = KeyCode.D0;
		names["1"] = KeyCode.D1;
		names["2"] = KeyCode.D2;
		names["3"] = KeyCode.D3;
		names["4"] = KeyCode.D4;
		names["5"] = KeyCode.D5;
		names["6"] = KeyCode.D6;
		names["7"] = KeyCode.D7;
		names["8"] = KeyCode.D8;
		names["9"] = KeyCode.D9;

		return names.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
	}

	/// <param name="key">The primary key; must be a defined <see cref="KeyCode"/> member.</param>
	/// <param name="modifiers">The modifier flags pressed with the key; must not set undefined flags.</param>
	/// <exception cref="ArgumentException">
	/// <paramref name="key"/> is not a defined <see cref="KeyCode"/> member, or <paramref name="modifiers"/> sets bits outside the defined
	/// <see cref="KeyModifiers"/> flags.
	/// </exception>
	public KeyStroke(KeyCode key, KeyModifiers modifiers)
	{
		if (!Enum.IsDefined<KeyCode>(key))
			throw new ArgumentException("A key stroke must use a defined KeyCode member.", nameof(key));

		if (modifiers.HasUndefinedFlags())
			throw new ArgumentException("A key stroke must not use undefined modifier flags.", nameof(modifiers));

		Key = key;
		Modifiers = modifiers;
	}

	public KeyCode Key { get; }

	public KeyModifiers Modifiers { get; }

	public bool IsInitialized => Key != default;

	/// <summary>Tries to parse a single stroke from text a user typed, such as <c>Ctrl+S</c> or <c>ctrl + s</c>.</summary>
	/// <param name="text">The text to parse.</param>
	/// <param name="keyStroke">
	/// The parsed stroke when the method returns <see langword="true"/>; otherwise, the <see langword="default"/> stroke.
	/// </param>
	/// <returns><see langword="true"/> when the text is a single valid stroke; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// This is an input
	/// grammar for typed text, not the persisted override format, and a stroke round-trips through <c>KeyDisplayTextFormatter</c>. The stroke grammar
	/// is in the <see href="https://github.com/Nickelony/LanguageServer/blob/main/docs/KeyBindingsGuide.md">key bindings guide</see>.
	/// </remarks>
	public static bool TryParse(string? text, out KeyStroke keyStroke)
	{
		keyStroke = default;

		if (string.IsNullOrWhiteSpace(text))
			return false;

		string[] parts = text.Split('+', StringSplitOptions.TrimEntries);

		KeyModifiers modifiers = KeyModifiers.None;
		KeyCode key = default;
		bool hasKey = false;

		foreach (string part in parts)
		{
			if (s_modifierNames.TryGetValue(part, out KeyModifiers modifier))
			{
				// A modifier that follows the key is not a stroke; modifiers must precede the key.
				if (hasKey)
					return false;

				modifiers |= modifier;
				continue;
			}

			// Only the final element may be the key, so a stroke never names two keys.
			if (hasKey || !s_keyNames.TryGetValue(part, out key))
				return false;

			hasKey = true;
		}

		if (!hasKey)
			return false;

		keyStroke = new KeyStroke(key, modifiers);
		return true;
	}
}

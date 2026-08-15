using System.Collections.Frozen;

namespace Nickelony.KeyBindings;

/// <summary>
/// Represents a single key press: a <see cref="KeyCode"/> and the <see cref="KeyModifierSet"/> held
/// with it.
/// </summary>
/// <remarks>
/// Instances created through the constructor always name a defined <see cref="KeyCode"/> member; the
/// <see langword="default"/> value of this record struct does not, so <see cref="IsInitialized"/>
/// reports <see langword="false"/> for it.
/// </remarks>
public readonly record struct KeyCombo
{
	/// <summary>
	/// The modifier names the typed-text grammar accepts: the canonical member names plus the
	/// operating-system vocabulary for the Meta key (<c>cmd</c>/<c>command</c>, <c>super</c>, and
	/// <c>win</c>/<c>windows</c>), which is accepted for typed input only and never emitted.
	/// </summary>
	private static readonly FrozenDictionary<string, KeyModifierSet> s_modifierNames =
		new Dictionary<string, KeyModifierSet>(StringComparer.OrdinalIgnoreCase)
		{
			["alt"] = KeyModifierSet.Alt,
			["ctrl"] = KeyModifierSet.Control,
			["control"] = KeyModifierSet.Control,
			["shift"] = KeyModifierSet.Shift,
			["cmd"] = KeyModifierSet.Meta,
			["command"] = KeyModifierSet.Meta,
			["meta"] = KeyModifierSet.Meta,
			["super"] = KeyModifierSet.Meta,
			["win"] = KeyModifierSet.Meta,
			["windows"] = KeyModifierSet.Meta
		}.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// The key names the typed-text grammar accepts: every <see cref="KeyCode"/> member name plus the
	/// main-row digit characters the default formatter renders.
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

	/// <summary>
	/// Initializes a new instance of the <see cref="KeyCombo"/> struct.
	/// </summary>
	/// <param name="key">The primary key; must be a defined <see cref="KeyCode"/> member.</param>
	/// <param name="modifiers">The modifier flags pressed with the key; must not set undefined flags.</param>
	/// <exception cref="ArgumentException">
	/// <paramref name="key"/> is not a defined <see cref="KeyCode"/> member, or
	/// <paramref name="modifiers"/> sets bits outside the defined <see cref="KeyModifierSet"/> flags.
	/// </exception>
	public KeyCombo(KeyCode key, KeyModifierSet modifiers)
	{
		if (!Enum.IsDefined<KeyCode>(key))
			throw new ArgumentException("A key combo must use a defined KeyCode member.", nameof(key));

		if ((modifiers & ~KeyModifierMasks.Defined) != KeyModifierSet.None)
			throw new ArgumentException("A key combo must not use undefined modifier flags.", nameof(modifiers));

		Key = key;
		Modifiers = modifiers;
	}

	/// <summary>
	/// Gets the primary key.
	/// </summary>
	public KeyCode Key { get; }

	/// <summary>
	/// Gets the modifier flags associated with the combo.
	/// </summary>
	public KeyModifierSet Modifiers { get; }

	/// <summary>
	/// Gets a value indicating whether <see cref="Key"/> names a defined <see cref="KeyCode"/> member.
	/// </summary>
	public bool IsInitialized => Key != default;

	/// <summary>
	/// Tries to parse a single stroke from text a user typed, such as <c>Ctrl+S</c> or <c>ctrl + s</c>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is an input grammar for typed text, not the persisted override format. A stroke is a
	/// <see cref="KeyCode"/> name with optional <see cref="KeyModifierSet"/> joined by <c>+</c>; matching is
	/// case-insensitive, whitespace around <c>+</c> is ignored, a repeated modifier is tolerated, and the
	/// modifiers also accept the operating-system aliases <c>cmd</c>, <c>command</c>, <c>super</c>,
	/// <c>win</c> and <c>windows</c> for <see cref="KeyModifierSet.Meta"/>. The main-row digits are accepted
	/// as <c>0</c>-<c>9</c> as well as <c>D0</c>-<c>D9</c>.
	/// </para>
	/// <para>
	/// The text parsed here is what <see cref="KeyDisplayTextFormatter"/> renders, so a stroke
	/// round-trips through that formatter's display text and back. Presentation conventions of other
	/// formatters (such as <c>Esc</c> or <c>Num 5</c>) are not part of this grammar, and neither is a
	/// two-stroke sequence - use <see cref="KeyChord.TryParse(string?, out KeyChord)"/> for a chord.
	/// </para>
	/// </remarks>
	/// <param name="text">The text to parse.</param>
	/// <param name="keyCombo">
	/// The parsed stroke when the method returns <see langword="true"/>; otherwise, the
	/// <see langword="default"/> combo.
	/// </param>
	/// <returns><see langword="true"/> when the text is a single valid stroke; otherwise, <see langword="false"/>.</returns>
	public static bool TryParse(string? text, out KeyCombo keyCombo)
	{
		keyCombo = default;

		if (string.IsNullOrWhiteSpace(text))
			return false;

		string[] parts = text.Split('+', StringSplitOptions.TrimEntries);

		KeyModifierSet modifiers = KeyModifierSet.None;
		KeyCode key = default;
		bool hasKey = false;

		foreach (string part in parts)
		{
			if (s_modifierNames.TryGetValue(part, out KeyModifierSet modifier))
			{
				// A modifier that follows the key is not a stroke, even though the key precedes nothing.
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

		keyCombo = new KeyCombo(key, modifiers);
		return true;
	}
}

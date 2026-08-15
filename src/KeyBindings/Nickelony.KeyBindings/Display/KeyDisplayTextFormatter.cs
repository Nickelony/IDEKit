using System.Text;

namespace Nickelony.KeyBindings;

/// <summary>
/// Renders a key combo or chord with the canonical <see cref="KeyCode"/> and <see cref="KeyModifierSet"/>
/// names, such as <c>Control+Shift+S</c> or <c>Meta+Enter</c>.
/// </summary>
/// <remarks>
/// <para>
/// This formatter is the package default and the base for every platform-convention formatter. It
/// contains no platform conventions, so the text is predictable on every host; a host that targets a
/// specific desktop convention or localizes shortcut text either supplies its own
/// <see cref="IKeyDisplayTextFormatter"/> or derives from this type and overrides its text hooks
/// (<see cref="GetKeyText"/>, <see cref="GetModifierText"/>, and, for a convention that differs,
/// <see cref="GetModifierOrder"/>, <see cref="GetModifierSeparator"/>, and
/// <see cref="GetStrokeSeparator"/>). A formatter that renders through a toolkit converter, such as the
/// WPF <c>KeyGestureDisplayTextFormatter</c>, overrides <see cref="GetComboText"/> instead, so the
/// stroke sequencing and the uninitialized-combo guard stay here.
/// </para>
/// <para>
/// Modifiers render in the order Control, Shift, Alt, Meta, separated with <c>+</c>, and main-row
/// digits render as <c>0</c>-<c>9</c>. Every other key renders as its <see cref="KeyCode"/> member
/// name. A chord's stroke texts are joined with <c>, </c>, so <c>Control+K, S</c> is two strokes,
/// matching a common desktop convention.
/// </para>
/// </remarks>
public class KeyDisplayTextFormatter : IKeyDisplayTextFormatter
{
	/// <summary>
	/// The order the base formatter renders modifiers in. It is the default for
	/// <see cref="GetModifierOrder"/>, which a platform formatter overrides when its convention orders
	/// the modifiers differently. It also lists every defined modifier flag, so a formatter that
	/// shortens <see cref="GetModifierOrder"/> still renders the flags the order omits.
	/// </summary>
	private static readonly KeyModifierSet[] s_modifierOrder =
	[
		KeyModifierSet.Control,
		KeyModifierSet.Shift,
		KeyModifierSet.Alt,
		KeyModifierSet.Meta
	];

	/// <summary>
	/// Gets the shared instance of the formatter.
	/// </summary>
	public static KeyDisplayTextFormatter Default { get; } = new();

	/// <summary>
	/// Initializes a new instance of the <see cref="KeyDisplayTextFormatter"/> class.
	/// </summary>
	protected KeyDisplayTextFormatter()
	{
	}

	/// <inheritdoc/>
	public string GetDisplayText(KeyCombo keyCombo)
	{
		if (!keyCombo.IsInitialized)
			throw new ArgumentException("The key combo must be initialized.", nameof(keyCombo));

		return GetComboText(keyCombo);
	}

	/// <inheritdoc/>
	public string GetDisplayText(KeyChord keyChord)
	{
		if (!keyChord.IsInitialized)
			throw new ArgumentException("The key chord must be initialized.", nameof(keyChord));

		var parts = new List<string>(keyChord.StrokeCount);

		foreach (KeyCombo stroke in keyChord.Strokes)
			parts.Add(GetDisplayText(stroke));

		return string.Join(GetStrokeSeparator(), parts);
	}

	/// <summary>
	/// Returns the display text of a single, initialized key combo.
	/// </summary>
	/// <remarks>
	/// The base implementation composes the modifier prefix with the key text. A formatter that renders
	/// through a toolkit converter overrides this member; <see cref="GetDisplayText(KeyCombo)"/> owns the
	/// uninitialized-combo guard and <see cref="GetDisplayText(KeyChord)"/> owns the chord sequencing, so an
	/// override only has to produce one combo's text.
	/// </remarks>
	/// <param name="keyCombo">The initialized key combo to render.</param>
	/// <returns>The display text of the combo.</returns>
	protected virtual string GetComboText(KeyCombo keyCombo)
		=> GetModifierPrefix(keyCombo.Modifiers) + GetKeyText(keyCombo.Key);

	/// <summary>
	/// Returns the text a single key renders as.
	/// </summary>
	/// <remarks>
	/// The base implementation renders the main-row digits as <c>0</c>-<c>9</c> and every other key as
	/// its <see cref="KeyCode"/> member name.
	/// </remarks>
	/// <param name="key">The key to render.</param>
	/// <returns>The key text.</returns>
	protected virtual string GetKeyText(KeyCode key)
		=> key switch
		{
			KeyCode.D0 => "0",
			KeyCode.D1 => "1",
			KeyCode.D2 => "2",
			KeyCode.D3 => "3",
			KeyCode.D4 => "4",
			KeyCode.D5 => "5",
			KeyCode.D6 => "6",
			KeyCode.D7 => "7",
			KeyCode.D8 => "8",
			KeyCode.D9 => "9",
			_ => key.ToString()
		};

	/// <summary>
	/// Returns the text a single modifier renders as.
	/// </summary>
	/// <remarks>
	/// The base implementation returns the <see cref="KeyModifierSet"/> member name. The formatter calls
	/// this member once for each modifier flag it renders, and each flag renders as a single text.
	/// </remarks>
	/// <param name="modifier">The single modifier flag to render.</param>
	/// <returns>The modifier text, or an empty string for a value the formatter does not render.</returns>
	protected virtual string GetModifierText(KeyModifierSet modifier)
		=> modifier switch
		{
			KeyModifierSet.Control => nameof(KeyModifierSet.Control),
			KeyModifierSet.Shift => nameof(KeyModifierSet.Shift),
			KeyModifierSet.Alt => nameof(KeyModifierSet.Alt),
			KeyModifierSet.Meta => nameof(KeyModifierSet.Meta),
			_ => string.Empty
		};

	/// <summary>
	/// Returns the order the modifiers render in.
	/// </summary>
	/// <remarks>
	/// The base implementation renders Control, Shift, Alt, Meta. A platform convention that orders the
	/// modifiers differently (for example the macOS Control, Option, Shift, Command order) overrides this
	/// member. The flags the combo carries do not change; only the order in which their text appears does.
	/// </remarks>
	/// <returns>The modifier flags in render order.</returns>
	protected virtual IReadOnlyList<KeyModifierSet> GetModifierOrder() => s_modifierOrder;

	/// <summary>
	/// Returns the text appended after each rendered modifier text.
	/// </summary>
	/// <remarks>
	/// The base implementation returns <c>+</c>. A platform convention that joins the modifiers and the
	/// key without a separator (for example the macOS glyph text) overrides this member to return an
	/// empty string.
	/// </remarks>
	/// <returns>The separator after each modifier text, or an empty string for none.</returns>
	protected virtual string GetModifierSeparator() => "+";

	/// <summary>
	/// Returns the text placed between the strokes of a chord.
	/// </summary>
	/// <remarks>
	/// The base implementation returns <c>, </c>. A platform convention that separates a chord's
	/// strokes differently overrides this member.
	/// </remarks>
	/// <returns>The separator between stroke texts.</returns>
	protected virtual string GetStrokeSeparator() => ", ";

	/// <summary>
	/// Builds the leading modifier text of a combo, such as <c>Control+Shift+</c>.
	/// </summary>
	/// <param name="modifiers">The combo's modifier flags.</param>
	/// <returns>The modifier prefix, ending in the modifier separator, or an empty string when no modifier renders.</returns>
	private string GetModifierPrefix(KeyModifierSet modifiers)
	{
		// A combo with no rendered modifier (the common case) skips the builder entirely.
		if (modifiers == KeyModifierSet.None)
			return string.Empty;

		// The prefix is assembled with a builder instead of repeated concatenation: a combo can carry up
		// to the four rendered modifiers, and a host renders the display text of every binding it shows.
		var prefix = new StringBuilder();
		KeyModifierSet rendered = KeyModifierSet.None;

		foreach (KeyModifierSet modifier in GetModifierOrder())
		{
			if ((modifiers & modifier) == KeyModifierSet.None)
				continue;

			prefix.Append(GetModifierText(modifier)).Append(GetModifierSeparator());
			rendered |= modifier;
		}

		// A host may shorten GetModifierOrder() to the flags its convention names; a defined flag the
		// order omits is still rendered, in the base order, so a modifier is never dropped from the text.
		KeyModifierSet remaining = modifiers & ~rendered;

		if (remaining == KeyModifierSet.None)
			return prefix.ToString();

		foreach (KeyModifierSet modifier in s_modifierOrder)
		{
			if ((remaining & modifier) == KeyModifierSet.None)
				continue;

			prefix.Append(GetModifierText(modifier)).Append(GetModifierSeparator());
			remaining &= ~modifier;
		}

		return prefix.ToString();
	}
}

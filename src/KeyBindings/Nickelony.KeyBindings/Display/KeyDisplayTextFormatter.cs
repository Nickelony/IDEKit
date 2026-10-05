using System.Text;

namespace Nickelony.KeyBindings;

/// <summary>
/// Renders a key stroke or chord with the canonical <see cref="KeyCode"/> and <see cref="KeyModifiers"/> names, such as <c>Control+Shift+S</c>
/// or <c>Meta+Enter</c>.
/// </summary>
/// <remarks>
/// The base for every desktop-convention formatter. A formatter that renders through a toolkit converter overrides <see cref="GetStrokeText"/>,
/// so the stroke sequencing and the uninitialized-stroke guard stay in the base.
/// </remarks>
public class KeyDisplayTextFormatter : IKeyDisplayTextFormatter
{
	/// <summary>The order the base formatter renders modifiers in.</summary>
	/// <remarks>
	/// It lists every defined modifier flag, so a formatter that shortens <see cref="GetModifierOrder"/> still renders the flags the order omits.
	/// </remarks>
	private static readonly KeyModifiers[] s_modifierOrder = [KeyModifiers.Control, KeyModifiers.Shift, KeyModifiers.Alt, KeyModifiers.Meta];

	/// <summary>The shared instance of the formatter.</summary>
	public static KeyDisplayTextFormatter Default { get; } = new();

	protected KeyDisplayTextFormatter() { }

	/// <inheritdoc/>
	public string GetDisplayText(KeyStroke keyStroke)
	{
		if (!keyStroke.IsInitialized)
			throw new ArgumentException("The key stroke must be initialized.", nameof(keyStroke));

		return GetStrokeText(keyStroke);
	}

	/// <inheritdoc/>
	public string GetDisplayText(KeyChord keyChord)
	{
		if (!keyChord.IsInitialized)
			throw new ArgumentException("The key chord must be initialized.", nameof(keyChord));

		var parts = new List<string>(keyChord.StrokeCount);

		foreach (KeyStroke stroke in keyChord.Strokes)
			parts.Add(GetDisplayText(stroke));

		return string.Join(GetStrokeSeparator(), parts);
	}

	/// <summary>Returns the display text of a single, initialized key stroke.</summary>
	/// <param name="keyStroke">The initialized key stroke to render.</param>
	/// <returns>The display text of the stroke.</returns>
	/// <remarks>
	/// The base implementation composes the modifier prefix with the key text. A formatter that renders through a toolkit converter
	/// overrides this member; <see cref="GetDisplayText(KeyStroke)"/> owns the uninitialized-stroke guard and <see cref="GetDisplayText(KeyChord)"/>
	/// owns the chord sequencing, so an override only has to produce one stroke's text.
	/// </remarks>
	protected virtual string GetStrokeText(KeyStroke keyStroke) => GetModifierPrefix(keyStroke.Modifiers) + GetKeyText(keyStroke.Key);

	/// <summary>Returns the text a single key renders as.</summary>
	/// <param name="key">The key to render.</param>
	/// <returns>The key text.</returns>
	/// <remarks>
	/// The base implementation renders the main-row digits as <c>0</c>-<c>9</c> and every other key as its <see cref="KeyCode"/> member name.
	/// </remarks>
	protected virtual string GetKeyText(KeyCode key)
	{
		return key switch
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
			_ => key.ToString(),
		};
	}

	/// <summary>Returns the text a single modifier renders as.</summary>
	/// <param name="modifier">The single modifier flag to render.</param>
	/// <returns>The modifier text, or an empty string for a value the formatter does not render.</returns>
	/// <remarks>
	/// The base implementation returns the <see cref="KeyModifiers"/> member name. The formatter calls this member once for each modifier
	/// flag it renders, and each flag renders as a single text.
	/// </remarks>
	protected virtual string GetModifierText(KeyModifiers modifier)
	{
		return modifier switch
		{
			KeyModifiers.Control => nameof(KeyModifiers.Control),
			KeyModifiers.Shift => nameof(KeyModifiers.Shift),
			KeyModifiers.Alt => nameof(KeyModifiers.Alt),
			KeyModifiers.Meta => nameof(KeyModifiers.Meta),
			_ => string.Empty,
		};
	}

	/// <summary>Returns the order the modifiers render in.</summary>
	/// <returns>The modifier flags in render order.</returns>
	/// <remarks>
	/// The base implementation renders Control, Shift, Alt, Meta. A desktop convention that orders the modifiers
	/// differently (for example the macOS Control, Option, Shift, Command order) overrides this member. The flags the stroke carries do not change;
	/// only the order in which their text appears does.
	/// </remarks>
	protected virtual IReadOnlyList<KeyModifiers> GetModifierOrder() => s_modifierOrder;

	/// <summary>Returns the text appended after each rendered modifier text.</summary>
	/// <returns>The separator after each modifier text, or an empty string for none.</returns>
	/// <remarks>
	/// The base implementation returns <c>+</c>. A desktop convention that joins the modifiers and the key without a separator
	/// (for example the macOS glyph text) overrides this member to return an empty string.
	/// </remarks>
	protected virtual string GetModifierSeparator() => "+";

	/// <summary>Returns the text placed between the strokes of a chord.</summary>
	/// <returns>The separator between stroke texts.</returns>
	/// <remarks>
	/// The base implementation returns <c>, </c>. A desktop convention that separates a chord's strokes differently overrides this member.
	/// </remarks>
	protected virtual string GetStrokeSeparator() => ", ";

	/// <summary>Builds the leading modifier text of a stroke, such as <c>Control+Shift+</c>.</summary>
	/// <param name="modifiers">The stroke's modifier flags.</param>
	/// <returns>The modifier prefix, ending in the modifier separator, or an empty string when no modifier renders.</returns>
	private string GetModifierPrefix(KeyModifiers modifiers)
	{
		// A stroke with no rendered modifier (the common case) skips the builder entirely.
		if (modifiers == KeyModifiers.None)
			return string.Empty;

		// A stroke can carry up to four rendered modifiers, so the prefix is assembled with a builder.
		var prefix = new StringBuilder();
		KeyModifiers rendered = KeyModifiers.None;

		foreach (KeyModifiers modifier in GetModifierOrder())
		{
			if ((modifiers & modifier) == KeyModifiers.None)
				continue;

			prefix.Append(GetModifierText(modifier)).Append(GetModifierSeparator());
			rendered |= modifier;
		}

		// A host may shorten GetModifierOrder() to the flags its convention names; a defined flag the
		// order omits is still rendered, in the base order, so a modifier is never dropped from the text.
		KeyModifiers remaining = modifiers & ~rendered;

		if (remaining == KeyModifiers.None)
			return prefix.ToString();

		foreach (KeyModifiers modifier in s_modifierOrder)
		{
			if ((remaining & modifier) == KeyModifiers.None)
				continue;

			prefix.Append(GetModifierText(modifier)).Append(GetModifierSeparator());
			remaining &= ~modifier;
		}

		return prefix.ToString();
	}
}

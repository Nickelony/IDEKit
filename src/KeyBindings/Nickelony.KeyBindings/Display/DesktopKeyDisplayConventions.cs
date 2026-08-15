using System.Collections.Frozen;

namespace Nickelony.KeyBindings;

/// <summary>
/// Describes the text conventions a desktop-style <see cref="DesktopKeyDisplayTextFormatter"/> renders:
/// the modifier labels and their render order, the separators between modifiers and between chord
/// strokes, the numpad prefix, and the per-key text overrides.
/// </summary>
/// <remarks>
/// <para>
/// A convention is a value the host supplies when it creates a
/// <see cref="DesktopKeyDisplayTextFormatter"/>. The package ships the <see cref="Windows"/>,
/// <see cref="Linux"/>, and <see cref="MacOS"/> presets and never inspects the operating system, so
/// the host decides which desktop convention it targets, exactly as it does for
/// <see cref="KeyBindingPlatformProfile"/>. A bare <c>new DesktopKeyDisplayConventions()</c> carries the
/// canonical neutral labels (<c>Control</c>, <c>Meta</c>, <c>Numpad </c>), so a host that wants a desktop
/// convention starts from the <see cref="Windows"/>, <see cref="Linux"/>, or <see cref="MacOS"/> preset.
/// </para>
/// <para>
/// The type and its formatter stay in this toolkit-free package because the data is desktop-OS convention
/// data, not a toolkit dependency: the package references no UI toolkit, so the conventions are defined
/// once for every host, and the two toolkit adapters would otherwise each have to duplicate them. The
/// earlier <c>Nickelony.KeyBindings.Windows</c>, <c>.Linux</c>, and <c>.MacOS</c> split packages were
/// deliberately retired in favour of this single value type.
/// </para>
/// <para>
/// The properties use the <c>with</c>-configuration idiom, so a host can start from a preset and adjust
/// one label: <c>DesktopKeyDisplayConventions.Windows with { MetaLabel = "Cmd" }</c>.
/// </para>
/// <para>
/// Equality compares every member by value, including the <see cref="ModifierOrder"/> sequence and the
/// <see cref="KeyTextOverrides"/> entries, so two conventions that were configured the same way are
/// equal and a convention can be used as a cache or dictionary key.
/// </para>
/// </remarks>
public sealed record DesktopKeyDisplayConventions
{
	/// <summary>
	/// The text overrides shared by the Windows and Linux conventions: the US-layout punctuation glyphs
	/// and <c>Esc</c>. A key that is not listed renders as its canonical <see cref="KeyCode"/> member
	/// name, and the numpad keys render with <see cref="NumpadPrefix"/>.
	/// </summary>
	private static readonly FrozenDictionary<KeyCode, string> s_desktopKeyText = new Dictionary<KeyCode, string>
	{
		[KeyCode.Semicolon] = ";",
		[KeyCode.Equals] = "=",
		[KeyCode.Comma] = ",",
		[KeyCode.Minus] = "-",
		[KeyCode.Period] = ".",
		[KeyCode.Slash] = "/",
		[KeyCode.Grave] = "`",
		[KeyCode.LeftBracket] = "[",
		[KeyCode.RightBracket] = "]",
		[KeyCode.Backslash] = "\\",
		[KeyCode.Apostrophe] = "'",
		[KeyCode.Escape] = "Esc"
	}.ToFrozenDictionary();

	/// <summary>
	/// The text overrides of the macOS convention: the desktop overrides plus the macOS keyboard words
	/// and the arrow glyphs.
	/// </summary>
	private static readonly FrozenDictionary<KeyCode, string> s_macOSKeyText = new Dictionary<KeyCode, string>(s_desktopKeyText)
	{
		[KeyCode.Enter] = "Return",
		[KeyCode.Backspace] = "Delete",
		[KeyCode.Delete] = "Forward Delete",
		[KeyCode.PageUp] = "Page Up",
		[KeyCode.PageDown] = "Page Down",
		[KeyCode.Left] = "←",
		[KeyCode.Up] = "↑",
		[KeyCode.Right] = "→",
		[KeyCode.Down] = "↓"
	}.ToFrozenDictionary();

	/// <summary>
	/// Gets the Windows desktop convention: Ctrl, Shift, Alt, Win, with <c>+</c> between modifiers and
	/// <c>, </c> between the strokes of a chord.
	/// </summary>
	public static DesktopKeyDisplayConventions Windows { get; } = new()
	{
		ControlLabel = "Ctrl",
		MetaLabel = "Win",
		NumpadPrefix = "Num ",
		KeyTextOverrides = s_desktopKeyText
	};

	/// <summary>
	/// Gets the Linux desktop convention: Ctrl, Shift, Alt, Super, with <c>+</c> between modifiers and
	/// <c>, </c> between the strokes of a chord.
	/// </summary>
	public static DesktopKeyDisplayConventions Linux { get; } = new()
	{
		ControlLabel = "Ctrl",
		MetaLabel = "Super",
		NumpadPrefix = "Num ",
		KeyTextOverrides = s_desktopKeyText
	};

	/// <summary>
	/// Gets the macOS desktop convention: the glyph modifiers Control <c>⌃</c>, Option <c>⌥</c> (the Alt
	/// key), Shift <c>⇧</c>, and Command <c>⌘</c> (the Meta key), rendered in the Control, Option, Shift,
	/// Command order with no separator between them.
	/// </summary>
	public static DesktopKeyDisplayConventions MacOS { get; } = new()
	{
		ControlLabel = "⌃",
		ShiftLabel = "⇧",
		AltLabel = "⌥",
		MetaLabel = "⌘",
		NumpadPrefix = "Num ",
		ModifierOrder = [KeyModifierSet.Control, KeyModifierSet.Alt, KeyModifierSet.Shift, KeyModifierSet.Meta],
		ModifierSeparator = string.Empty,
		StrokeSeparator = " ",
		KeyTextOverrides = s_macOSKeyText
	};

	/// <summary>
	/// Gets or initializes the label the Control modifier renders as.
	/// </summary>
	public string ControlLabel { get; init; } = "Control";

	/// <summary>
	/// Gets or initializes the label the Shift modifier renders as.
	/// </summary>
	public string ShiftLabel { get; init; } = "Shift";

	/// <summary>
	/// Gets or initializes the label the Alt modifier renders as.
	/// </summary>
	public string AltLabel { get; init; } = "Alt";

	/// <summary>
	/// Gets or initializes the label the Meta modifier renders as.
	/// </summary>
	public string MetaLabel { get; init; } = "Meta";

	/// <summary>
	/// Gets or initializes the order the modifiers render in.
	/// </summary>
	public IReadOnlyList<KeyModifierSet> ModifierOrder { get; init; } =
	[
		KeyModifierSet.Control,
		KeyModifierSet.Shift,
		KeyModifierSet.Alt,
		KeyModifierSet.Meta
	];

	/// <summary>
	/// Gets or initializes the text appended after each rendered modifier label.
	/// </summary>
	public string ModifierSeparator { get; init; } = "+";

	/// <summary>
	/// Gets or initializes the text placed between the strokes of a chord.
	/// </summary>
	public string StrokeSeparator { get; init; } = ", ";

	/// <summary>
	/// Gets or initializes the prefix the numpad keys render with.
	/// </summary>
	public string NumpadPrefix { get; init; } = "Numpad ";

	/// <summary>
	/// Gets or initializes the per-key text overrides. A key that is not listed renders as its canonical
	/// <see cref="KeyCode"/> member name, except for the numpad keys, which the
	/// <see cref="DesktopKeyDisplayTextFormatter"/> renders with <see cref="NumpadPrefix"/>.
	/// </summary>
	public IReadOnlyDictionary<KeyCode, string> KeyTextOverrides { get; init; } = FrozenDictionary<KeyCode, string>.Empty;

	/// <summary>
	/// Determines whether two conventions render the same text.
	/// </summary>
	/// <param name="other">The convention to compare with.</param>
	/// <returns>
	/// <see langword="true"/> when every member is equal, including the <see cref="ModifierOrder"/>
	/// sequence and the <see cref="KeyTextOverrides"/> entries; otherwise <see langword="false"/>.
	/// </returns>
	public bool Equals(DesktopKeyDisplayConventions? other)
	{
		if (other is null)
			return false;

		if (ReferenceEquals(this, other))
			return true;

		return ControlLabel == other.ControlLabel
			&& ShiftLabel == other.ShiftLabel
			&& AltLabel == other.AltLabel
			&& MetaLabel == other.MetaLabel
			&& ModifierSeparator == other.ModifierSeparator
			&& StrokeSeparator == other.StrokeSeparator
			&& NumpadPrefix == other.NumpadPrefix
			&& ValueSequencesEqual(ModifierOrder, other.ModifierOrder)
			&& ValueDictionariesEqual(KeyTextOverrides, other.KeyTextOverrides);
	}

	/// <inheritdoc/>
	public override int GetHashCode()
	{
		var hash = new HashCode();

		hash.Add(ControlLabel);
		hash.Add(ShiftLabel);
		hash.Add(AltLabel);
		hash.Add(MetaLabel);
		hash.Add(ModifierSeparator);
		hash.Add(StrokeSeparator);
		hash.Add(NumpadPrefix);

		foreach (KeyModifierSet modifier in ModifierOrder)
			hash.Add(modifier);

		// The entries compare without regard to order, so their hash contribution must be order-independent
		// as well: two equal conventions must always produce the same code.
		int overridesHash = 0;

		foreach (KeyValuePair<KeyCode, string> pair in KeyTextOverrides)
			overridesHash ^= HashCode.Combine(pair.Key, pair.Value);

		hash.Add(overridesHash);

		return hash.ToHashCode();
	}

	/// <summary>
	/// Renders a short summary; the generated record form would print every <see cref="KeyTextOverrides"/>
	/// entry and flood a host that logs its options.
	/// </summary>
	/// <returns>The summary of this convention.</returns>
	public override string ToString()
		=> $"DesktopKeyDisplayConventions {{ ControlLabel = {ControlLabel}, ShiftLabel = {ShiftLabel}, " +
			$"AltLabel = {AltLabel}, MetaLabel = {MetaLabel}, ModifierOrder = [{string.Join(", ", ModifierOrder)}], " +
			$"ModifierSeparator = {ModifierSeparator}, StrokeSeparator = {StrokeSeparator}, " +
			$"NumpadPrefix = {NumpadPrefix}, KeyTextOverrides = {KeyTextOverrides.Count} }}";

	private static bool ValueSequencesEqual(IReadOnlyList<KeyModifierSet> left, IReadOnlyList<KeyModifierSet> right)
	{
		if (ReferenceEquals(left, right))
			return true;

		if (left.Count != right.Count)
			return false;

		for (int i = 0; i < left.Count; i++)
		{
			if (left[i] != right[i])
				return false;
		}

		return true;
	}

	private static bool ValueDictionariesEqual(IReadOnlyDictionary<KeyCode, string> left, IReadOnlyDictionary<KeyCode, string> right)
	{
		if (ReferenceEquals(left, right))
			return true;

		if (left.Count != right.Count)
			return false;

		foreach (KeyValuePair<KeyCode, string> pair in left)
		{
			if (!right.TryGetValue(pair.Key, out string? value) || value != pair.Value)
				return false;
		}

		return true;
	}
}

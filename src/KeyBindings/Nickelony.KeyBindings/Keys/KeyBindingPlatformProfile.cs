using System.Collections.Frozen;
using System.Numerics;

namespace Nickelony.KeyBindings;

/// <summary>Describes what varies by desktop platform: the primary shortcut modifier and the text a key renders as.</summary>
/// <remarks>
/// <para>
/// A host picks one preset - <see cref="Windows"/>, <see cref="Linux"/> or <see cref="MacOS"/> - and passes it to <see cref="PrimaryStroke"/>
/// while it builds its command catalog and to a desktop display formatter for the text. The presets carry no operating-system check, so the host
/// decides which platform it targets; <see cref="ForCurrentPlatform"/> picks the preset of the running system.
/// </para>
/// <para>
/// A default instance carries Control as the primary modifier
/// and the canonical neutral labels (<c>Control</c>, <c>Meta</c>, <c>NumPad </c>), and a preset is adjusted with the <c>with</c>-configuration idiom,
/// for example <c>KeyBindingPlatformProfile.Windows with { MetaLabel = "Cmd" }</c>. Equality compares every member,
/// including the <see cref="ModifierOrder"/> sequence and the <see cref="KeyTextOverrides"/> entries.
/// </para>
/// </remarks>
public sealed record KeyBindingPlatformProfile
{
	/// <summary>The text overrides shared by the Windows and Linux presets: the US-layout punctuation glyphs and <c>Esc</c>.</summary>
	/// <remarks>
	/// A key that is not listed renders as its canonical <see cref="KeyCode"/> member name; the numeric keypad's digit and operator keys render
	/// with <see cref="NumPadPrefix"/>.
	/// </remarks>
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
		[KeyCode.Escape] = "Esc",
	}.ToFrozenDictionary();

	/// <summary>The text overrides of the macOS preset: the desktop overrides plus the macOS keyboard words and the arrow glyphs.</summary>
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
		[KeyCode.Down] = "↓",
	}.ToFrozenDictionary();

	private KeyModifiers _primaryModifier = KeyModifiers.Control;
	private IReadOnlyList<KeyModifiers> _modifierOrder = [KeyModifiers.Control, KeyModifiers.Shift, KeyModifiers.Alt, KeyModifiers.Meta];

	/// <summary>The neutral profile: Control is the primary modifier and every label is the canonical name.</summary>
	public static KeyBindingPlatformProfile Default { get; } = new();

	/// <summary>
	/// The preset of the operating system the process runs on: <see cref="MacOS"/>, <see cref="Windows"/>, or <see cref="Linux"/> for any other.
	/// </summary>
	public static KeyBindingPlatformProfile ForCurrentPlatform =>
		OperatingSystem.IsMacOS() ? MacOS
		: OperatingSystem.IsWindows() ? Windows
		: Linux;

	/// <summary>The Windows preset: Control is the primary modifier, and the labels are <c>Ctrl</c>, <c>Shift</c>, <c>Alt</c>, <c>Win</c>.</summary>
	public static KeyBindingPlatformProfile Windows { get; } =
		new()
		{
			ControlLabel = "Ctrl",
			MetaLabel = "Win",
			NumPadPrefix = "Num ",
			KeyTextOverrides = s_desktopKeyText,
		};

	/// <summary>The Linux preset: Control is the primary modifier, and the labels are <c>Ctrl</c>, <c>Shift</c>, <c>Alt</c>, <c>Super</c>.</summary>
	public static KeyBindingPlatformProfile Linux { get; } =
		new()
		{
			ControlLabel = "Ctrl",
			MetaLabel = "Super",
			NumPadPrefix = "Num ",
			KeyTextOverrides = s_desktopKeyText,
		};

	/// <summary>
	/// The macOS preset: Meta (Command) is the primary modifier, and the glyph modifiers are Control <c>⌃</c>, Option <c>⌥</c> (the Alt key),
	/// Shift <c>⇧</c> and Command <c>⌘</c> (the Meta key), rendered in the Control, Option, Shift, Command order with no separator between them.
	/// </summary>
	/// <remarks>
	/// The macOS keyboard labels its Backspace key <c>Delete</c>, so <see cref="KeyCode.Backspace"/> renders as <c>Delete</c>
	/// and <see cref="KeyCode.Delete"/> as <c>Forward Delete</c>.
	/// </remarks>
	public static KeyBindingPlatformProfile MacOS { get; } =
		new()
		{
			PrimaryModifier = KeyModifiers.Meta,
			ControlLabel = "⌃",
			ShiftLabel = "⇧",
			AltLabel = "⌥",
			MetaLabel = "⌘",
			NumPadPrefix = "Num ",
			ModifierOrder = [KeyModifiers.Control, KeyModifiers.Alt, KeyModifiers.Shift, KeyModifiers.Meta],
			ModifierSeparator = string.Empty,
			StrokeSeparator = " ",
			KeyTextOverrides = s_macOSKeyText,
		};

	/// <summary>The modifier a primary shortcut uses on the platform: Control on Windows and Linux, Meta (Command) on macOS.</summary>
	/// <exception cref="ArgumentException">The assigned value is not exactly one defined modifier.</exception>
	public KeyModifiers PrimaryModifier
	{
		get => _primaryModifier;
		init
		{
			if (value == KeyModifiers.None || value.HasUndefinedFlags() || !BitOperations.IsPow2((uint)value))
				throw new ArgumentException("The primary modifier must be exactly one defined modifier.", nameof(PrimaryModifier));

			_primaryModifier = value;
		}
	}

	/// <summary>The label the Control modifier renders as.</summary>
	public string ControlLabel { get; init; } = "Control";

	/// <summary>The label the Shift modifier renders as.</summary>
	public string ShiftLabel { get; init; } = "Shift";

	/// <summary>The label the Alt modifier renders as.</summary>
	public string AltLabel { get; init; } = "Alt";

	/// <summary>The label the Meta modifier renders as.</summary>
	public string MetaLabel { get; init; } = "Meta";

	/// <summary>The order the modifiers render in.</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// The assigned value holds an entry that is not exactly one defined modifier, repeats a modifier, or leaves a defined modifier out.
	/// </exception>
	/// <remarks>
	/// The sequence is copied on assignment, and it names every modifier once, so the formatter renders each pressed modifier once.
	/// </remarks>
	public IReadOnlyList<KeyModifiers> ModifierOrder
	{
		get => _modifierOrder;
		init
		{
			ArgumentNullException.ThrowIfNull(value);
			_modifierOrder = ValidateModifierOrder(value);
		}
	}

	/// <summary>The text appended after each rendered modifier label.</summary>
	public string ModifierSeparator { get; init; } = "+";

	/// <summary>The text placed between the strokes of a chord.</summary>
	public string StrokeSeparator { get; init; } = ", ";

	/// <summary>The prefix the numeric keypad's digit and operator keys render with.</summary>
	public string NumPadPrefix { get; init; } = "NumPad ";

	/// <summary>The per-key text overrides.</summary>
	/// <remarks>
	/// A key that is not listed renders as its canonical <see cref="KeyCode"/> member name, except for the numeric keypad's digit and operator keys,
	/// which a desktop display formatter renders with <see cref="NumPadPrefix"/>.
	/// </remarks>
	public IReadOnlyDictionary<KeyCode, string> KeyTextOverrides { get; init; } = FrozenDictionary<KeyCode, string>.Empty;

	/// <summary>Builds a stroke whose key carries the profile's primary modifier together with any additional modifiers.</summary>
	/// <param name="key">The stroke's primary key.</param>
	/// <param name="additionalModifiers">Any further modifiers to hold with the primary modifier.</param>
	/// <returns>The stroke, which converts implicitly to a one-stroke <see cref="KeyChord"/>.</returns>
	/// <exception cref="ArgumentException">
	/// <paramref name="key"/> is not a defined <see cref="KeyCode"/> member, or <paramref name="additionalModifiers"/> sets bits outside the defined
	/// <see cref="KeyModifiers"/> flags.
	/// </exception>
	public KeyStroke PrimaryStroke(KeyCode key, KeyModifiers additionalModifiers = KeyModifiers.None)
	{
		if (additionalModifiers.HasUndefinedFlags())
			throw new ArgumentException("The additional modifiers must use only defined KeyModifiers flags.", nameof(additionalModifiers));

		return new(key, PrimaryModifier | additionalModifiers);
	}

	/// <summary>Determines whether two profiles describe the same platform.</summary>
	/// <param name="other">The profile to compare with.</param>
	/// <returns>
	/// <see langword="true"/> when every member is equal, including the <see cref="ModifierOrder"/> sequence
	/// and the <see cref="KeyTextOverrides"/> entries; otherwise, <see langword="false"/>.
	/// </returns>
	public bool Equals(KeyBindingPlatformProfile? other)
	{
		if (other is null)
			return false;

		if (ReferenceEquals(this, other))
			return true;

		return PrimaryModifier == other.PrimaryModifier
			&& ControlLabel == other.ControlLabel
			&& ShiftLabel == other.ShiftLabel
			&& AltLabel == other.AltLabel
			&& MetaLabel == other.MetaLabel
			&& ModifierSeparator == other.ModifierSeparator
			&& StrokeSeparator == other.StrokeSeparator
			&& NumPadPrefix == other.NumPadPrefix
			&& ValueSequencesEqual(ModifierOrder, other.ModifierOrder)
			&& ValueDictionariesEqual(KeyTextOverrides, other.KeyTextOverrides);
	}

	/// <inheritdoc/>
	public override int GetHashCode()
	{
		var hash = new HashCode();

		hash.Add(PrimaryModifier);
		hash.Add(ControlLabel);
		hash.Add(ShiftLabel);
		hash.Add(AltLabel);
		hash.Add(MetaLabel);
		hash.Add(ModifierSeparator);
		hash.Add(StrokeSeparator);
		hash.Add(NumPadPrefix);

		foreach (KeyModifiers modifier in ModifierOrder)
			hash.Add(modifier);

		// The entries compare without regard to order, so their hash contribution must be order-independent
		// as well: two equal profiles must always produce the same code.
		int overridesHash = 0;

		foreach (KeyValuePair<KeyCode, string> pair in KeyTextOverrides)
			overridesHash ^= HashCode.Combine(pair.Key, pair.Value);

		hash.Add(overridesHash);

		return hash.ToHashCode();
	}

	/// <summary>Renders a one-line summary of this profile for logs.</summary>
	/// <returns>The summary of this profile.</returns>
	public override string ToString()
	{
		return $"KeyBindingPlatformProfile {{ PrimaryModifier = {PrimaryModifier}, ControlLabel = {ControlLabel}, ShiftLabel = {ShiftLabel}, "
			+ $"AltLabel = {AltLabel}, MetaLabel = {MetaLabel}, ModifierOrder = [{string.Join(", ", ModifierOrder)}], "
			+ $"ModifierSeparator = {ModifierSeparator}, StrokeSeparator = {StrokeSeparator}, "
			+ $"NumPadPrefix = {NumPadPrefix}, KeyTextOverrides = {KeyTextOverrides.Count} }}";
	}

	private static KeyModifiers[] ValidateModifierOrder(IReadOnlyList<KeyModifiers> value)
	{
		KeyModifiers[] order = [.. value];
		KeyModifiers seen = KeyModifiers.None;

		foreach (KeyModifiers modifier in order)
		{
			if (modifier == KeyModifiers.None || modifier.HasUndefinedFlags() || !BitOperations.IsPow2((uint)modifier))
				throw new ArgumentException("Each entry of the modifier order must be exactly one defined modifier.", nameof(value));

			if ((seen & modifier) != 0)
				throw new ArgumentException($"The modifier order names {modifier} more than once.", nameof(value));

			seen |= modifier;
		}

		if (seen != (KeyModifiers.Alt | KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Meta))
			throw new ArgumentException("The modifier order must name every modifier.", nameof(value));

		return order;
	}

	private static bool ValueSequencesEqual(IReadOnlyList<KeyModifiers> left, IReadOnlyList<KeyModifiers> right)
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

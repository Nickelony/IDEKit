namespace Nickelony.KeyBindings;

/// <summary>
/// Renders key strokes with the text conventions of a platform, such as <c>Ctrl+Shift+S</c>, <c>Win+S</c>, or the macOS glyph text <c>⇧⌘Z</c>.
/// </summary>
/// <remarks>
/// The text is not localized: the key labels follow the standard US layout and the modifiers follow the <see cref="Profile"/> value.
/// <see cref="KeyDisplayTextFormatter.Default"/> stays platform-neutral; a host opts in by assigning a formatter over a platform profile
/// to <c>KeyBindingServiceOptions.DisplayTextFormatter</c>.
/// </remarks>
public sealed class DesktopKeyDisplayTextFormatter : KeyDisplayTextFormatter
{
	/// <param name="profile">The platform profile whose text conventions the formatter renders.</param>
	/// <exception cref="ArgumentNullException"><paramref name="profile"/> is <see langword="null"/>.</exception>
	public DesktopKeyDisplayTextFormatter(KeyBindingPlatformProfile profile)
	{
		ArgumentNullException.ThrowIfNull(profile);
		Profile = profile;
	}

	/// <summary>The platform profile whose text conventions the formatter renders.</summary>
	public KeyBindingPlatformProfile Profile { get; }

	/// <inheritdoc/>
	protected override IReadOnlyList<KeyModifiers> GetModifierOrder() => Profile.ModifierOrder;

	/// <inheritdoc/>
	protected override string GetModifierSeparator() => Profile.ModifierSeparator;

	/// <inheritdoc/>
	protected override string GetStrokeSeparator() => Profile.StrokeSeparator;

	/// <inheritdoc/>
	protected override string GetKeyText(KeyCode key)
	{
		if (Profile.KeyTextOverrides.TryGetValue(key, out string? text))
			return text;

		// A numeric keypad key without its own override renders as the convention's NumPadPrefix plus its symbol,
		// so one prefix value relabels them all.
		return GetNumPadSymbol(key) is { } symbol ? Profile.NumPadPrefix + symbol : base.GetKeyText(key);
	}

	/// <inheritdoc/>
	protected override string GetModifierText(KeyModifiers modifier)
	{
		return modifier switch
		{
			KeyModifiers.Control => Profile.ControlLabel,
			KeyModifiers.Shift => Profile.ShiftLabel,
			KeyModifiers.Alt => Profile.AltLabel,
			KeyModifiers.Meta => Profile.MetaLabel,
			_ => string.Empty,
		};
	}

	/// <summary>Returns the symbol a numeric keypad key renders as after the convention's numeric keypad prefix.</summary>
	/// <param name="key">The key to render.</param>
	/// <returns>The numeric keypad symbol, or <see langword="null"/> when the key is not a numeric keypad key.</returns>
	private static string? GetNumPadSymbol(KeyCode key)
	{
		return key switch
		{
			KeyCode.NumPad0 => "0",
			KeyCode.NumPad1 => "1",
			KeyCode.NumPad2 => "2",
			KeyCode.NumPad3 => "3",
			KeyCode.NumPad4 => "4",
			KeyCode.NumPad5 => "5",
			KeyCode.NumPad6 => "6",
			KeyCode.NumPad7 => "7",
			KeyCode.NumPad8 => "8",
			KeyCode.NumPad9 => "9",
			KeyCode.Add => "+",
			KeyCode.Subtract => "-",
			KeyCode.Multiply => "*",
			KeyCode.Divide => "/",
			KeyCode.Decimal => ".",
			_ => null,
		};
	}
}

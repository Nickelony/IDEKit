namespace Nickelony.KeyBindings;

/// <summary>
/// Renders key combos with a configurable desktop convention, such as <c>Ctrl+Shift+S</c>,
/// <c>Win+S</c>, or the macOS glyph text <c>⇧⌘Z</c>.
/// </summary>
/// <remarks>
/// <para>
/// The formatter is a leaf of <see cref="KeyDisplayTextFormatter"/> driven by a
/// <see cref="DesktopKeyDisplayConventions"/> value. It carries the desktop conventions and depends on
/// no desktop toolkit, so any host can use it. <see cref="KeyDisplayTextFormatter.Default"/> stays the
/// platform-neutral default; a host opts in by assigning one of the <see cref="Windows"/>,
/// <see cref="Linux"/>, or <see cref="MacOS"/> instances to
/// <see cref="KeyBindingServiceOptions.DisplayTextFormatter"/>.
/// </para>
/// <para>
/// The text is not localized: the key labels follow the standard US layout and the modifiers follow the
/// convention the <see cref="Conventions"/> value carries. A host that needs culture-aware or
/// otherwise different text supplies its own <see cref="IKeyDisplayTextFormatter"/>.
/// </para>
/// </remarks>
public sealed class DesktopKeyDisplayTextFormatter : KeyDisplayTextFormatter
{
	/// <summary>
	/// Gets the shared formatter for the <see cref="DesktopKeyDisplayConventions.Windows"/> convention.
	/// </summary>
	public static DesktopKeyDisplayTextFormatter Windows { get; } = new(DesktopKeyDisplayConventions.Windows);

	/// <summary>
	/// Gets the shared formatter for the <see cref="DesktopKeyDisplayConventions.Linux"/> convention.
	/// </summary>
	public static DesktopKeyDisplayTextFormatter Linux { get; } = new(DesktopKeyDisplayConventions.Linux);

	/// <summary>
	/// Gets the shared formatter for the <see cref="DesktopKeyDisplayConventions.MacOS"/> convention.
	/// </summary>
	public static DesktopKeyDisplayTextFormatter MacOS { get; } = new(DesktopKeyDisplayConventions.MacOS);

	private readonly DesktopKeyDisplayConventions _conventions;

	/// <summary>
	/// Initializes a new instance of the <see cref="DesktopKeyDisplayTextFormatter"/> class.
	/// </summary>
	/// <param name="conventions">The conventions the formatter renders.</param>
	/// <exception cref="ArgumentNullException"><paramref name="conventions"/> is <see langword="null"/>.</exception>
	public DesktopKeyDisplayTextFormatter(DesktopKeyDisplayConventions conventions)
	{
		ArgumentNullException.ThrowIfNull(conventions);

		_conventions = conventions;
	}

	/// <summary>
	/// Gets the conventions the formatter renders.
	/// </summary>
	public DesktopKeyDisplayConventions Conventions => _conventions;

	/// <inheritdoc/>
	protected override IReadOnlyList<KeyModifierSet> GetModifierOrder() => _conventions.ModifierOrder;

	/// <inheritdoc/>
	protected override string GetModifierSeparator() => _conventions.ModifierSeparator;

	/// <inheritdoc/>
	protected override string GetStrokeSeparator() => _conventions.StrokeSeparator;

	/// <inheritdoc/>
	protected override string GetKeyText(KeyCode key)
	{
		if (_conventions.KeyTextOverrides.TryGetValue(key, out string? text))
			return text;

		// The numpad symbols share one prefix across the desktop conventions, so they are not part of a
		// convention's text table; a host that wants a different prefix overrides the key directly.
		return GetNumpadSymbol(key) is { } symbol ? _conventions.NumpadPrefix + symbol : base.GetKeyText(key);
	}

	/// <inheritdoc/>
	protected override string GetModifierText(KeyModifierSet modifier)
		=> modifier switch
		{
			KeyModifierSet.Control => _conventions.ControlLabel,
			KeyModifierSet.Shift => _conventions.ShiftLabel,
			KeyModifierSet.Alt => _conventions.AltLabel,
			KeyModifierSet.Meta => _conventions.MetaLabel,
			_ => string.Empty
		};

	/// <summary>
	/// Returns the symbol a numpad key renders as after the convention's numpad prefix.
	/// </summary>
	/// <param name="key">The key to render.</param>
	/// <returns>The numpad symbol, or <see langword="null"/> when the key is not a numpad key.</returns>
	private static string? GetNumpadSymbol(KeyCode key)
		=> key switch
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
			_ => null
		};
}

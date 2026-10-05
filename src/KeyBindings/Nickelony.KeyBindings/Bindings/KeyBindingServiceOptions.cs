namespace Nickelony.KeyBindings;

/// <summary>Optional configuration for <see cref="KeyBindingService{TCommandId}"/>.</summary>
public sealed record KeyBindingServiceOptions
{
	private IKeyDisplayTextFormatter _displayTextFormatter = KeyDisplayTextFormatter.Default;

	/// <summary>The formatter that renders key strokes and chords as display text.</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	/// <remarks>
	/// Defaults to <see cref="KeyDisplayTextFormatter.Default"/>, which renders the canonical <see cref="KeyCode"/> and <see cref="KeyModifiers"/>
	/// names and carries no desktop conventions. A host supplies desktop-convention text (for example <see cref="DesktopKeyDisplayTextFormatter"/>,
	/// WPF gesture text, or its own formatter) here.
	/// </remarks>
	public IKeyDisplayTextFormatter DisplayTextFormatter
	{
		get => _displayTextFormatter;
		init => _displayTextFormatter = value ?? throw new ArgumentNullException(nameof(DisplayTextFormatter));
	}

	/// <summary>The default options: the canonical display-text formatter.</summary>
	public static KeyBindingServiceOptions Default { get; } = new();
}

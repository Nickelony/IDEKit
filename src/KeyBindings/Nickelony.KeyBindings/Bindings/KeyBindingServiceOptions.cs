using Microsoft.Extensions.Logging;

namespace Nickelony.KeyBindings;

/// <summary>
/// Optional configuration for <see cref="KeyBindingService{TCommandId}"/>.
/// </summary>
public sealed record KeyBindingServiceOptions
{
	private IKeyDisplayTextFormatter _displayTextFormatter = KeyDisplayTextFormatter.Default;

	/// <summary>
	/// Gets or initializes the formatter used to render key combos and chords as display text.
	/// </summary>
	/// <remarks>
	/// Defaults to <see cref="KeyDisplayTextFormatter.Default"/>, which renders the canonical
	/// <see cref="KeyCode"/> and <see cref="KeyModifierSet"/> names and carries no platform conventions. A
	/// host supplies platform-convention text (for example <see cref="DesktopKeyDisplayTextFormatter"/>, WPF
	/// gesture text, or its own formatter) here.
	/// </remarks>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public IKeyDisplayTextFormatter DisplayTextFormatter
	{
		get => _displayTextFormatter;
		init => _displayTextFormatter = value ?? throw new ArgumentNullException(nameof(DisplayTextFormatter));
	}

	/// <summary>
	/// Gets or initializes the optional logger for override diagnostics: host-reserved, invalid, or unknown
	/// entries. When <see langword="null"/>, diagnostics are discarded.
	/// </summary>
	public ILogger? Logger { get; init; }

	/// <summary>
	/// Gets the default options: the canonical display-text formatter and no logger. A host overrides only the
	/// members it needs with a <c>with</c> expression.
	/// </summary>
	public static KeyBindingServiceOptions Default { get; } = new();
}

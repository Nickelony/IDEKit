namespace Nickelony.KeyBindings.Wpf;

/// <summary>Controls which WPF key-down events <see cref="KeyBindingDispatcherExtensions.HandleKeyDown"/> dispatches to the bound command.</summary>
/// <remarks>
/// The options only filter <em>which</em> events are considered; an event that another handler already marked handled is never dispatched,
/// whatever the options say.
/// </remarks>
public sealed record KeyDownHandlingOptions
{
	/// <summary>The options used when a caller supplies none: auto-repeat and AltGr strokes are both ignored.</summary>
	public static KeyDownHandlingOptions Default { get; } = new();

	/// <summary>Indicates whether events raised by an auto-repeating (held) key are ignored.</summary>
	/// <remarks>
	/// Defaults to <see langword="true"/>, so a held key runs its command once. Set to <see langword="false"/> for commands that are meant to repeat,
	/// such as a caret movement.
	/// </remarks>
	public bool IgnoreAutoRepeat { get; init; } = true;

	/// <summary>Indicates whether AltGr strokes are ignored.</summary>
	/// <remarks>
	/// Defaults to <see langword="true"/>: an AltGr stroke types a character on the layouts that have one, so it must not match a Ctrl+Alt binding;
	/// a Ctrl+Alt binding on a non-character key such as an F-key is unaffected. Set to <see langword="false"/> on a host that binds Ctrl+Alt
	/// deliberately and knows its layouts never report AltGr. Detection is described by <see cref="KeyEventArgsExtensions.IsAltGr"/>.
	/// </remarks>
	public bool IgnoreAltGr { get; init; } = true;
}

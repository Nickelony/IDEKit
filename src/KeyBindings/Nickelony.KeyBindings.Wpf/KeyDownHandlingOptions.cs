namespace Nickelony.KeyBindings.Wpf;

/// <summary>
/// Controls which WPF key-down events <see cref="KeyBindingDispatcherExtensions.HandleKeyDown{TCommandId}"/>
/// dispatches to the bound command.
/// </summary>
/// <remarks>
/// The options only filter <em>which</em> events are considered; an event that another handler already marked
/// handled is never dispatched, whatever the options say.
/// </remarks>
public sealed record KeyDownHandlingOptions
{
	/// <summary>
	/// Gets the options used when a caller supplies none: auto-repeat and AltGr strokes are both ignored.
	/// </summary>
	public static KeyDownHandlingOptions Default { get; } = new();

	/// <summary>
	/// Gets a value indicating whether events raised by an auto-repeating (held) key are ignored.
	/// </summary>
	/// <remarks>
	/// Defaults to <see langword="true"/>: a command is not expected to run once per repeat while a key is
	/// held. Set to <see langword="false"/> for commands that are meant to repeat, such as a caret
	/// movement.
	/// </remarks>
	public bool IgnoreAutoRepeat { get; init; } = true;

	/// <summary>
	/// Gets a value indicating whether AltGr strokes are ignored.
	/// </summary>
	/// <remarks>
	/// Defaults to <see langword="true"/>: an AltGr stroke types a character on the layouts that have one, so
	/// it must not match a Ctrl+Alt binding. Set to <see langword="false"/> on a host that binds Ctrl+Alt
	/// deliberately and knows its layouts never report AltGr. Detection is described by
	/// <see cref="KeyEventArgsExtensions.IsAltGr"/>.
	/// </remarks>
	public bool IgnoreAltGr { get; init; } = true;
}

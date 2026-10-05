namespace Nickelony.KeyBindings;

/// <summary>Routes strokes to commands, so a toolkit adapter can feed one dispatcher or a chain of them through the same call.</summary>
/// <remarks>
/// <see cref="KeyBindingDispatcher{TCommandId}"/> routes within one catalog and <see cref="KeyBindingDispatcherChain{TCommandId}"/> walks several
/// of them in priority order. The interface carries no command identity, because dispatching a stroke never exposes it.
/// </remarks>
public interface IKeyBindingDispatcher
{
	/// <summary>Tries to dispatch a stroke to the command bound to the chord it forms.</summary>
	/// <param name="keyStroke">The stroke to dispatch. An uninitialized stroke is never bound.</param>
	/// <param name="context">
	/// The active context token, or <see langword="null"/> for the always-active context. A host with no contexts passes <see langword="null"/>.
	/// </param>
	/// <returns>
	/// <see cref="KeyStrokeDispatchOutcome.Executed"/> when the stroke completed
	/// a bound chord and the command was invoked, <see cref="KeyStrokeDispatchOutcome.Pending"/> when the stroke started or continued a chord,
	/// or <see cref="KeyStrokeDispatchOutcome.NotHandled"/> when it did neither. The caller marks the input event consumed for the first two.
	/// </returns>
	KeyStrokeDispatchOutcome Dispatch(KeyStroke keyStroke, string? context);
}

namespace Nickelony.KeyBindings;

/// <summary>Dispatches strokes across a priority-ordered chain of context dispatchers.</summary>
/// <typeparam name="TCommandId">The command identity type.</typeparam>
/// <remarks>
/// A host that routes input by context composes one service and dispatcher per context, ordered from the most specific to the least specific,
/// and evaluates them as one chain. The chain copies and validates its dispatchers once, when it is constructed.
/// </remarks>
public sealed class KeyBindingDispatcherChain<TCommandId> : IKeyBindingDispatcher
	where TCommandId : notnull
{
	private readonly KeyBindingDispatcher<TCommandId>[] _dispatchers;

	/// <param name="dispatchers">The dispatchers to evaluate, from the highest to the lowest priority.</param>
	/// <exception cref="ArgumentNullException"><paramref name="dispatchers"/> or one of its elements is <see langword="null"/>.</exception>
	public KeyBindingDispatcherChain(IReadOnlyList<KeyBindingDispatcher<TCommandId>> dispatchers)
	{
		ArgumentNullException.ThrowIfNull(dispatchers);

		_dispatchers = new KeyBindingDispatcher<TCommandId>[dispatchers.Count];

		for (int index = 0; index < _dispatchers.Length; index++)
			_dispatchers[index] = dispatchers[index] ?? throw new ArgumentNullException($"{nameof(dispatchers)}[{index}]");
	}

	/// <summary>Whether a dispatcher of the chain holds a chord in progress, which a host abandons on Escape or focus loss.</summary>
	public bool HasPendingChord => Array.Exists(_dispatchers, dispatcher => dispatcher.HasPendingChord);

	/// <summary>Discards the chord in progress of every dispatcher of the chain; with none pending, it does nothing.</summary>
	public void CancelPendingChord()
	{
		foreach (KeyBindingDispatcher<TCommandId> dispatcher in _dispatchers)
			dispatcher.CancelPendingChord();
	}

	/// <summary>Dispatches a stroke through the dispatchers in priority order.</summary>
	/// <param name="keyStroke">The stroke to dispatch. Uninitialized strokes are never bound.</param>
	/// <param name="context">
	/// The active context token, or <see langword="null"/> for the always-active context. Every dispatcher receives the same token, because the host
	/// has one active context.
	/// </param>
	/// <returns>
	/// The outcome reported by the dispatcher that handled the stroke, or <see cref="KeyStrokeDispatchOutcome.NotHandled"/> when no dispatcher did.
	/// </returns>
	/// <remarks>
	/// A stroke that continues a chord goes to the dispatcher that started it and that outcome is final, so a lower-priority context
	/// cannot steal the continuation; a buffered chord whose context has since become inactive is abandoned and the stroke is dispatched as fresh.
	/// With no pending chord the chain stops at the first dispatcher that executes the stroke or starts a chord; a command that is bound
	/// but cannot execute does not swallow the stroke.
	/// </remarks>
	public KeyStrokeDispatchOutcome Dispatch(KeyStroke keyStroke, string? context)
	{
		foreach (KeyBindingDispatcher<TCommandId> dispatcher in _dispatchers)
		{
			if (!dispatcher.HasPendingChord)
				continue;

			if (string.Equals(dispatcher.PendingContext, context, StringComparison.Ordinal))
				return dispatcher.Dispatch(keyStroke, context);

			// The chord was started in a context that has since become inactive, so its strokes cannot be
			// completed here. Abandoning the buffer lets the ordinary priority walk below dispatch the
			// stroke as a fresh stroke, which keeps the mode change from swallowing it.
			dispatcher.CancelPendingChord();
		}

		foreach (KeyBindingDispatcher<TCommandId> dispatcher in _dispatchers)
		{
			KeyStrokeDispatchOutcome outcome = dispatcher.Dispatch(keyStroke, context);

			if (outcome != KeyStrokeDispatchOutcome.NotHandled)
				return outcome;
		}

		return KeyStrokeDispatchOutcome.NotHandled;
	}
}

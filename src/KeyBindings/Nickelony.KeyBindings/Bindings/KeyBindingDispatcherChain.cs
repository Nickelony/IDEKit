namespace Nickelony.KeyBindings;

/// <summary>
/// Dispatches keystrokes across a priority-ordered chain of context dispatchers.
/// </summary>
/// <remarks>
/// <para>
/// A host that routes input by context composes one service and dispatcher per context, ordered from
/// the most specific to the least specific, and evaluates them as one chain. This is the host-neutral
/// dispatch order a key-down adapter feeds.
/// </para>
/// <para>
/// The chain covers contexts that can hold focus at the same time, such as several panes. A single
/// control whose mode changes uses one dispatcher and the context token instead, because a mode is
/// not focus; a token a dispatcher's chord was started under also lets the chain abandon that chord
/// when the mode changed between two strokes.
/// </para>
/// </remarks>
public static class KeyBindingDispatcherChain
{
	/// <summary>
	/// Dispatches a stroke to the chord it belongs to, honoring the priority order.
	/// </summary>
	/// <typeparam name="TCommandId">The command identity type.</typeparam>
	/// <remarks>
	/// <para>
	/// A stroke that continues a chord belongs to the dispatcher that started it. When a dispatcher
	/// has a pending chord under the active context, it receives the stroke and its result is final,
	/// so a lower-priority context can never steal a chord continuation and a wrong stroke ends the
	/// chord instead of being offered to the next dispatcher.
	/// </para>
	/// <para>
	/// A pending chord that was started under a different context belongs to a mode that is no longer
	/// active, so the chain abandons it and dispatches the stroke as a fresh stroke. Without that, a
	/// mode change between two strokes would silently swallow the second stroke.
	/// </para>
	/// <para>
	/// With no pending chord the chain walks from the highest priority: evaluation stops at the first
	/// dispatcher that executes the stroke or starts a chord, so a context shadows every dispatcher
	/// behind it. A context whose command is bound but cannot execute does not swallow the stroke: the
	/// next dispatcher gets the turn.
	/// </para>
	/// <para>
	/// If more than one dispatcher holds a pending chord (host misuse, because one stroke continues only
	/// one chord), the first dispatcher whose pending context matches the active context receives the
	/// stroke and the dispatchers behind it are ignored.
	/// </para>
	/// <para>
	/// The null elements are validated before any dispatcher runs, so a malformed chain cannot execute
	/// part of itself and then throw.
	/// </para>
	/// </remarks>
	/// <param name="dispatchers">The dispatchers to evaluate, from the highest to the lowest priority.</param>
	/// <param name="keyCombo">The stroke to dispatch. Uninitialized strokes are never bound.</param>
	/// <param name="context">
	/// The active context token, or <see langword="null"/> for the always-active context. Every
	/// dispatcher receives the same token, because the host has one active context.
	/// </param>
	/// <returns>
	/// The result reported by the dispatcher that handled the stroke, or
	/// <see cref="KeyChordDispatchResult.NotHandled"/> when no dispatcher did.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="dispatchers"/> or one of its elements is <see langword="null"/>.
	/// </exception>
	public static KeyChordDispatchResult DispatchInPriorityOrder<TCommandId>(
		this IReadOnlyList<KeyBindingDispatcher<TCommandId>> dispatchers,
		KeyCombo keyCombo,
		string? context)
		where TCommandId : notnull
	{
		ArgumentNullException.ThrowIfNull(dispatchers);

		for (int index = 0; index < dispatchers.Count; index++)
			ArgumentNullException.ThrowIfNull(dispatchers[index], $"{nameof(dispatchers)}[{index}]");

		foreach (KeyBindingDispatcher<TCommandId> dispatcher in dispatchers)
		{
			if (!dispatcher.HasPendingChord)
				continue;

			if (string.Equals(dispatcher.PendingContext, context, StringComparison.Ordinal))
				return dispatcher.Dispatch(keyCombo, context);

			// The chord was started in a context that is no longer active, so its strokes cannot be
			// completed here. Abandoning the buffer lets the ordinary priority walk below dispatch the
			// stroke as a fresh stroke, which keeps the mode change from swallowing it.
			dispatcher.CancelPendingChord();
		}

		foreach (KeyBindingDispatcher<TCommandId> dispatcher in dispatchers)
		{
			KeyChordDispatchResult result = dispatcher.Dispatch(keyCombo, context);

			if (result != KeyChordDispatchResult.NotHandled)
				return result;
		}

		return KeyChordDispatchResult.NotHandled;
	}
}

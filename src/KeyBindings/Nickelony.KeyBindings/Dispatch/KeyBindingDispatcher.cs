namespace Nickelony.KeyBindings;

/// <summary>Routes strokes to the command bound to the chord they form, buffering a chord in progress.</summary>
/// <typeparam name="TCommandId">The command identity type.</typeparam>
/// <remarks>
/// The dispatcher owns the pending-chord buffer but not the clock: the host decides when a buffered chord
/// is abandoned and calls <see cref="CancelPendingChord"/>. A chord belongs to the context it was started in, so a context change between two strokes
/// abandons the buffer. Not thread-safe: the host feeds it from its input thread.
/// </remarks>
public sealed class KeyBindingDispatcher<TCommandId> : IKeyBindingDispatcher
	where TCommandId : notnull
{
	private readonly IKeyBindingResolver<TCommandId> _keyBindings;
	private readonly Func<TCommandId, bool> _canExecuteCommand;
	private readonly Action<TCommandId> _executeCommand;
	private readonly List<KeyStroke> _pendingStrokes = [];

	/// <param name="keyBindings">The lookup that resolves chords to commands, typically the binding service.</param>
	/// <param name="hooks">The command callbacks the dispatcher invokes.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="keyBindings"/> or a callback of <paramref name="hooks"/> is <see langword="null"/>.
	/// </exception>
	public KeyBindingDispatcher(IKeyBindingResolver<TCommandId> keyBindings, KeyBindingDispatcherHooks<TCommandId> hooks)
	{
		ArgumentNullException.ThrowIfNull(keyBindings);
		ArgumentNullException.ThrowIfNull(hooks.CanExecuteCommand);
		ArgumentNullException.ThrowIfNull(hooks.ExecuteCommand);

		_keyBindings = keyBindings;
		_canExecuteCommand = hooks.CanExecuteCommand;
		_executeCommand = hooks.ExecuteCommand;
	}

	public bool HasPendingChord => _pendingStrokes.Count > 0;

	/// <summary>The chord buffered so far, or the <see langword="default"/> chord when no chord is pending.</summary>
	public KeyChord PendingChord { get; private set; }

	/// <summary>The context token the buffered chord was started under, or <see langword="null"/> when no chord is buffered.</summary>
	/// <remarks>
	/// A priority chain compares it with the active token to route a continuation to its owner or abandon a chord started under a context
	/// that has since become inactive.
	/// </remarks>
	internal string? PendingContext { get; private set; }

	/// <summary>Discards the buffered chord, if any.</summary>
	/// <remarks>
	/// The host calls it when it decides a chord in progress is abandoned, for example on Escape, on focus loss,
	/// or when its own idle timeout elapses. Calling it with no buffered chord is a no-op.
	/// </remarks>
	public void CancelPendingChord()
	{
		_pendingStrokes.Clear();
		PendingContext = null;
		PendingChord = default;
	}

	/// <inheritdoc/>
	/// <remarks>
	/// The caller
	/// marks the input event consumed for both <see cref="KeyStrokeDispatchOutcome.Executed"/> and <see cref="KeyStrokeDispatchOutcome.Pending"/>,
	/// because a buffered chord consumed the stroke. A stroke starts or continues a chord only while a bound command that can execute is still
	/// reachable from the buffered chord; otherwise, it reports <see cref="KeyStrokeDispatchOutcome.NotHandled"/> and drops the buffer, so a chord
	/// that cannot run never consumes its first stroke. A stroke that completes a bound chord whose command cannot execute reports
	/// <see cref="KeyStrokeDispatchOutcome.NotHandled"/> and drops the buffer too, unless the chord is also a strict prefix of a longer bound chord
	/// that a command that can execute completes, in which case the continuation wins and the stroke is buffered. A buffered chord is dropped when
	/// <paramref name="context"/> differs from the one it was started in.
	/// </remarks>
	public KeyStrokeDispatchOutcome Dispatch(KeyStroke keyStroke, string? context)
	{
		if (!keyStroke.IsInitialized)
			return KeyStrokeDispatchOutcome.NotHandled;

		// A chord belongs to the context it was started in. A context change abandons the buffered
		// strokes instead of letting them complete under a context they were never started in.
		if (_pendingStrokes.Count > 0 && !string.Equals(PendingContext, context, StringComparison.Ordinal))
			CancelPendingChord();

		// A stroke that repeats one already buffered cannot extend a chord, because a chord never
		// repeats a stroke; the attempt drops the buffered chord instead of failing to build one.
		if (_pendingStrokes.Contains(keyStroke))
		{
			CancelPendingChord();
			return KeyStrokeDispatchOutcome.NotHandled;
		}

		// Build the candidate from a stack buffer so no throwaway array is materialized, and extend the
		// buffer only once the stroke is known to continue a chord.
		int bufferedCount = _pendingStrokes.Count;
		Span<KeyStroke> candidateStrokes = stackalloc KeyStroke[bufferedCount + 1];

		for (int strokeIndex = 0; strokeIndex < bufferedCount; strokeIndex++)
			candidateStrokes[strokeIndex] = _pendingStrokes[strokeIndex];

		candidateStrokes[bufferedCount] = keyStroke;

		var candidate = new KeyChord(candidateStrokes);

		if (_keyBindings.TryGetCommand(candidate, context, out TCommandId? command) && _canExecuteCommand(command))
		{
			CancelPendingChord();
			_executeCommand(command);
			return KeyStrokeDispatchOutcome.Executed;
		}

		// A stroke from which no executable command is reachable is not part of a chord in progress, so it is
		// left to the next dispatcher exactly like a complete chord whose command cannot execute.
		if (_keyBindings.IsChordPrefix(candidate, context, _canExecuteCommand))
		{
			_pendingStrokes.Add(keyStroke);
			PendingContext = context;

			// The candidate already names the extended buffer, so it is the cached pending chord.
			PendingChord = candidate;

			return KeyStrokeDispatchOutcome.Pending;
		}

		CancelPendingChord();
		return KeyStrokeDispatchOutcome.NotHandled;
	}
}

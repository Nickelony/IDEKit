namespace Nickelony.KeyBindings;

/// <summary>
/// Routes keystrokes to the command bound to the chord they form, buffering a chord in progress.
/// </summary>
/// <typeparam name="TCommandId">The command identity type.</typeparam>
/// <remarks>
/// <para>
/// The dispatcher owns the pending-chord state, not the clock. A stroke that starts or continues a
/// chord is buffered and reported as <see cref="KeyChordDispatchResult.Pending"/>; the stroke that
/// completes a bound chord runs its command and reports
/// <see cref="KeyChordDispatchResult.Executed"/>. The host decides when a buffered chord is
/// abandoned (Escape, focus loss, a timeout) and calls <see cref="CancelPendingChord"/>, because
/// the timing policy belongs to the toolkit that raises the key events.
/// </para>
/// <para>
/// The host also supplies the active context token per stroke. A chord belongs to the context it was
/// started in, so when the token changes between two strokes the buffered strokes are abandoned and
/// the new stroke starts a fresh chord instead of completing a chord the user never began in that
/// context.
/// </para>
/// <para>
/// A dispatcher is not thread-safe: the host feeds it from its input thread, the same expectation
/// the binding service documents for mutations.
/// </para>
/// </remarks>
public sealed class KeyBindingDispatcher<TCommandId>
	where TCommandId : notnull
{
	private readonly IKeyBindingService<TCommandId> _keyBindings;
	private readonly Func<TCommandId, bool> _canExecuteCommand;
	private readonly Action<TCommandId> _executeCommand;
	private readonly List<KeyCombo> _pendingStrokes = [];

	private string? _pendingContext;

	// The chord built from the buffer, refreshed whenever the buffer changes so a read of PendingChord
	// does not build a fresh chord from the strokes. The default chord while no chord is buffered.
	private KeyChord _pendingChord;

	/// <summary>
	/// Initializes a new instance of the <see cref="KeyBindingDispatcher{TCommandId}"/> class.
	/// </summary>
	/// <param name="keyBindings">The binding service used to resolve chords to commands.</param>
	/// <param name="hooks">The command callbacks the dispatcher invokes.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="keyBindings"/> or <paramref name="hooks"/> is <see langword="null"/>.
	/// </exception>
	public KeyBindingDispatcher(
		IKeyBindingService<TCommandId> keyBindings,
		KeyBindingDispatcherHooks<TCommandId> hooks)
	{
		ArgumentNullException.ThrowIfNull(keyBindings);
		ArgumentNullException.ThrowIfNull(hooks);

		_keyBindings = keyBindings;
		_canExecuteCommand = hooks.CanExecuteCommand;
		_executeCommand = hooks.ExecuteCommand;
	}

	/// <summary>
	/// Gets a value indicating whether a chord is currently buffered.
	/// </summary>
	public bool HasPendingChord => _pendingStrokes.Count > 0;

	/// <summary>
	/// Gets the chord buffered so far, or the <see langword="default"/> chord when no chord is
	/// pending. A host can render its strokes as a status-bar hint while the chord is incomplete.
	/// </summary>
	public KeyChord PendingChord => _pendingChord;

	/// <summary>
	/// Gets the context token the buffered chord was started under, or <see langword="null"/> when no
	/// chord is buffered.
	/// </summary>
	/// <remarks>
	/// A priority chain uses this to tell a chord continuation from a chord that a context change
	/// abandoned, so it can route the continuation to its owner and still dispatch a stroke as a fresh
	/// stroke after a context change.
	/// </remarks>
	internal string? PendingContext => _pendingContext;

	/// <summary>
	/// Discards the buffered chord, if any. Calling it with no buffered chord is a no-op.
	/// </summary>
	/// <remarks>
	/// The host calls this when it decides a chord in progress is abandoned, for example on Escape,
	/// on focus loss, or when its own idle timeout elapses.
	/// </remarks>
	public void CancelPendingChord()
	{
		_pendingStrokes.Clear();
		_pendingContext = null;
		_pendingChord = default;
	}

	/// <summary>
	/// Tries to dispatch a stroke to the command bound to the chord it forms.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The caller decides whether a handled input event must be marked as consumed: it marks the
	/// event for both <see cref="KeyChordDispatchResult.Executed"/> and
	/// <see cref="KeyChordDispatchResult.Pending"/>, because a buffered chord consumed the stroke.
	/// </para>
	/// <para>
	/// A stroke that completes a bound chord whose command cannot currently execute is reported as
	/// <see cref="KeyChordDispatchResult.NotHandled"/> and the buffered chord is dropped, matching the
	/// single-stroke rule that a command which cannot execute does not swallow the key. When the same
	/// candidate chord is also a strict prefix of a longer bound chord, the continuation is the more
	/// specific intent: the stroke is buffered and reported as <see cref="KeyChordDispatchResult.Pending"/>,
	/// so the shared prefix stays reachable. (The catalog and the override validation reject a bound chord
	/// that is a strict prefix of a co-active bound chord, so this only arises with a custom
	/// <see cref="IKeyBindingService{TCommandId}"/> that does not enforce that rule.)
	/// </para>
	/// <para>
	/// When <paramref name="context"/> differs from the context a buffered chord was started in, the
	/// buffered chord is dropped before the stroke is considered: the strokes of a chord are
	/// context-specific, so completing a chord in another context could run a command the user never
	/// started there. The stroke then starts a fresh chord in the new context.
	/// </para>
	/// </remarks>
	/// <param name="keyCombo">The stroke to dispatch. An uninitialized stroke is never bound and
	/// leaves a buffered chord untouched.</param>
	/// <param name="context">
	/// The active context token, or <see langword="null"/> for the always-active context. A host with
	/// no contexts passes <see langword="null"/>.
	/// </param>
	/// <returns>
	/// <see cref="KeyChordDispatchResult.Executed"/> when the stroke completed a bound chord and the
	/// command was invoked, <see cref="KeyChordDispatchResult.Pending"/> when the stroke started or
	/// continued a chord, or <see cref="KeyChordDispatchResult.NotHandled"/> when the stroke neither
	/// completed nor continued a chord.
	/// </returns>
	public KeyChordDispatchResult Dispatch(KeyCombo keyCombo, string? context)
	{
		if (!keyCombo.IsInitialized)
			return KeyChordDispatchResult.NotHandled;

		// A chord belongs to the context it was started in. A context change abandons the buffered
		// strokes instead of letting them complete under a context they were never started in.
		if (_pendingStrokes.Count > 0 && !string.Equals(_pendingContext, context, StringComparison.Ordinal))
			CancelPendingChord();

		// A stroke that repeats one already buffered cannot extend a chord, because a chord never
		// repeats a stroke; the attempt drops the buffered chord instead of failing to build one.
		if (_pendingStrokes.Contains(keyCombo))
		{
			CancelPendingChord();
			return KeyChordDispatchResult.NotHandled;
		}

		// The candidate is the buffered strokes plus the new one. The buffer itself is extended only once
		// the stroke is known to continue a chord, so a stroke that completes or fails never leaves a
		// duplicated stroke behind.
		var candidate = new KeyChord([.. _pendingStrokes, keyCombo]);

		if (_keyBindings.TryGetCommand(candidate, context, out TCommandId? command) && _canExecuteCommand(command))
		{
			CancelPendingChord();
			_executeCommand(command);
			return KeyChordDispatchResult.Executed;
		}

		if (_keyBindings.IsChordPrefix(candidate, context))
		{
			_pendingStrokes.Add(keyCombo);
			_pendingContext = context;

			// The candidate already names the extended buffer, so it is the cached pending chord.
			_pendingChord = candidate;

			return KeyChordDispatchResult.Pending;
		}

		CancelPendingChord();
		return KeyChordDispatchResult.NotHandled;
	}
}

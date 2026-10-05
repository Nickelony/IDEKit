namespace Nickelony.KeyBindings;

/// <summary>
/// Provides runtime key binding lookup, display text, validation, rebinding, and reset operations, plus a <see cref="BindingsChanged"/> notification.
/// </summary>
/// <typeparam name="TCommandId">The command identity type.</typeparam>
/// <remarks>
/// <para>
/// Mutations are expected from a single owning thread, typically the host's input thread. The complete candidate state is validated,
/// persisted through the overrides store, and only then published as one immutable snapshot, so a concurrent reader never observes data from two
/// rebuilds and a failed or throwing store call leaves the runtime state unchanged.
/// </para>
/// <para>
/// A command's bindings can be scoped to a <see cref="CommandDescriptor{TCommandId}.Context"/> token;
/// <see langword="null"/> is the only always-active token. An empty or whitespace-only token is never declared, so a lookup with one resolves
/// only the always-active bindings. Two bindings only compete when their contexts can be active at the same time, so the same chord can be bound
/// in two different contexts.
/// </para>
/// <para>
/// <see cref="Validate"/>, <see cref="Apply"/>, <see cref="Reset"/>, and <see cref="ResetAll"/> all validate the state the operation would produce,
/// so a <see cref="KeyBindingOutcome.Conflict"/> or <see cref="KeyBindingOutcome.PrefixConflict"/> can describe a binding another command
/// keeps or regains when the operation removes or restores an entry. Override entries whose command the catalog does not contain are preserved
/// in every written snapshot, and a claim that would collide with one is refused for every policy.
/// </para>
/// <para>
/// A semantic rejection is reported as a <see cref="KeyBindingOutcome"/> and leaves the state unchanged; a malformed call -
/// an uninitialized chord or an undefined policy value - throws instead. The <c>command</c> parameters reject <see langword="null"/>
/// because <typeparamref name="TCommandId"/> is not constrained to a value type; with a value-type identity - the common enum case -
/// that guard can never trigger, so an unknown command is reported through the outcome rather than by throwing.
/// </para>
/// </remarks>
public interface IKeyBindingService<TCommandId> : IKeyBindingResolver<TCommandId>
	where TCommandId : notnull
{
	/// <summary>
	/// Raised after the runtime maps are rebuilt by an <see cref="Apply"/>, <see cref="Reset"/>, or <see cref="ResetAll"/> operation.
	/// </summary>
	/// <remarks>
	/// The event data names the command whose bindings changed, or sets <see cref="BindingsChangedEventArgs{TCommandId}.AffectsAllCommands"/>
	/// for a <see cref="ResetAll"/> that could change every command and for an <see cref="Apply"/> or a <see cref="Reset"/> that changes the bindings
	/// another command dispatches: <see cref="KeyBindingConflictPolicy.Replace"/> taking its chord, or a default regaining a chord the removed
	/// or narrowed override had shadowed. An exception thrown by a handler propagates to the operation that raised the event, after the state
	/// was committed.
	/// </remarks>
	event EventHandler<BindingsChangedEventArgs<TCommandId>>? BindingsChanged;

	/// <summary>Lists every context token the catalog declares, or an empty set when it declares none.</summary>
	/// <remarks>
	/// Check the host's context token against this set to catch a typo. An undeclared token resolves only the always-active bindings,
	/// exactly like <see langword="null"/>, so a typo silently disables context scoping without any error. The comparison is ordinal.
	/// </remarks>
	IReadOnlySet<string> Contexts { get; }

	/// <summary>Returns the bindings that currently dispatch to a command.</summary>
	/// <param name="command">The command whose current bindings are returned.</param>
	/// <returns>
	/// The bindings that currently dispatch to the command; an empty list when the command is not cataloged, was explicitly unbound,
	/// or has every binding shadowed by another command's override.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="command"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// A catalog default that another command's override shadows is omitted in the override's context until that override is removed;
	/// the command's stored bindings are unchanged. Bindings are returned in stored order: catalog declaration order for a command
	/// without an override, otherwise the order of its override entry.
	/// </remarks>
	IReadOnlyList<KeyChord> GetBindings(TCommandId command);

	/// <summary>
	/// Returns the display text for a command's current bindings, rendered by the configured <see cref="IKeyDisplayTextFormatter"/>.
	/// </summary>
	/// <param name="command">The command whose display text is returned.</param>
	/// <param name="fallbackDisplayText">The text returned when the command has no bindings.</param>
	/// <returns>The display text for the command's bindings, or <paramref name="fallbackDisplayText"/> when the command has no bindings.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="command"/> or <paramref name="fallbackDisplayText"/> is <see langword="null"/>.
	/// </exception>
	/// <remarks>
	/// Multiple bindings are joined with <c> / </c> in stored order.
	/// </remarks>
	string GetDisplayText(TCommandId command, string fallbackDisplayText = "");

	/// <summary>Validates a proposed binding set for a command without mutating state, under a conflict policy.</summary>
	/// <param name="command">The command the proposed binding set applies to.</param>
	/// <param name="bindings">The proposed binding set to validate.</param>
	/// <param name="conflictPolicy">The policy that controls how chords owned by other commands are treated.</param>
	/// <returns>The outcome of the validation.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="command"/> or <paramref name="bindings"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="bindings"/> contains an uninitialized key chord, or <paramref name="conflictPolicy"/> is not a defined policy value.
	/// </exception>
	/// <remarks>
	/// <para>
	/// The outcome accounts for the command's remapping policy, repeated chords, conflicts with other commands, and the state
	/// the change itself produces. The policy is applied exactly as <see cref="Apply"/> applies it, so a <see cref="KeyBindingOutcome.Succeeded"/>
	/// outcome means the proposal passes the checks <see cref="Apply"/>
	/// makes before it persists, although <see cref="Apply"/> can still return <see cref="KeyBindingOutcome.PersistenceFailed"/>; a rejected proposal
	/// under the default <see cref="KeyBindingConflictPolicy.Reject"/> does not say whether <see cref="KeyBindingConflictPolicy.Replace"/>
	/// would accept it. The state is not mutated.
	/// </para>
	/// <para>
	/// Failures are reported in this order: <see cref="KeyBindingOutcome.UnknownCommand"/>, <see cref="KeyBindingOutcome.HostReserved"/>,
	/// <see cref="KeyBindingOutcome.HostManaged"/>, <see cref="KeyBindingOutcome.DuplicateInCommand"/>, <see cref="KeyBindingOutcome.PrefixConflict"/>,
	/// then <see cref="KeyBindingOutcome.Conflict"/>.
	/// </para>
	/// </remarks>
	KeyBindingOutcome Validate(
		TCommandId command,
		IReadOnlyList<KeyChord> bindings,
		KeyBindingConflictPolicy conflictPolicy = KeyBindingConflictPolicy.Reject
	);

	/// <summary>Replaces a command's binding set, persists it through the overrides store, and publishes the new runtime maps.</summary>
	/// <param name="command">The command to rebind.</param>
	/// <param name="bindings">The binding set to store for the command.</param>
	/// <param name="conflictPolicy">The policy that controls how chords owned by other commands are treated.</param>
	/// <returns>The outcome of the operation.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="command"/> or <paramref name="bindings"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="bindings"/> contains an uninitialized key chord, or <paramref name="conflictPolicy"/> is not a defined policy value.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// The accepted binding set cannot produce a consistent runtime state. This indicates an internal invariant violation
	/// rather than a rejected proposal; the runtime state and the overrides store are left unchanged.
	/// </exception>
	/// <remarks>
	/// <para>
	/// The proposal is checked against the remapping policy, repeated chords,
	/// conflicts with other commands, and prefix relationships. With <see cref="KeyBindingConflictPolicy.Reject"/> any conflict fails validation;
	/// with <see cref="KeyBindingConflictPolicy.Replace"/> the proposal takes precedence - a conflicting catalog
	/// default stays stored but is shadowed, and a conflicting binding is removed from the other command's stored override entry, which is deleted
	/// when no bindings remain. A conflict with a command that is not <see cref="CommandRemappingPolicy.Remappable"/> is rejected for both policies,
	/// and a prefix relationship is reported as <see cref="KeyBindingOutcome.PrefixConflict"/> for both.
	/// </para>
	/// <para>
	/// An empty binding set stores an explicit override that unbinds the command.
	/// Every successful call stores and persists an override entry and raises <see cref="BindingsChanged"/>, even when the proposed
	/// set equals the current bindings. Each call persists the whole override snapshot, so a host that rebinds many commands pays one store
	/// write per call. The snapshot is persisted before the runtime state changes: when the store reports failure or throws, no runtime state changes
	/// and <see cref="KeyBindingOutcome.PersistenceFailed"/> is returned (or the exception propagates).
	/// </para>
	/// </remarks>
	KeyBindingOutcome Apply(
		TCommandId command,
		IReadOnlyList<KeyChord> bindings,
		KeyBindingConflictPolicy conflictPolicy = KeyBindingConflictPolicy.Reject
	);

	/// <summary>Removes the override for a single command, falling back to catalog defaults.</summary>
	/// <param name="command">The command whose override is removed.</param>
	/// <returns>The outcome of the operation.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="command"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">
	/// The resulting binding set cannot produce a consistent runtime state. This indicates an internal invariant violation; the runtime state
	/// and the overrides store are left unchanged.
	/// </exception>
	/// <remarks>
	/// The operation is a no-op that returns <see cref="KeyBindingOutcome.Succeeded"/>
	/// when the command has no override, and returns <see cref="KeyBindingOutcome.UnknownCommand"/> for an uncataloged command.
	/// It is not policy-checked, so it removes the override for any cataloged command. Removing the entry restores the command's catalog defaults,
	/// and the shared validation covers them. A restored default that collides with another command's binding is refused with no state change.
	/// A default that another command's override shadows is restored but stays shadowed, so the command can end up with no dispatching binding
	/// while the operation still reports <see cref="KeyBindingOutcome.Succeeded"/>.
	/// </remarks>
	KeyBindingOutcome Reset(TCommandId command);

	/// <summary>Removes all overrides for cataloged commands, falling back to catalog defaults.</summary>
	/// <returns>The outcome of the operation.</returns>
	/// <exception cref="InvalidOperationException">
	/// The resulting binding set cannot produce a consistent runtime state. This indicates an internal invariant violation; the runtime state
	/// and the overrides store are left unchanged.
	/// </exception>
	/// <remarks>
	/// Preserved entries (overrides whose command the catalog does not contain) are kept. The operation is a no-op that returns
	/// <see cref="KeyBindingOutcome.Succeeded"/> when the applied overrides contain no cataloged entry. Every restored default is validated
	/// against the rest of the state, so the operation reports <see cref="KeyBindingOutcome.Conflict"/> without changing anything when a restored
	/// default collides with an override entry it preserves.
	/// </remarks>
	KeyBindingOutcome ResetAll();
}

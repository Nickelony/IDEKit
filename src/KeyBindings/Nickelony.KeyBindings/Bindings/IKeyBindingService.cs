using System.Diagnostics.CodeAnalysis;

namespace Nickelony.KeyBindings;

/// <summary>
/// Provides runtime key binding lookup, display text, validation, rebinding, and reset operations,
/// plus a <see cref="BindingsChanged"/> notification.
/// </summary>
/// <typeparam name="TCommandId">The command identity type.</typeparam>
/// <remarks>
/// <para>
/// Mutations are expected from a single owning thread, typically the host's input thread. The
/// published lookup maps are swapped as one immutable snapshot, so a concurrent reader never
/// observes data from two different rebuilds.
/// </para>
/// <para>
/// Mutations persist before they publish: the complete candidate state is validated, handed to the
/// overrides store, and only applied when the store reports success. A failed store call or an
/// exception thrown by the store leaves the runtime state unchanged.
/// </para>
/// <para>
/// A command's bindings can be scoped to a context token that names a mutually exclusive host
/// state, such as one of an editor's view modes; <see langword="null"/> means always active. Only
/// <see langword="null"/> is the always-active context token. An empty or whitespace-only token is
/// not a token the catalog declares, so a lookup with one resolves only the always-active bindings,
/// exactly like any other token the catalog does not declare. Lookup and prefix probes take the
/// active token, and two bindings only
/// compete when their contexts can be active at the same time, so the same chord can be bound in two
/// different contexts.
/// </para>
/// <para>
/// <see cref="Validate"/>, <see cref="Apply"/>, <see cref="Reset"/>, and <see cref="ResetAll"/> share one
/// mutation pipeline. A mutation plans the override state the operation would produce and validates that
/// state, not only the proposal it started from, so a <see cref="KeyBindingOutcome.Conflict"/> or
/// <see cref="KeyBindingOutcome.PrefixConflict"/> can describe a binding another command keeps or regains
/// when the operation removes or restores an entry. Override entries whose command the catalog does not
/// contain are preserved in the snapshots the service writes, and a claim that would collide with one is
/// refused for every policy, because a later run may catalog that command and would then reject the
/// document.
/// </para>
/// <para>
/// A semantic rejection is reported as a <see cref="KeyBindingOutcome"/> and leaves the state unchanged.
/// A malformed call - an uninitialized key chord or an undefined policy value - throws instead, because it
/// is a programming error rather than a user outcome.
/// </para>
/// <para>
/// The <c>command</c> parameters reject <see langword="null"/> because <typeparamref name="TCommandId"/> is
/// unconstrained. With a value-type identity - the common enum case - that guard can never trigger, so an
/// unknown command is reported through the operation's result (an empty binding list, a fallback text, or
/// <see cref="KeyBindingOutcome.UnknownCommand"/>) rather than by throwing.
/// </para>
/// </remarks>
public interface IKeyBindingService<TCommandId>
	where TCommandId : notnull
{
	/// <summary>
	/// Raised after the runtime maps are rebuilt by an <see cref="Apply"/>, <see cref="Reset"/>, or
	/// <see cref="ResetAll"/> operation.
	/// </summary>
	/// <remarks>
	/// Consumers can use this notification to refresh command presentation. The event data names the
	/// command whose bindings changed, or sets <see cref="KeyBindingsChangedEventArgs{TCommandId}.AllCommands"/>
	/// for a <see cref="ResetAll"/> that could change every command - a flag rather than a null command,
	/// which a value-type identity cannot produce - so a consumer refreshes only the presentation it must.
	/// An exception thrown by a handler propagates to the operation that raised the event, after the state
	/// was committed.
	/// </remarks>
	event EventHandler<KeyBindingsChangedEventArgs<TCommandId>>? BindingsChanged;

	/// <summary>
	/// Looks up the <typeparamref name="TCommandId"/> currently bound to a key chord in a context.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Each chord identifies at most one command per context. A binding of the requested context
	/// takes precedence over an always-active binding for the same chord. A single keystroke is a
	/// one-stroke chord, so a <see cref="KeyCombo"/> can be passed directly.
	/// </para>
	/// <para>
	/// A binding from an override entry takes precedence over another command's catalog default for
	/// the same chord and context.
	/// </para>
	/// </remarks>
	/// <param name="keyChord">The key chord to look up.</param>
	/// <param name="context">
	/// The active context token, or <see langword="null"/> for the always-active context. A host with
	/// no contexts passes <see langword="null"/>. Use <see cref="Contexts"/> to validate a token against
	/// the catalog; the type remarks state how an undeclared or empty token resolves.
	/// </param>
	/// <param name="command">The command bound to the chord when the method returns <see langword="true"/>; otherwise, the <see langword="default"/> command value.</param>
	/// <returns><see langword="true"/> when the chord is bound to a command; otherwise, <see langword="false"/>.</returns>
	bool TryGetCommand(KeyChord keyChord, string? context, [NotNullWhen(true)] out TCommandId? command);

	/// <summary>
	/// Determines whether a chord is a strict prefix of at least one bound chord in a context.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A dispatcher uses this probe to decide whether a keystroke starts or continues a chord that
	/// could still complete. A bound chord is never itself reported as a prefix: the catalog and the
	/// override validation reject a bound chord that is a strict prefix of a co-active bound chord.
	/// </para>
	/// <para>
	/// The probe is scoped exactly like <see cref="TryGetCommand"/>: a prefix that is declared only in
	/// another context is not reported here.
	/// </para>
	/// </remarks>
	/// <param name="keyChord">The chord to test.</param>
	/// <param name="context">
	/// The active context token, or <see langword="null"/> for the always-active context. A host with
	/// no contexts passes <see langword="null"/>; the type remarks state how an undeclared or empty token
	/// resolves.
	/// </param>
	/// <returns>
	/// <see langword="true"/> when a longer bound chord that is active in <paramref name="context"/> starts
	/// with <paramref name="keyChord"/>; otherwise, <see langword="false"/>.
	/// </returns>
	bool IsChordPrefix(KeyChord keyChord, string? context);

	/// <summary>
	/// Gets every context token the catalog declares; empty when the catalog declares none.
	/// </summary>
	/// <remarks>
	/// A host can compare the token it publishes with this set to fail fast on a typo: a token the catalog
	/// does not declare resolves only the always-active bindings, exactly like <see langword="null"/>, so a
	/// typo would silently disable context scoping rather than never match. The comparison is ordinal,
	/// matching the rest of the package.
	/// </remarks>
	IReadOnlySet<string> Contexts { get; }

	/// <summary>
	/// Returns the bindings that currently dispatch to a command.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A catalog default that another command's override shadows is omitted until that override is
	/// removed; the command's stored bindings, which are its override entry or its catalog defaults,
	/// are unchanged.
	/// </para>
	/// <para>
	/// Bindings are returned in stored order: catalog declaration order for a command without an
	/// override, otherwise the order of its override entry.
	/// </para>
	/// </remarks>
	/// <param name="command">The command whose current bindings are returned.</param>
	/// <returns>
	/// The bindings that currently dispatch to the command; an empty list when the command is not
	/// cataloged, was explicitly unbound, or has every binding shadowed by another command's override.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="command"/> is <see langword="null"/>.</exception>
	IReadOnlyList<KeyChord> GetBindings(TCommandId command);

	/// <summary>
	/// Returns the display text for a command's current bindings, rendered by the configured
	/// <see cref="IKeyDisplayTextFormatter"/>.
	/// </summary>
	/// <remarks>Multiple bindings are joined with <c> / </c> in stored order.</remarks>
	/// <param name="command">The command whose display text is returned.</param>
	/// <param name="fallbackDisplayText">The text returned when the command has no bindings.</param>
	/// <returns>The display text for the command's bindings, or <paramref name="fallbackDisplayText"/> when the command has no bindings.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="command"/> or <paramref name="fallbackDisplayText"/> is <see langword="null"/>.</exception>
	string GetDisplayText(TCommandId command, string fallbackDisplayText = "");

	/// <summary>
	/// Validates a proposed binding set for a command without mutating state, under a conflict policy.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The result accounts for the command's remapping policy, repeated chords within the set, conflicts
	/// with the current bindings of other commands, and the state the change itself produces, as the
	/// interface remarks describe. The conflict policy is applied exactly as <see cref="Apply"/> applies
	/// it, so a <see cref="KeyBindingOutcome.Succeeded"/> result means <see cref="Apply"/> accepts the
	/// proposal too. The state is not mutated.
	/// </para>
	/// <para>
	/// The policy defaults to <see cref="KeyBindingConflictPolicy.Reject"/>, so a
	/// <see cref="KeyBindingOutcome.Conflict"/> result does not say whether
	/// <see cref="KeyBindingConflictPolicy.Replace"/> would accept the proposal; pass
	/// <see cref="KeyBindingConflictPolicy.Replace"/> to ask that question.
	/// </para>
	/// <para>
	/// Failures are reported in this order: <see cref="KeyBindingOutcome.UnknownCommand"/>,
	/// <see cref="KeyBindingOutcome.Reserved"/>, <see cref="KeyBindingOutcome.NotRemappable"/>,
	/// <see cref="KeyBindingOutcome.DuplicateInCommand"/>,
	/// <see cref="KeyBindingOutcome.PrefixConflict"/>, then
	/// <see cref="KeyBindingOutcome.Conflict"/>.
	/// </para>
	/// </remarks>
	/// <param name="command">The command the proposed binding set applies to.</param>
	/// <param name="bindings">The proposed binding set to validate.</param>
	/// <param name="conflictPolicy">The policy that controls how chords owned by other commands are treated.</param>
	/// <returns>The outcome of the validation.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="command"/> or <paramref name="bindings"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="bindings"/> contains an uninitialized key chord, or
	/// <paramref name="conflictPolicy"/> is not a defined policy value.
	/// </exception>
	KeyBindingOutcome Validate(TCommandId command, IReadOnlyList<KeyChord> bindings, KeyBindingConflictPolicy conflictPolicy = KeyBindingConflictPolicy.Reject);

	/// <summary>
	/// Replaces a command's binding set, persists it through the overrides store, and publishes the
	/// new runtime maps.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The proposal is checked against the remapping policy, repeated chords, conflicts with the
	/// current bindings of other commands, and prefix relationships with them. With
	/// <see cref="KeyBindingConflictPolicy.Reject"/>, any conflict fails validation. With
	/// <see cref="KeyBindingConflictPolicy.Replace"/>, the proposal takes precedence: a conflicting
	/// catalog default stays stored but is shadowed, and a conflicting binding is removed from the
	/// other command's stored override entry, which is deleted when no bindings remain so the command
	/// falls back to its catalog defaults. A conflict with a command that is not
	/// <see cref="CommandRemappingPolicy.Remappable"/> is rejected for both policies.
	/// </para>
	/// <para>
	/// A chord that is a strict prefix of another command's bound chord, or that another command's
	/// bound chord is a strict prefix of, is reported as <see cref="KeyBindingOutcome.PrefixConflict"/>
	/// for both policies: the shorter chord would always resolve first, so replacing the other
	/// command's binding cannot make both chords reachable.
	/// </para>
	/// <para>
	/// An empty binding set stores an explicit override that unbinds the command. Every successful
	/// call stores and persists an override entry and raises <see cref="BindingsChanged"/>, even when
	/// the proposed set equals the current bindings, because the settings document stays the
	/// authoritative record. The write is per call, so a host that rebinds many commands in a loop
	/// writes the document once per command; batch the changes, or write the document directly, when
	/// that cost matters. The outcome describes the state the operation produces, as the interface
	/// remarks describe.
	/// </para>
	/// <para>
	/// The override snapshot is persisted before the runtime state changes. When the store reports
	/// failure or throws, no runtime state changes and <see cref="KeyBindingOutcome.PersistenceFailed"/>
	/// is returned (or the exception propagates). The snapshot is built before the store call, so an
	/// operation that cannot build a consistent snapshot throws instead of returning an outcome.
	/// </para>
	/// </remarks>
	/// <param name="command">The command to rebind.</param>
	/// <param name="bindings">The binding set to store for the command.</param>
	/// <param name="conflictPolicy">The policy that controls how chords owned by other commands are treated.</param>
	/// <returns>The outcome of the operation.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="command"/> or <paramref name="bindings"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="bindings"/> contains an uninitialized key chord, or
	/// <paramref name="conflictPolicy"/> is not a defined policy value.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// The accepted binding set cannot produce a consistent runtime state. This indicates an internal
	/// invariant violation rather than a rejected proposal; the runtime state and the overrides store are
	/// left unchanged.
	/// </exception>
	KeyBindingOutcome Apply(TCommandId command, IReadOnlyList<KeyChord> bindings, KeyBindingConflictPolicy conflictPolicy = KeyBindingConflictPolicy.Reject);

	/// <summary>
	/// Removes the override for a single command, falling back to catalog defaults.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The operation is a no-op when the command has no override and returns
	/// <see cref="KeyBindingOutcome.Succeeded"/>; an uncataloged command returns
	/// <see cref="KeyBindingOutcome.UnknownCommand"/> without removing anything.
	/// </para>
	/// <para>
	/// The operation is not policy-checked: it removes the override for any cataloged command,
	/// including <see cref="CommandRemappingPolicy.HostManaged"/> and
	/// <see cref="CommandRemappingPolicy.HostReserved"/> commands (a host-reserved entry was already
	/// ignored while it was loaded).
	/// </para>
	/// <para>
	/// Removing the entry restores the command's catalog defaults, which the shared validation covers: a
	/// restored default that collides with another command's binding is refused with no state change, and
	/// a default that another command's override shadows is restored and stays shadowed, so the command
	/// can end up with no binding that dispatches while the operation still reports
	/// <see cref="KeyBindingOutcome.Succeeded"/>.
	/// </para>
	/// </remarks>
	/// <param name="command">The command whose override is removed.</param>
	/// <returns>The outcome of the operation.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="command"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">
	/// The resulting binding set cannot produce a consistent runtime state. This indicates an internal
	/// invariant violation; the runtime state and the overrides store are left unchanged.
	/// </exception>
	KeyBindingOutcome Reset(TCommandId command);

	/// <summary>
	/// Removes all overrides for cataloged commands, falling back to catalog defaults.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Preserved entries (overrides whose command the catalog does not contain) are kept, matching the
	/// preservation guarantee documented for written snapshots. The operation is a no-op when the
	/// applied overrides contain no cataloged entry and returns <see cref="KeyBindingOutcome.Succeeded"/>.
	/// </para>
	/// <para>
	/// Every restored default is validated against the rest of the state, as the interface remarks
	/// describe, so the operation reports <see cref="KeyBindingOutcome.Conflict"/> without changing
	/// anything when a default it restores collides with an override entry it preserves.
	/// </para>
	/// </remarks>
	/// <returns>The outcome of the operation.</returns>
	/// <exception cref="InvalidOperationException">
	/// The resulting binding set cannot produce a consistent runtime state. This indicates an internal
	/// invariant violation; the runtime state and the overrides store are left unchanged.
	/// </exception>
	KeyBindingOutcome ResetAll();
}

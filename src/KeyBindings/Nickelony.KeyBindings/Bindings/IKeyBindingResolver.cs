using System.Diagnostics.CodeAnalysis;

namespace Nickelony.KeyBindings;

/// <summary>Resolves key chords to commands in a context.</summary>
/// <typeparam name="TCommandId">The command identity type.</typeparam>
/// <remarks>
/// A dispatcher needs only this part of <see cref="IKeyBindingService{TCommandId}"/>, so a host that dispatches against its own binding source
/// implements this interface alone; the bundled <see cref="KeyBindingService{TCommandId}"/> implements the larger service contract, which extends it.
/// </remarks>
public interface IKeyBindingResolver<TCommandId>
	where TCommandId : notnull
{
	/// <summary>Looks up the <typeparamref name="TCommandId"/> currently bound to a key chord in a context.</summary>
	/// <param name="keyChord">The key chord to look up.</param>
	/// <param name="context">
	/// The active context token, or <see langword="null"/> for the always-active context. A host with no contexts passes <see langword="null"/>.
	/// Use <see cref="IKeyBindingService{TCommandId}.Contexts"/> to validate a token against the catalog.
	/// </param>
	/// <param name="command">
	/// The command bound to the chord when the method returns <see langword="true"/>; otherwise, the <see langword="default"/> command value.
	/// </param>
	/// <returns><see langword="true"/> when the chord is bound to a command; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// Each chord identifies at most one command per context: a binding of the requested context takes precedence over an always-active binding,
	/// and a binding from an override entry takes precedence over another command's catalog default. A single stroke is a one-stroke chord,
	/// so a <see cref="KeyStroke"/> can be passed directly.
	/// </remarks>
	bool TryGetCommand(KeyChord keyChord, string? context, [NotNullWhen(true)] out TCommandId? command);

	/// <summary>Determines whether a chord is a strict prefix of a bound chord, in a context, whose command a predicate accepts.</summary>
	/// <param name="keyChord">The chord to test.</param>
	/// <param name="context">
	/// The active context token, or <see langword="null"/> for the always-active context. A host with no contexts passes <see langword="null"/>.
	/// </param>
	/// <param name="commandPredicate">
	/// Decides whether a command counts. The probe calls it on the caller's thread for the commands of the bound chords that start with
	/// <paramref name="keyChord"/>, at most once per command, and stops at the first command it accepts.
	/// </param>
	/// <returns>
	/// <see langword="true"/> when a longer bound chord that is active in <paramref name="context"/> starts with <paramref name="keyChord"/> and
	/// belongs to a command that <paramref name="commandPredicate"/> accepts; otherwise, <see langword="false"/>.
	/// </returns>
	/// <remarks>
	/// A dispatcher passes the predicate that tells whether a command can execute, so a stroke starts or continues a chord only while a command that
	/// can run is still reachable from it, and a predicate that accepts every command asks about the bindings alone. The probe is scoped exactly like
	/// <see cref="TryGetCommand"/>, so a prefix declared only in another context is not reported; a bound chord is never itself reported
	/// as a prefix, because the catalog and the override validation reject a bound chord that is a strict prefix of a co-active bound chord.
	/// </remarks>
	bool IsChordPrefix(KeyChord keyChord, string? context, Func<TCommandId, bool> commandPredicate);
}

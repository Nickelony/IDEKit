using System.Collections.Frozen;
using System.Collections.Immutable;

namespace Nickelony.KeyBindings;

/// <summary>
/// The runtime lookup maps are published as one immutable snapshot, so a concurrent reader never
/// observes the command-to-bindings map and the chord-to-command map from different rebuilds.
/// <see cref="CommandsByChord"/> and <see cref="ChordPrefixes"/> are keyed by a context and a chord,
/// so the same chord can be bound in two contexts that are never active at the same time;
/// <see cref="ContextScopedChords"/> holds every chord bound in a context other than the always-active
/// one, which is how a co-active collision with an always-active binding is detected.
/// <see cref="DisplayTextByCommand"/> is the rendered display text of every command that has a
/// dispatching binding, formatted when the snapshot was built.
/// </summary>
/// <typeparam name="TCommandId">The command identity type.</typeparam>
/// <param name="BindingsByCommand">The bindings that dispatch per command, in stored order.</param>
/// <param name="CommandsByChord">The command a context-and-chord resolves to.</param>
/// <param name="ChordPrefixes">Every strict prefix of a dispatching chord, per context.</param>
/// <param name="ContextScopedChords">Every chord bound in a context other than the always-active one.</param>
internal sealed record PublishedMaps<TCommandId>(
	FrozenDictionary<TCommandId, ImmutableArray<KeyChord>> BindingsByCommand,
	FrozenDictionary<ContextChord, TCommandId> CommandsByChord,
	FrozenSet<ContextChord> ChordPrefixes,
	FrozenSet<KeyChord> ContextScopedChords)
	where TCommandId : notnull
{
	/// <summary>
	/// Gets the display text of every command that has a dispatching binding, in the form a host renders.
	/// </summary>
	/// <remarks>
	/// The text is precomputed because a host renders it on every menu or toolbar pass, and formatting it
	/// per read would re-run the formatter and the join for a value that only a rebuild can change. It
	/// travels inside the snapshot so a concurrent reader sees the texts and the bindings they were built
	/// from together. A command without a dispatching binding has no entry and falls back.
	/// </remarks>
	internal FrozenDictionary<TCommandId, string> DisplayTextByCommand { get; init; } =
		FrozenDictionary<TCommandId, string>.Empty;
}

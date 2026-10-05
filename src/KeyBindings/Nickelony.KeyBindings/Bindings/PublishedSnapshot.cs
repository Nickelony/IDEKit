using System.Collections.Frozen;
using System.Collections.Immutable;

namespace Nickelony.KeyBindings;

/// <summary>
/// The runtime lookup maps, published as one immutable snapshot so a concurrent reader never observes the command-to-bindings map
/// and the chord-to-command map from different rebuilds.
/// </summary>
/// <typeparam name="TCommandId">The command identity type.</typeparam>
/// <remarks>
/// <see cref="CommandsByChord"/> and <see cref="CommandsByChordPrefix"/> are keyed by a context and a chord, so the same chord
/// can be bound in two contexts that are never active at the same time. <see cref="ContextScopedChords"/> holds every chord bound in a context
/// other than the always-active one, which is how a co-active collision with an always-active binding is detected.
/// </remarks>
internal sealed class PublishedSnapshot<TCommandId>
	where TCommandId : notnull
{
	/// <summary>Creates a snapshot of the given maps, with no display text until <see cref="WithDisplayText"/> adds it.</summary>
	/// <param name="bindingsByCommand">The bindings that dispatch per command, in stored order.</param>
	/// <param name="commandsByChord">The command a context-and-chord resolves to.</param>
	/// <param name="commandsByChordPrefix">The commands whose dispatching chords start with each strict prefix, per context.</param>
	/// <param name="contextScopedChords">Every chord bound in a context other than the always-active one.</param>
	internal PublishedSnapshot(
		FrozenDictionary<TCommandId, ImmutableArray<KeyChord>> bindingsByCommand,
		FrozenDictionary<ContextChord, TCommandId> commandsByChord,
		FrozenDictionary<ContextChord, ImmutableArray<TCommandId>> commandsByChordPrefix,
		FrozenSet<KeyChord> contextScopedChords
	)
	{
		BindingsByCommand = bindingsByCommand;
		CommandsByChord = commandsByChord;
		CommandsByChordPrefix = commandsByChordPrefix;
		ContextScopedChords = contextScopedChords;
		DisplayTextByCommand = FrozenDictionary<TCommandId, string>.Empty;
	}

	/// <summary>The bindings that dispatch per command, in stored order.</summary>
	internal FrozenDictionary<TCommandId, ImmutableArray<KeyChord>> BindingsByCommand { get; }

	/// <summary>The command a context-and-chord resolves to.</summary>
	internal FrozenDictionary<ContextChord, TCommandId> CommandsByChord { get; }

	/// <summary>The commands whose dispatching chords start with each strict prefix, per context.</summary>
	/// <remarks>
	/// A key is a strict prefix of a dispatching chord together with the context of that chord, and its value lists each command that chord
	/// belongs to once, so a probe that asks whether a command can still complete the prefix reads one bucket.
	/// </remarks>
	internal FrozenDictionary<ContextChord, ImmutableArray<TCommandId>> CommandsByChordPrefix { get; }

	/// <summary>Every chord bound in a context other than the always-active one.</summary>
	internal FrozenSet<KeyChord> ContextScopedChords { get; }

	/// <summary>The display text of every command that has a dispatching binding, in the form a host renders.</summary>
	/// <remarks>
	/// Precomputed because a host renders it on every menu or toolbar pass, and it travels inside the snapshot so a concurrent reader sees the texts
	/// and the bindings they were built from together. A command without a dispatching binding has no entry and falls back.
	/// </remarks>
	internal FrozenDictionary<TCommandId, string> DisplayTextByCommand { get; private init; }

	/// <summary>Determines whether a predicate accepts a command whose dispatching chord starts with a prefix in one context.</summary>
	/// <param name="prefix">The strict prefix and the context of the chords that start with it.</param>
	/// <param name="commandPredicate">Decides whether a command counts; it is called once per command, until it accepts one.</param>
	/// <returns><see langword="true"/> when the predicate accepts such a command; otherwise, <see langword="false"/>.</returns>
	internal bool AcceptsCommandUnderPrefix(ContextChord prefix, Func<TCommandId, bool> commandPredicate)
	{
		if (!CommandsByChordPrefix.TryGetValue(prefix, out ImmutableArray<TCommandId> commands))
			return false;

		foreach (TCommandId command in commands)
		{
			if (commandPredicate(command))
				return true;
		}

		return false;
	}

	/// <summary>Determines whether any command other than one dispatches different bindings in a later snapshot.</summary>
	/// <param name="later">The snapshot to compare with.</param>
	/// <param name="excluded">The command to leave out of the comparison.</param>
	/// <returns>
	/// <see langword="true"/> when a command other than <paramref name="excluded"/> dispatches other chords, or the same chords
	/// in another order; otherwise, <see langword="false"/>.
	/// </returns>
	/// <remarks>
	/// Both snapshots come from one catalog, so each lists the same commands.
	/// </remarks>
	internal bool DiffersForCommandsOtherThan(PublishedSnapshot<TCommandId> later, TCommandId excluded)
	{
		foreach ((TCommandId command, ImmutableArray<KeyChord> bindings) in BindingsByCommand)
		{
			if (EqualityComparer<TCommandId>.Default.Equals(command, excluded))
				continue;

			if (
				!later.BindingsByCommand.TryGetValue(command, out ImmutableArray<KeyChord> laterBindings)
				|| !bindings.AsSpan().SequenceEqual(laterBindings.AsSpan())
			)
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>Returns a copy of the maps that carries the display text of each command with a dispatching binding.</summary>
	/// <param name="formatter">The formatter that renders each chord.</param>
	/// <remarks>
	/// The display text of a command is fixed by its bindings, so it is computed once when the maps are published instead of on every read.
	/// </remarks>
	internal PublishedSnapshot<TCommandId> WithDisplayText(IKeyDisplayTextFormatter formatter)
	{
		var displayTextByCommand = new Dictionary<TCommandId, string>(BindingsByCommand.Count);

		foreach (KeyValuePair<TCommandId, ImmutableArray<KeyChord>> entry in BindingsByCommand)
		{
			if (entry.Value.Length > 0)
				displayTextByCommand[entry.Key] = string.Join(" / ", entry.Value.Select(binding => formatter.GetDisplayText(binding)));
		}

		return new PublishedSnapshot<TCommandId>(BindingsByCommand, CommandsByChord, CommandsByChordPrefix, ContextScopedChords)
		{
			DisplayTextByCommand = displayTextByCommand.ToFrozenDictionary(),
		};
	}
}

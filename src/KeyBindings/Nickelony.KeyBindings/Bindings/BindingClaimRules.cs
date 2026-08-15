using System.Diagnostics.CodeAnalysis;

namespace Nickelony.KeyBindings;

/// <summary>
/// The conflict rules a set of chord claims must satisfy: a chord may not be claimed by two commands
/// whose contexts can be active at the same time, and a claimed chord may not be a strict prefix of
/// another co-active claimed chord. The catalog's default-claim validation, a map build - including
/// the catalog default an override shadows - and a mutation's candidate analysis all evaluate the same
/// rules through this type, so a rule change has a single home; each caller keeps its own diagnostics.
/// </summary>
internal static class BindingClaimRules
{
	/// <summary>
	/// Finds the existing claim that can be active at the same time as a candidate claim: the claim of
	/// the same context, the always-active claim, or - for an always-active candidate - a context-scoped
	/// claim, because the always-active context is active in every context. The owner is returned as
	/// well because a map build must describe the shadowed catalog default it collides with.
	/// </summary>
	/// <typeparam name="TCommandId">The command identity type.</typeparam>
	/// <param name="claims">The claims collected so far.</param>
	/// <param name="candidate">The claim under test, carrying its context token and chord.</param>
	/// <param name="conflictingClaim">Receives the co-active claim when the method returns <see langword="true"/>.</param>
	/// <param name="conflictingOwner">Receives the command that owns the co-active claim when the method returns <see langword="true"/>.</param>
	/// <returns><see langword="true"/> when a co-active claim already exists; otherwise, <see langword="false"/>.</returns>
	internal static bool TryGetCoActiveClaim<TCommandId>(
		IReadOnlyDictionary<ContextChord, TCommandId> claims,
		ContextChord candidate,
		out ContextChord conflictingClaim,
		[NotNullWhen(true)] out TCommandId? conflictingOwner)
		where TCommandId : notnull
	{
		var alwaysClaim = new ContextChord(ContextChord.Always, candidate.Chord);

		if (claims.TryGetValue(alwaysClaim, out TCommandId? alwaysOwner))
		{
			conflictingClaim = alwaysClaim;
			conflictingOwner = alwaysOwner;
			return true;
		}

		var scopedClaim = new ContextChord(candidate.Context, candidate.Chord);

		if (claims.TryGetValue(scopedClaim, out TCommandId? scopedOwner))
		{
			conflictingClaim = scopedClaim;
			conflictingOwner = scopedOwner;
			return true;
		}

		if (candidate.Context is not null)
		{
			conflictingClaim = default;
			conflictingOwner = default;
			return false;
		}

		foreach (KeyValuePair<ContextChord, TCommandId> claim in claims)
		{
			if (claim.Key.Context is not null && claim.Key.Chord == candidate.Chord)
			{
				conflictingClaim = claim.Key;
				conflictingOwner = claim.Value;
				return true;
			}
		}

		conflictingClaim = default;
		conflictingOwner = default;
		return false;
	}

	/// <summary>
	/// Determines whether a strict prefix of a claim is already claimed co-actively, which would make
	/// the claim unreachable because the shorter chord always resolves first. A prefix claimed in a
	/// different context leaves both chords reachable and is not a conflict.
	/// </summary>
	/// <typeparam name="TCommandId">The command identity type.</typeparam>
	/// <param name="claims">The claims collected so far.</param>
	/// <param name="contextScopedChords">The chords that have a context-scoped claim.</param>
	/// <param name="claim">The claim whose prefix is tested.</param>
	/// <param name="prefix">A strict prefix of <paramref name="claim"/>'s chord.</param>
	/// <returns><see langword="true"/> when the prefix is claimed in a co-active context; otherwise, <see langword="false"/>.</returns>
	internal static bool IsPrefixConflict<TCommandId>(
		IReadOnlyDictionary<ContextChord, TCommandId> claims,
		IReadOnlySet<KeyChord> contextScopedChords,
		ContextChord claim,
		KeyChord prefix)
		where TCommandId : notnull
		=> claims.ContainsKey(new ContextChord(claim.Context, prefix)) ||
			claims.ContainsKey(new ContextChord(ContextChord.Always, prefix)) ||
			(claim.Context is null && contextScopedChords.Contains(prefix));

	/// <summary>
	/// Determines whether a proposed chord has a strict prefix relationship with a bound chord of
	/// another command, in either direction, within a context. Chords the command already owns are
	/// ignored: the proposal replaces its whole stored set, so its own current chords disappear.
	/// </summary>
	/// <remarks>
	/// Only co-active contexts compete. A chord in a different context can never dispatch at the same
	/// time as the proposal, so a prefix relationship with it leaves both chords reachable.
	/// </remarks>
	/// <typeparam name="TCommandId">The command identity type.</typeparam>
	/// <param name="commandsByChord">The command a context-and-chord resolves to.</param>
	/// <param name="contextScopedChords">The chords that have a context-scoped claim.</param>
	/// <param name="chord">The proposed chord.</param>
	/// <param name="command">The command the proposal applies to.</param>
	/// <param name="context">The context token the proposal would use, or <see langword="null"/> for the always-active context.</param>
	/// <returns><see langword="true"/> when a co-active prefix relationship exists; otherwise, <see langword="false"/>.</returns>
	internal static bool HasPrefixRelationship<TCommandId>(
		IReadOnlyDictionary<ContextChord, TCommandId> commandsByChord,
		IReadOnlySet<KeyChord> contextScopedChords,
		KeyChord chord,
		TCommandId command,
		string? context)
		where TCommandId : notnull
	{
		var proposed = new ContextChord(context, chord);

		// The proposal is a strict prefix of a longer bound chord: the proposal resolves first, so the
		// longer chord could never dispatch.
		foreach (KeyValuePair<ContextChord, TCommandId> bound in commandsByChord)
		{
			if (!EqualityComparer<TCommandId>.Default.Equals(bound.Value, command) &&
				proposed.IsCoActiveWith(bound.Key) &&
				IsStrictPrefix(chord, bound.Key.Chord))
			{
				return true;
			}
		}

		// A bound chord is a strict prefix of the proposal: the bound chord resolves first, so the
		// proposal could never dispatch.
		ReadOnlySpan<KeyCombo> strokes = chord.Strokes;

		for (int length = 1; length < strokes.Length; length++)
		{
			var prefix = new KeyChord(strokes[..length].ToArray());

			if (IsChordClaimedByCoActiveBinding(commandsByChord, contextScopedChords, prefix, context, command))
				return true;
		}

		return false;
	}

	/// <summary>
	/// Resolves the command a chord dispatches to in a context: the context's own binding first, then
	/// the always-active binding. Only the requested context and the always-active context can be
	/// active at the same time, so those are the only buckets a lookup considers.
	/// </summary>
	/// <typeparam name="TCommandId">The command identity type.</typeparam>
	/// <param name="commandsByChord">The command a context-and-chord resolves to.</param>
	/// <param name="chord">The chord to resolve.</param>
	/// <param name="context">The active context token, or <see langword="null"/> for the always-active context.</param>
	/// <param name="owner">Receives the command the chord dispatches to when the method returns <see langword="true"/>.</param>
	/// <returns><see langword="true"/> when the chord is claimed in a co-active context; otherwise, <see langword="false"/>.</returns>
	internal static bool TryGetOwner<TCommandId>(
		IReadOnlyDictionary<ContextChord, TCommandId> commandsByChord,
		KeyChord chord,
		string? context,
		[NotNullWhen(true)] out TCommandId? owner)
		where TCommandId : notnull
	{
		// With no active token the two probes are the same key, so the dictionary is probed once.
		if (context is null)
			return commandsByChord.TryGetValue(new ContextChord(ContextChord.Always, chord), out owner);

		return commandsByChord.TryGetValue(new ContextChord(context, chord), out owner) ||
			commandsByChord.TryGetValue(new ContextChord(ContextChord.Always, chord), out owner);
	}

	/// <summary>
	/// Determines whether <paramref name="prefix"/> is a strict prefix of <paramref name="chord"/>, that
	/// is whether every stroke of <paramref name="prefix"/> starts <paramref name="chord"/> and the
	/// latter is longer.
	/// </summary>
	/// <param name="prefix">The candidate prefix.</param>
	/// <param name="chord">The chord the prefix is tested against.</param>
	/// <returns><see langword="true"/> when <paramref name="prefix"/> is a strict prefix of <paramref name="chord"/>; otherwise, <see langword="false"/>.</returns>
	internal static bool IsStrictPrefix(KeyChord prefix, KeyChord chord)
	{
		if (prefix.StrokeCount >= chord.StrokeCount)
			return false;

		ReadOnlySpan<KeyCombo> prefixStrokes = prefix.Strokes;
		ReadOnlySpan<KeyCombo> chordStrokes = chord.Strokes;

		for (int index = 0; index < prefixStrokes.Length; index++)
		{
			if (prefixStrokes[index] != chordStrokes[index])
				return false;
		}

		return true;
	}

	/// <summary>
	/// Determines whether a chord is claimed by a binding of another command that can be active at the
	/// same time as a context.
	/// </summary>
	/// <remarks>
	/// A command's own bindings all live in its single context, so a context-scoped claim of an
	/// always-active context always belongs to another command.
	/// </remarks>
	private static bool IsChordClaimedByCoActiveBinding<TCommandId>(
		IReadOnlyDictionary<ContextChord, TCommandId> commandsByChord,
		IReadOnlySet<KeyChord> contextScopedChords,
		KeyChord chord,
		string? context,
		TCommandId command)
		where TCommandId : notnull
	{
		if (TryGetOwner(commandsByChord, chord, context, out TCommandId? owner))
			return !EqualityComparer<TCommandId>.Default.Equals(owner, command);

		return context is null && contextScopedChords.Contains(chord);
	}
}

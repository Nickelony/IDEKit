using Nickelony.KeyBindings.OverrideEntries;

namespace Nickelony.KeyBindings;

/// <summary>Detects whether a proposed change to the bindings collides with the bindings that exist.</summary>
/// <typeparam name="TCommandId">The command identity type.</typeparam>
/// <remarks>
/// <see cref="Analyze"/> judges a proposal against the published maps. <see cref="CollidesWithPreservedEntry"/> judges a candidate
/// state against the override entries whose commands the catalog does not contain, which no map holds. The co-active claim and strict-prefix rules
/// are owned by <see cref="BindingClaimRules"/>.
/// </remarks>
internal sealed class KeyBindingConflictDetector<TCommandId>
	where TCommandId : notnull
{
	private readonly CommandCatalog<TCommandId> _catalog;
	private readonly IReadOnlySet<string> _catalogSerializedIds;
	private readonly KeyBindingOverrideCodec _overrideCodec;

	/// <param name="catalog">The command catalog that supplies the descriptor of a command that owns a colliding chord.</param>
	/// <param name="catalogSerializedIds">
	/// The serialized identifiers of every cataloged command; an override entry outside this set is preserved.
	/// </param>
	/// <param name="overrideCodec">The codec that parses the bindings of a preserved entry.</param>
	internal KeyBindingConflictDetector(
		CommandCatalog<TCommandId> catalog,
		IReadOnlySet<string> catalogSerializedIds,
		KeyBindingOverrideCodec overrideCodec
	)
	{
		_catalog = catalog;
		_catalogSerializedIds = catalogSerializedIds;
		_overrideCodec = overrideCodec;
	}

	/// <summary>Analyzes the proposed bindings against the published maps under a conflict policy.</summary>
	/// <param name="descriptor">The descriptor of the command the proposal applies to.</param>
	/// <param name="bindings">The proposed binding set.</param>
	/// <param name="conflictPolicy">The policy that controls how chords owned by other commands are treated.</param>
	/// <param name="published">The published maps to analyze against.</param>
	/// <param name="conflictsByOwner">Receives the conflicting chords per command when the analysis succeeds.</param>
	/// <returns>
	/// <see cref="KeyBindingOutcome.Succeeded"/> when the proposal passes the policy,
	/// <see cref="KeyBindingOutcome.PrefixConflict"/> for a strict prefix relationship, or <see cref="KeyBindingOutcome.Conflict"/> when the policy
	/// rejects an exact collision.
	/// </returns>
	/// <remarks>
	/// The policy is applied exactly as the service's apply path applies it, and a prefix relationship is reported before the exact
	/// conflicts and is never resolved by either policy. Only bindings co-active with the proposal compete; an always-active proposal
	/// competes with every context, including context-scoped bindings a lookup of the always-active context alone never reaches. One snapshot backs
	/// the whole analysis.
	/// </remarks>
	internal KeyBindingOutcome Analyze(
		CommandDescriptor<TCommandId> descriptor,
		IReadOnlyList<KeyChord> bindings,
		KeyBindingConflictPolicy conflictPolicy,
		PublishedSnapshot<TCommandId> published,
		out Dictionary<TCommandId, List<KeyChord>> conflictsByOwner
	)
	{
		conflictsByOwner = [];

		string? context = descriptor.Context;

		// A prefix relationship is a declaration error rather than a competing claim: the shorter chord
		// always resolves first, so the longer one could never dispatch. It is reported before the exact
		// conflicts so that Replace cannot silently accept a set that leaves one of its chords unreachable.
		foreach (KeyChord binding in bindings)
		{
			if (
				BindingClaimRules.HasPrefixRelationship(
					commandsByChord: published.CommandsByChord,
					contextScopedChords: published.ContextScopedChords,
					chord: binding,
					command: descriptor.Command,
					context
				) || IsPrefixOfAnotherInSet(bindings, binding)
			)
			{
				return KeyBindingOutcome.PrefixConflict;
			}
		}

		foreach (KeyChord binding in bindings)
		{
			if (
				!BindingClaimRules.TryGetOwner(commandsByChord: published.CommandsByChord, chord: binding, context, out TCommandId? owner)
				|| EqualityComparer<TCommandId>.Default.Equals(owner, descriptor.Command)
			)
			{
				// A lookup of the always-active context alone never reaches a context-scoped binding, but an
				// always-active proposal is active in every context, so a context-scoped claim still
				// competes with it.
				if (context is not null || !published.ContextScopedChords.Contains(binding))
					continue;

				return KeyBindingOutcome.Conflict;
			}

			if (
				conflictPolicy == KeyBindingConflictPolicy.Reject
				|| !_catalog.TryGetDescriptor(owner, out CommandDescriptor<TCommandId>? ownerDescriptor)
				|| ownerDescriptor.RemappingPolicy != CommandRemappingPolicy.Remappable
			)
			{
				return KeyBindingOutcome.Conflict;
			}

			if (!conflictsByOwner.TryGetValue(owner, out List<KeyChord>? ownerConflicts))
			{
				ownerConflicts = [];
				conflictsByOwner.Add(owner, ownerConflicts);
			}

			ownerConflicts.Add(binding);
		}

		return KeyBindingOutcome.Succeeded;
	}

	/// <summary>
	/// Determines whether a candidate introduces a claim that collides with a preserved override entry: one whose serialized identifier the catalog
	/// does not contain.
	/// </summary>
	/// <param name="candidate">The runtime maps of the candidate state.</param>
	/// <param name="appliedOverrides">The override state currently applied.</param>
	/// <param name="currentPublished">The runtime maps currently published.</param>
	/// <returns><see langword="true"/> when a newly introduced claim collides with a preserved entry; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// A preserved entry is not in the runtime maps, but a later run may catalog its command, so a colliding claim (the same chord, or a strict
	/// prefix relationship in either direction) is refused. Only claims the current state does not already hold are compared, and preserved strokes
	/// are parsed without logging.
	/// </remarks>
	internal bool CollidesWithPreservedEntry(
		PublishedSnapshot<TCommandId> candidate,
		KeyBindingOverrides appliedOverrides,
		PublishedSnapshot<TCommandId> currentPublished
	)
	{
		foreach (KeyBindingOverrideEntry entry in appliedOverrides.Entries)
		{
			if (_catalogSerializedIds.Contains(entry.SerializedId))
				continue;

			foreach (KeyBindingOverrideChord binding in entry.Bindings)
			{
				if (_overrideCodec.ParseBinding(binding, entry.SerializedId, logDiagnostics: false) is not KeyChord preserved)
					continue;

				foreach (ContextChord claim in candidate.CommandsByChord.Keys)
				{
					if (currentPublished.CommandsByChord.ContainsKey(claim))
						continue;

					if (
						preserved == claim.Chord
						|| BindingClaimRules.IsStrictPrefix(preserved, claim.Chord)
						|| BindingClaimRules.IsStrictPrefix(claim.Chord, preserved)
					)
					{
						return true;
					}
				}
			}
		}

		return false;
	}

	/// <summary>Determines whether a chord in a proposed set is a strict prefix of another chord in the same set.</summary>
	private static bool IsPrefixOfAnotherInSet(IReadOnlyList<KeyChord> bindings, KeyChord chord)
	{
		foreach (KeyChord candidate in bindings)
		{
			if (BindingClaimRules.IsStrictPrefix(chord, candidate))
				return true;
		}

		return false;
	}
}

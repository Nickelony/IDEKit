using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Nickelony.KeyBindings.OverrideEntries;

namespace Nickelony.KeyBindings;

/// <summary>Builds the runtime lookup maps from an override state.</summary>
/// <typeparam name="TCommandId">The command identity type.</typeparam>
/// <remarks>
/// <see cref="KeyBindingService{TCommandId}"/> delegates every map build here. Override entries are parsed by <see cref="KeyBindingOverrideCodec"/>
/// and the co-active claim and strict-prefix rules are owned by <see cref="BindingClaimRules"/>, so this type keeps the map construction,
/// the shadowed-default policy, and the map-specific diagnostics.
/// </remarks>
internal sealed partial class KeyBindingMapBuilder<TCommandId>
	where TCommandId : notnull
{
	[LoggerMessage(
		EventId = 4000,
		EventName = "HostReservedOverrideIgnored",
		Level = LogLevel.Warning,
		Message = "Key binding override for host-reserved command '{SerializedId}' ignored. Using catalog defaults."
	)]
	private static partial void LogHostReservedOverrideIgnored(ILogger logger, string serializedId);

	[LoggerMessage(
		EventId = 4001,
		EventName = "AllOverrideBindingsInvalid",
		Level = LogLevel.Warning,
		Message = "All override bindings for command '{SerializedId}' were invalid. Falling back to catalog defaults."
	)]
	private static partial void LogAllOverrideBindingsInvalid(ILogger logger, string serializedId);

	private readonly ILogger _logger;
	private readonly KeyBindingOverrideCodec _overrideCodec;
	private readonly IKeyDisplayTextFormatter _displayTextFormatter;
	private readonly CommandCatalog<TCommandId> _catalog;

	/// <param name="catalog">The command catalog that supplies descriptors and default bindings.</param>
	/// <param name="logger">The logger for override diagnostics.</param>
	/// <param name="displayTextFormatter">The formatter that describes chords in a build failure.</param>
	internal KeyBindingMapBuilder(CommandCatalog<TCommandId> catalog, ILogger logger, IKeyDisplayTextFormatter displayTextFormatter)
	{
		_catalog = catalog;
		_logger = logger;
		_displayTextFormatter = displayTextFormatter;

		// The codec is stateless apart from its logger, so each owner constructs its own instance.
		_overrideCodec = new KeyBindingOverrideCodec(logger);
	}

	/// <summary>Builds the runtime maps for an override state, or reports why the state cannot be built.</summary>
	/// <param name="overrides">The override state to build.</param>
	/// <param name="logDiagnostics">
	/// <see langword="true"/> to log override diagnostics; otherwise, <see langword="false"/>. Only the initial load passes <see langword="true"/>.
	/// </param>
	/// <returns>The maps, or the reason the state cannot be built.</returns>
	/// <remarks>
	/// Catalog defaults are claimed by their commands unless an override entry shadows them, and an override entry replaces
	/// its command's defaults. The state is rejected when two commands claim one chord the rules disallow, when one bound chord is a strict prefix
	/// of another co-active bound chord, or when one serialized identifier has two entries.
	/// </remarks>
	internal MapBuildResult Build(KeyBindingOverrides overrides, bool logDiagnostics)
	{
		var entriesById = new Dictionary<string, KeyBindingOverrideEntry>(StringComparer.Ordinal);

		foreach (KeyBindingOverrideEntry entry in overrides.Entries)
		{
			if (!entriesById.TryAdd(entry.SerializedId, entry))
			{
				return MapBuildResult.Fail(
					MapBuildOutcome.DuplicateEntry,
					$"The overrides contain more than one entry for command '{entry.SerializedId}'."
				);
			}
		}

		var defaultClaims = new Dictionary<ContextChord, TCommandId>();
		var overrideClaims = new Dictionary<ContextChord, TCommandId>();
		var resolved = new List<ResolvedBindings>(_catalog.Descriptors.Count);

		foreach (CommandDescriptor<TCommandId> descriptor in _catalog.Descriptors)
		{
			entriesById.TryGetValue(descriptor.SerializedId, out KeyBindingOverrideEntry? entry);

			string? context = descriptor.Context;

			bool usesOverride = entry is not null && entry.Bindings.Count > 0 && descriptor.RemappingPolicy != CommandRemappingPolicy.HostReserved;

			IReadOnlyList<KeyChord> bindings;

			if (usesOverride)
			{
				List<KeyChord> parsed = _overrideCodec.ParseEntryBindings(entry!, descriptor.SerializedId, logDiagnostics);

				if (parsed.Count == 0)
				{
					if (logDiagnostics)
						LogAllOverrideBindingsInvalid(_logger, descriptor.SerializedId);

					usesOverride = false;
					bindings = descriptor.DefaultBindings;
				}
				else
				{
					bindings = parsed;
				}
			}
			else if (entry is not null && descriptor.RemappingPolicy == CommandRemappingPolicy.HostReserved)
			{
				// A host-reserved command ignores every loaded override, including an empty binding
				// list that would otherwise explicitly unbind it.
				if (logDiagnostics)
					LogHostReservedOverrideIgnored(_logger, descriptor.SerializedId);

				bindings = descriptor.DefaultBindings;
			}
			else if (entry is not null && entry.Bindings.Count == 0)
			{
				bindings = []; // An empty override explicitly unbinds the command.
			}
			else
			{
				bindings = descriptor.DefaultBindings;
			}

			if (usesOverride)
			{
				foreach (KeyChord chord in bindings)
				{
					if (
						BindingClaimRules.TryGetCoActiveClaim(
							claims: overrideClaims,
							candidate: new ContextChord(context, chord),
							out ContextChord conflictingClaim,
							out TCommandId? conflictingOwner
						)
					)
					{
						return MapBuildResult.Fail(
							MapBuildOutcome.ClaimConflict,

							DescribeOverrideClaimConflict(
								chord,
								ownerClaim: conflictingClaim,
								owner: conflictingOwner,
								context,
								command: descriptor.Command
							)
						);
					}

					overrideClaims[new ContextChord(context, chord)] = descriptor.Command;
				}
			}
			else
			{
				// The catalog guarantees that no two descriptors claim a default binding in co-active contexts.
				foreach (KeyChord chord in bindings)
					defaultClaims[new ContextChord(context, chord)] = descriptor.Command;
			}

			resolved.Add(new ResolvedBindings(descriptor.Command, context, bindings));
		}

		// An override may take a chord over only where the runtime rules allow it.
		foreach (KeyValuePair<ContextChord, TCommandId> claim in overrideClaims)
		{
			if (
				!BindingClaimRules.TryGetCoActiveClaim(
					claims: defaultClaims,
					candidate: claim.Key,
					conflictingClaim: out ContextChord shadowedClaim,
					conflictingOwner: out TCommandId? shadowedOwner
				)
			)
			{
				continue;
			}

			bool shadowsScopedDefault = claim.Key.Context is null && shadowedClaim.Context is not null;

			if (
				shadowsScopedDefault
				|| !_catalog.TryGetDescriptor(shadowedOwner, out CommandDescriptor<TCommandId>? shadowedDescriptor)
				|| shadowedDescriptor.RemappingPolicy != CommandRemappingPolicy.Remappable
			)
			{
				return MapBuildResult.Fail(
					MapBuildOutcome.ClaimConflict,
					DescribeShadowedDefault(claim: claim.Key, shadowedClaim, shadowedOwner, owner: claim.Value, shadowsScopedDefault)
				);
			}
		}

		var commandsByChord = new Dictionary<ContextChord, TCommandId>(defaultClaims);

		foreach (KeyValuePair<ContextChord, TCommandId> claim in overrideClaims)
			commandsByChord[claim.Key] = claim.Value;

		var bindingsByCommand = new Dictionary<TCommandId, ImmutableArray<KeyChord>>();

		foreach (ResolvedBindings resolvedEntry in resolved)
		{
			bindingsByCommand[resolvedEntry.Command] =
			[
				.. resolvedEntry.Bindings.Where(binding =>
					commandsByChord.TryGetValue(new ContextChord(resolvedEntry.Context, binding), out TCommandId? owner)
					&& EqualityComparer<TCommandId>.Default.Equals(owner, resolvedEntry.Command)
				),
			];
		}

		// Every strict prefix of a dispatching chord is collected with the commands that chord belongs to,
		// so the dispatcher can tell a chord in progress from an unbound stroke, and a chord that a command
		// can still complete from one that none can. A prefix belongs to the context of its chord, so a
		// probe in that context - or in any context, for an always-active chord - reports the chord in
		// progress. A dispatching chord that is itself a prefix of a co-active chord makes the longer chord
		// unreachable, so that state is rejected instead of being built.
		var commandsByChordPrefix = new Dictionary<ContextChord, List<TCommandId>>();
		var contextScopedChords = new HashSet<KeyChord>();

		foreach (ContextChord claim in commandsByChord.Keys)
		{
			if (claim.Context is not null)
				contextScopedChords.Add(claim.Chord);
		}

		foreach ((ContextChord claim, TCommandId owner) in commandsByChord)
		{
			foreach (KeyChord prefix in BindingClaimRules.GetStrictPrefixes(claim.Chord))
			{
				if (BindingClaimRules.IsPrefixConflict(claims: commandsByChord, contextScopedChords, claim, prefix))
					return MapBuildResult.Fail(MapBuildOutcome.PrefixConflict, DescribePrefixConflict(claim.Context, prefix, claim.Chord));

				var prefixKey = new ContextChord(claim.Context, prefix);

				if (!commandsByChordPrefix.TryGetValue(prefixKey, out List<TCommandId>? commands))
				{
					commands = [];
					commandsByChordPrefix.Add(prefixKey, commands);
				}

				if (!commands.Contains(owner))
					commands.Add(owner);
			}
		}

		return MapBuildResult.Ok(
			new PublishedSnapshot<TCommandId>(
				bindingsByCommand: bindingsByCommand.ToFrozenDictionary(),
				commandsByChord: commandsByChord.ToFrozenDictionary(),
				commandsByChordPrefix: commandsByChordPrefix.ToFrozenDictionary(entry => entry.Key, entry => entry.Value.ToImmutableArray()),
				contextScopedChords: contextScopedChords.ToFrozenSet()
			)
		);
	}

	private string DescribeChord(KeyChord chord) => _displayTextFormatter.GetDisplayText(chord);

	/// <summary>Describes the context a claim or a binding belongs to.</summary>
	/// <param name="context">The context token, or <see cref="ContextChord.Always"/> for the always-active context.</param>
	private static string DescribeContext(string? context) => context is null ? "the always-active context" : $"context '{context}'";

	/// <summary>Describes two same-chord override claims that cannot both hold because their contexts can be active at the same time.</summary>
	private string DescribeOverrideClaimConflict(KeyChord chord, ContextChord ownerClaim, TCommandId owner, string? context, TCommandId command) =>
		$"The overrides bind {DescribeChord(chord)} to both {owner} in {DescribeContext(ownerClaim.Context)} and {command} in {DescribeContext(context)}.";

	/// <summary>Describes an override claim the binding rules do not let shadow the catalog default it collides with.</summary>
	/// <param name="claim">The override claim the state would introduce.</param>
	/// <param name="shadowedClaim">The catalog default the claim collides with.</param>
	/// <param name="shadowedOwner">The command that owns the shadowed default.</param>
	/// <param name="owner">The command the claim belongs to.</param>
	/// <param name="shadowsScopedDefault"><see langword="true"/> to describe a context-scoped default an always-active claim cannot shadow.</param>
	/// <returns>The description of the rejected claim.</returns>
	private string DescribeShadowedDefault(
		ContextChord claim,
		ContextChord shadowedClaim,
		TCommandId shadowedOwner,
		TCommandId owner,
		bool shadowsScopedDefault
	)
	{
		string chord = DescribeChord(claim.Chord);

		if (shadowsScopedDefault)
			return $"The overrides bind {chord} to {owner} in {DescribeContext(claim.Context)}, which would leave {shadowedOwner} unreachable in {DescribeContext(shadowedClaim.Context)}.";

		return $"The overrides bind {chord} to {owner} in {DescribeContext(claim.Context)}, shadowing {shadowedOwner} in {DescribeContext(shadowedClaim.Context)}, which that command's remapping policy does not allow.";
	}

	private string DescribePrefixConflict(string? context, KeyChord prefix, KeyChord chord)
	{
		if (context is null)
			return $"The bindings contain a chord that is a prefix of another: {DescribeChord(prefix)} and {DescribeChord(chord)}.";

		return $"The bindings contain a chord that is a prefix of another in context '{context}': {DescribeChord(prefix)} and {DescribeChord(chord)}.";
	}

	/// <summary>How a map build ended, which decides whether a mutation reports an outcome or an invariant violation.</summary>
	internal enum MapBuildOutcome
	{
		/// <summary>The state is consistent.</summary>
		Succeeded,

		/// <summary>More than one entry exists for one serialized identifier.</summary>
		DuplicateEntry,

		/// <summary>Two commands claim one chord in a way the binding rules reject.</summary>
		ClaimConflict,

		/// <summary>A bound chord is a strict prefix of another co-active bound chord.</summary>
		PrefixConflict,
	}

	/// <summary>The result of building the runtime maps for an override state: the maps, or the reason the state cannot be built.</summary>
	/// <param name="Outcome">How the build ended.</param>
	/// <param name="Reason">The description of the inconsistency; empty when the state is consistent.</param>
	/// <param name="Maps">The runtime maps, available exactly when the state is consistent.</param>
	internal readonly record struct MapBuildResult(MapBuildOutcome Outcome, string Reason, PublishedSnapshot<TCommandId>? Maps)
	{
		/// <summary>Indicates whether the state is consistent and its maps are available.</summary>
		[MemberNotNullWhen(true, nameof(Maps))]
		internal bool Succeeded => Outcome == MapBuildOutcome.Succeeded;

		/// <summary>
		/// Indicates whether the failure is a duplicate entry, the one failure no operation can produce and therefore an invariant violation.
		/// </summary>
		internal bool IsDuplicateEntryFailure => Outcome == MapBuildOutcome.DuplicateEntry;

		/// <summary>Indicates whether the failure is the strict-prefix conflict a mutation can reach.</summary>
		internal bool IsPrefixConflictFailure => Outcome == MapBuildOutcome.PrefixConflict;

		/// <summary>Creates the result for a consistent state.</summary>
		/// <param name="maps">The runtime maps of the state.</param>
		/// <returns>The result carrying the maps.</returns>
		internal static MapBuildResult Ok(PublishedSnapshot<TCommandId> maps) => new(MapBuildOutcome.Succeeded, string.Empty, maps);

		/// <summary>Creates the result for a state that cannot be built.</summary>
		/// <param name="outcome">The kind of inconsistency.</param>
		/// <param name="reason">The description of the inconsistency.</param>
		/// <returns>The result carrying the failure.</returns>
		internal static MapBuildResult Fail(MapBuildOutcome outcome, string reason) => new(outcome, reason, null);
	}

	/// <summary>One command's stored bindings for the candidate state, before shadowing is applied.</summary>
	private readonly record struct ResolvedBindings(TCommandId Command, string? Context, IReadOnlyList<KeyChord> Bindings);
}

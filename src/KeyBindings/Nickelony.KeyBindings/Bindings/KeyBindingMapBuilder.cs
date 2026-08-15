using Microsoft.Extensions.Logging;
using System.Collections.Frozen;
using System.Collections.Immutable;

namespace Nickelony.KeyBindings;

/// <summary>
/// Builds the runtime lookup maps from an override state, and analyzes proposed bindings against a
/// published snapshot under a conflict policy.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="KeyBindingService{TCommandId}"/> keeps the store and the public operations and delegates
/// every map build and conflict analysis here. Override entries are read and written by
/// <see cref="KeyBindingOverrideCodec"/>, and the co-active claim, shadowed-default, and prefix
/// conflict invariants are owned by <see cref="BindingClaimRules"/>, so this type keeps the map
/// construction and the map-specific diagnostics.
/// </para>
/// </remarks>
/// <typeparam name="TCommandId">The command identity type.</typeparam>
internal sealed partial class KeyBindingMapBuilder<TCommandId>
	where TCommandId : notnull
{
	// Key-binding map-build diagnostics occupy the package log event id block 4000-4001.
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
	private readonly KeyBindingOverrideCodec _codec;
	private readonly IKeyDisplayTextFormatter _displayTextFormatter;
	private readonly CommandCatalog<TCommandId> _catalog;
	private readonly HashSet<string> _catalogSerializedIds;

	/// <summary>
	/// Initializes a new instance of the <see cref="KeyBindingMapBuilder{TCommandId}"/> class.
	/// </summary>
	/// <param name="catalog">The command catalog that supplies descriptors and default bindings.</param>
	/// <param name="logger">The logger for override diagnostics.</param>
	/// <param name="displayTextFormatter">The formatter used to describe chords in a build failure.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="catalog"/>, <paramref name="logger"/>, or <paramref name="displayTextFormatter"/> is
	/// <see langword="null"/>.
	/// </exception>
	internal KeyBindingMapBuilder(
		CommandCatalog<TCommandId> catalog,
		ILogger logger,
		IKeyDisplayTextFormatter displayTextFormatter)
	{
		ArgumentNullException.ThrowIfNull(catalog);
		ArgumentNullException.ThrowIfNull(logger);
		ArgumentNullException.ThrowIfNull(displayTextFormatter);

		_catalog = catalog;
		_logger = logger;
		_displayTextFormatter = displayTextFormatter;
		// The codec is stateless apart from its logger, so each owner constructs its own instance.
		_codec = new KeyBindingOverrideCodec(logger);
		_catalogSerializedIds = [.. catalog.Descriptors.Select(descriptor => descriptor.SerializedId)];
	}

	/// <summary>
	/// Gets the serialized identifiers the catalog declares.
	/// </summary>
	internal IReadOnlySet<string> CatalogSerializedIds => _catalogSerializedIds;

	/// <summary>
	/// Builds the runtime maps for an override state, or reports why the state cannot be built.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Catalog defaults are claimed by their commands unless an override entry shadows them, and an
	/// override entry replaces its command's defaults. The state is rejected when two commands claim one
	/// chord in a way the binding rules do not allow, when one bound chord is a strict prefix of another
	/// co-active bound chord, or when one serialized identifier has two entries.
	/// </para>
	/// <para>
	/// An override may shadow a catalog default, since that is how
	/// <see cref="KeyBindingConflictPolicy.Replace"/> takes a chord over, but not a default whose command is
	/// not remappable; an always-active override may never displace a context-scoped default, because that
	/// default's command would become unreachable in its own context. The same rules apply when a preserved
	/// entry's command becomes cataloged, so a document can never win a chord the catalog refuses to give
	/// it. <paramref name="logDiagnostics"/> is <see langword="true"/> only for the initial load.
	/// </para>
	/// </remarks>
	/// <param name="overrides">The override state to build.</param>
	/// <param name="logDiagnostics"><see langword="true"/> to log override diagnostics; otherwise, <see langword="false"/>.</param>
	/// <returns>The maps, or the reason the state cannot be built.</returns>
	internal MapBuildResult Build(KeyBindingOverrides overrides, bool logDiagnostics)
	{
		var entriesById = new Dictionary<string, KeyBindingOverrideEntry>(StringComparer.Ordinal);

		foreach (KeyBindingOverrideEntry entry in overrides.Entries)
		{
			if (!entriesById.TryAdd(entry.SerializedId, entry))
			{
				return MapBuildResult.Fail(MapBuildFailure.DuplicateEntry, $"The overrides contain more than one entry for command '{entry.SerializedId}'.");
			}
		}

		var defaultClaims = new Dictionary<ContextChord, TCommandId>();
		var overrideClaims = new Dictionary<ContextChord, TCommandId>();
		var resolved = new List<ResolvedBindings>(_catalog.Descriptors.Count);

		foreach (CommandDescriptor<TCommandId> descriptor in _catalog.Descriptors)
		{
			entriesById.TryGetValue(descriptor.SerializedId, out KeyBindingOverrideEntry? entry);

			string? context = descriptor.Context ?? ContextChord.Always;

			bool usesOverride = entry is not null &&
				entry.Bindings.Count > 0 &&
				descriptor.RemappingPolicy != CommandRemappingPolicy.HostReserved;

			IReadOnlyList<KeyChord> bindings;

			if (usesOverride)
			{
				List<KeyChord> parsed = _codec.ParseEntryBindings(entry!, descriptor.SerializedId, logDiagnostics);

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
					if (BindingClaimRules.TryGetCoActiveClaim(overrideClaims, new ContextChord(context, chord), out ContextChord conflictingClaim, out TCommandId? conflictingOwner))
					{
						return MapBuildResult.Fail(
							MapBuildFailure.ClaimConflict,
							DescribeOverrideClaimConflict(chord, conflictingClaim, conflictingOwner, context, descriptor.Command));
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

		// An override may take a chord over only where the runtime rules allow it: the shadowed default's
		// command must be remappable, and an always-active override may never displace a context-scoped
		// default, because that default's command would become unreachable in its own context.
		foreach (KeyValuePair<ContextChord, TCommandId> claim in overrideClaims)
		{
			if (!BindingClaimRules.TryGetCoActiveClaim(defaultClaims, claim.Key, out ContextChord shadowedClaim, out TCommandId? shadowedOwner))
				continue;

			bool displacesScopedDefault = claim.Key.Context is null && shadowedClaim.Context is not null;

			if (displacesScopedDefault ||
				!_catalog.TryGetDescriptor(shadowedOwner, out CommandDescriptor<TCommandId>? shadowedDescriptor) ||
				shadowedDescriptor.RemappingPolicy != CommandRemappingPolicy.Remappable)
			{
				return MapBuildResult.Fail(
					MapBuildFailure.ClaimConflict,
					DescribeShadowedDefault(claim.Key, shadowedClaim, shadowedOwner, claim.Value, displacesScopedDefault));
			}
		}

		var commandsByChord = new Dictionary<ContextChord, TCommandId>(defaultClaims);

		foreach (KeyValuePair<ContextChord, TCommandId> claim in overrideClaims)
			commandsByChord[claim.Key] = claim.Value;

		var bindingsByCommand = new Dictionary<TCommandId, ImmutableArray<KeyChord>>();

		foreach (ResolvedBindings resolvedEntry in resolved)
		{
			bindingsByCommand[resolvedEntry.Command] =
				[.. resolvedEntry.Bindings.Where(binding =>
					commandsByChord.TryGetValue(new ContextChord(resolvedEntry.Context, binding), out TCommandId? owner) &&
					EqualityComparer<TCommandId>.Default.Equals(owner, resolvedEntry.Command))];
		}

		// Every strict prefix of a dispatching chord is collected so the dispatcher can tell a chord in
		// progress from an unbound stroke. A prefix belongs to the context of its chord, so a probe in
		// that context - or in any context, for an always-active chord - reports the chord in progress.
		// A dispatching chord that is itself a prefix of a co-active chord makes the longer chord
		// unreachable, so that state is rejected instead of being built.
		var chordPrefixes = new HashSet<ContextChord>();
		var contextScopedChords = new HashSet<KeyChord>();

		foreach (ContextChord claim in commandsByChord.Keys)
		{
			if (claim.Context is not null)
				contextScopedChords.Add(claim.Chord);
		}

		foreach (ContextChord claim in commandsByChord.Keys)
		{
			ReadOnlySpan<KeyCombo> strokes = claim.Chord.Strokes;

			for (int length = 1; length < strokes.Length; length++)
			{
				var prefix = new KeyChord(strokes[..length].ToArray());

				if (BindingClaimRules.IsPrefixConflict(commandsByChord, contextScopedChords, claim, prefix))
					return MapBuildResult.Fail(MapBuildFailure.PrefixConflict, DescribePrefixConflict(claim.Context, prefix, claim.Chord));

				chordPrefixes.Add(new ContextChord(claim.Context, prefix));
			}
		}

		return MapBuildResult.Ok(
			new PublishedMaps<TCommandId>(
				bindingsByCommand.ToFrozenDictionary(),
				commandsByChord.ToFrozenDictionary(),
				chordPrefixes.ToFrozenSet(),
				contextScopedChords.ToFrozenSet()));
	}

	/// <summary>
	/// Determines whether a candidate introduces a claim that collides with a preserved override entry:
	/// one whose serialized identifier the catalog does not contain.
	/// </summary>
	/// <remarks>
	/// A preserved entry is not part of the runtime maps, but a later run may catalog its command, so a
	/// claim that collides with the entry (the same chord, or a strict prefix relationship in either
	/// direction) is refused: the writer will not produce a shape the loader rejects. Only claims the
	/// current state does not already hold are compared, so an existing collision does not block an
	/// unrelated operation. Preserved strokes are parsed without logging.
	/// </remarks>
	/// <param name="candidate">The runtime maps of the candidate state.</param>
	/// <param name="appliedOverrides">The override state currently applied.</param>
	/// <param name="currentPublished">The runtime maps currently published.</param>
	/// <returns><see langword="true"/> when a newly introduced claim collides with a preserved entry; otherwise, <see langword="false"/>.</returns>
	internal bool HasNewPreservedEntryConflict(
		PublishedMaps<TCommandId> candidate,
		KeyBindingOverrides appliedOverrides,
		PublishedMaps<TCommandId> currentPublished)
	{
		foreach (KeyBindingOverrideEntry entry in appliedOverrides.Entries)
		{
			if (_catalogSerializedIds.Contains(entry.SerializedId))
				continue;

			foreach (KeyBindingOverrideBinding binding in entry.Bindings)
			{
				if (_codec.ParseBinding(binding, entry.SerializedId, logDiagnostics: false) is not KeyChord preserved)
					continue;

				foreach (ContextChord claim in candidate.CommandsByChord.Keys)
				{
					if (currentPublished.CommandsByChord.ContainsKey(claim))
						continue;

					if (preserved == claim.Chord || BindingClaimRules.IsStrictPrefix(preserved, claim.Chord) || BindingClaimRules.IsStrictPrefix(claim.Chord, preserved))
						return true;
				}
			}
		}

		return false;
	}

	/// <summary>
	/// Analyzes the proposed bindings against the published maps under a conflict policy.
	/// </summary>
	/// <remarks>
	/// The conflict policy is applied exactly as the service's apply path applies it, and a prefix
	/// relationship is reported before the exact conflicts and is never resolved by either policy. Only
	/// bindings that can be active at the same time as the proposal compete, so a chord a command in
	/// another context owns is not a conflict; an always-active proposal competes with every context,
	/// including the context-scoped bindings a lookup of the always-active context alone never reaches. One
	/// published snapshot is used for the whole analysis, so a concurrent publication cannot make the result
	/// come from two different rebuilds.
	/// </remarks>
	/// <param name="descriptor">The descriptor of the command the proposal applies to.</param>
	/// <param name="command">The command the proposed binding set applies to.</param>
	/// <param name="bindings">The proposed binding set.</param>
	/// <param name="conflictPolicy">The policy that controls how chords owned by other commands are treated.</param>
	/// <param name="published">The published maps to analyze against.</param>
	/// <param name="conflictsByOwner">Receives the conflicting chords per command when the analysis succeeds.</param>
	/// <returns>
	/// <see cref="KeyBindingOutcome.Succeeded"/> when the proposal passes the policy,
	/// <see cref="KeyBindingOutcome.PrefixConflict"/> when one proposed chord is a strict prefix of another
	/// bound chord (or the other way around), or <see cref="KeyBindingOutcome.Conflict"/> when the policy
	/// rejects an exact collision.
	/// </returns>
	internal KeyBindingOutcome AnalyzeConflicts(
		CommandDescriptor<TCommandId> descriptor,
		TCommandId command,
		IReadOnlyList<KeyChord> bindings,
		KeyBindingConflictPolicy conflictPolicy,
		PublishedMaps<TCommandId> published,
		out Dictionary<TCommandId, List<KeyChord>> conflictsByOwner)
	{
		conflictsByOwner = [];

		string? context = descriptor.Context ?? ContextChord.Always;

		// A prefix relationship is a declaration error rather than a competing claim: the shorter chord
		// always resolves first, so the longer one could never dispatch. It is reported before the exact
		// conflicts so that Replace cannot silently accept a set that leaves one of its chords unreachable.
		foreach (KeyChord binding in bindings)
		{
			if (BindingClaimRules.HasPrefixRelationship(published.CommandsByChord, published.ContextScopedChords, binding, command, context) || IsPrefixOfAnotherInSet(bindings, binding))
				return KeyBindingOutcome.PrefixConflict;
		}

		foreach (KeyChord binding in bindings)
		{
			if (!BindingClaimRules.TryGetOwner(published.CommandsByChord, binding, context, out TCommandId? owner) ||
				EqualityComparer<TCommandId>.Default.Equals(owner, command))
			{
				// A lookup of the always-active context alone never reaches a context-scoped binding, but an
				// always-active proposal is active in every context, so a context-scoped claim still
				// competes with it.
				if (context is not null || !published.ContextScopedChords.Contains(binding))
					continue;

				return KeyBindingOutcome.Conflict;
			}

			if (conflictPolicy == KeyBindingConflictPolicy.Reject ||
				!_catalog.TryGetDescriptor(owner, out CommandDescriptor<TCommandId>? ownerDescriptor) ||
				ownerDescriptor.RemappingPolicy != CommandRemappingPolicy.Remappable)
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
	/// Determines whether a chord in a proposed set is a strict prefix of another chord in the same set.
	/// </summary>
	private static bool IsPrefixOfAnotherInSet(IReadOnlyList<KeyChord> bindings, KeyChord chord)
	{
		foreach (KeyChord candidate in bindings)
		{
			if (BindingClaimRules.IsStrictPrefix(chord, candidate))
				return true;
		}

		return false;
	}

	private string DescribeChord(KeyChord chord)
		=> _displayTextFormatter.GetDisplayText(chord);

	/// <summary>
	/// Describes the context a claim or a binding belongs to.
	/// </summary>
	/// <param name="context">The context token, or <see cref="ContextChord.Always"/> for the always-active context.</param>
	private static string DescribeContext(string? context)
		=> context is null ? "the always-active context" : $"context '{context}'";

	/// <summary>
	/// Describes two same-chord override claims that cannot both hold because their contexts can be
	/// active at the same time.
	/// </summary>
	private string DescribeOverrideClaimConflict(
		KeyChord chord,
		ContextChord ownerClaim,
		TCommandId owner,
		string? context,
		TCommandId command)
		=> $"The overrides bind {DescribeChord(chord)} to both {owner} in {DescribeContext(ownerClaim.Context)} and {command} in {DescribeContext(context)}.";

	/// <summary>
	/// Describes an override claim the binding rules do not let shadow the catalog default it collides
	/// with.
	/// </summary>
	/// <param name="claim">The override claim the state would introduce.</param>
	/// <param name="shadowedClaim">The catalog default the claim collides with.</param>
	/// <param name="shadowedOwner">The command that owns the shadowed default.</param>
	/// <param name="owner">The command the claim belongs to.</param>
	/// <param name="displacesScopedDefault">
	/// <see langword="true"/> to describe a context-scoped default an always-active claim cannot displace.
	/// </param>
	/// <returns>The description of the rejected claim.</returns>
	private string DescribeShadowedDefault(
		ContextChord claim,
		ContextChord shadowedClaim,
		TCommandId shadowedOwner,
		TCommandId owner,
		bool displacesScopedDefault)
	{
		string chord = DescribeChord(claim.Chord);

		if (displacesScopedDefault)
		{
			return $"The overrides bind {chord} to {owner} in {DescribeContext(claim.Context)}, which would leave {shadowedOwner} unreachable in {DescribeContext(shadowedClaim.Context)}.";
		}

		return $"The overrides bind {chord} to {owner} in {DescribeContext(claim.Context)}, shadowing {shadowedOwner} in {DescribeContext(shadowedClaim.Context)}, which that command's remapping policy does not allow.";
	}

	private string DescribePrefixConflict(string? context, KeyChord prefix, KeyChord chord)
	{
		if (context is null)
			return $"The bindings contain a chord that is a prefix of another: {DescribeChord(prefix)} and {DescribeChord(chord)}.";

		return $"The bindings contain a chord that is a prefix of another in context '{context}': {DescribeChord(prefix)} and {DescribeChord(chord)}.";
	}

	/// <summary>
	/// The kind of inconsistency an override state can have, which decides whether a mutation reports an
	/// outcome or an invariant violation.
	/// </summary>
	internal enum MapBuildFailure
	{
		/// <summary>The state is consistent.</summary>
		None,

		/// <summary>More than one entry exists for one serialized identifier.</summary>
		DuplicateEntry,

		/// <summary>Two commands claim one chord in a way the binding rules reject.</summary>
		ClaimConflict,

		/// <summary>A bound chord is a strict prefix of another co-active bound chord.</summary>
		PrefixConflict
	}

	/// <summary>
	/// The result of building the runtime maps for an override state: the maps, or the reason the state
	/// cannot be built.
	/// </summary>
	/// <param name="Failure">The kind of inconsistency, or <see cref="MapBuildFailure.None"/>.</param>
	/// <param name="Reason">The description of the inconsistency; empty when the state is consistent.</param>
	/// <param name="Maps">The runtime maps, available exactly when the state is consistent.</param>
	internal readonly record struct MapBuildResult(MapBuildFailure Failure, string Reason, PublishedMaps<TCommandId>? Maps)
	{
		/// <summary>
		/// Gets a value indicating whether the state is consistent and its maps are available.
		/// </summary>
		internal bool Succeeded => Failure == MapBuildFailure.None;

		/// <summary>
		/// Gets a value indicating whether the failure is a duplicate entry, the one failure no operation
		/// can produce and therefore an invariant violation.
		/// </summary>
		internal bool IsDuplicateEntryFailure => Failure == MapBuildFailure.DuplicateEntry;

		/// <summary>
		/// Gets a value indicating whether the failure is a prefix conflict.
		/// </summary>
		internal bool IsPrefixConflictFailure => Failure == MapBuildFailure.PrefixConflict;

		/// <summary>
		/// Creates the result for a consistent state.
		/// </summary>
		/// <param name="maps">The runtime maps of the state.</param>
		/// <returns>The result carrying the maps.</returns>
		internal static MapBuildResult Ok(PublishedMaps<TCommandId> maps) => new(MapBuildFailure.None, string.Empty, maps);

		/// <summary>
		/// Creates the result for a state that cannot be built.
		/// </summary>
		/// <param name="failure">The kind of inconsistency.</param>
		/// <param name="reason">The description of the inconsistency.</param>
		/// <returns>The result carrying the failure.</returns>
		internal static MapBuildResult Fail(MapBuildFailure failure, string reason) => new(failure, reason, null);
	}

	/// <summary>
	/// One command's stored bindings for the candidate state, before shadowing is applied.
	/// </summary>
	private readonly record struct ResolvedBindings(TCommandId Command, string? Context, IReadOnlyList<KeyChord> Bindings);
}

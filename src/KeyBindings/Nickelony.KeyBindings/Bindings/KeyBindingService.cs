using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Nickelony.KeyBindings;

/// <summary>
/// Runtime key binding service: the default <see cref="IKeyBindingService{TCommandId}"/> implementation.
/// </summary>
/// <remarks>
/// <para>
/// The contract lives on <see cref="IKeyBindingService{TCommandId}"/>. This implementation owns the
/// runtime state; map construction and conflict analysis live in an internal builder. The overrides
/// store is read once, in the constructor, and each committed mutation persists a snapshot before it
/// publishes the rebuilt maps.
/// </para>
/// </remarks>
/// <typeparam name="TCommandId">The command identity type.</typeparam>
public sealed partial class KeyBindingService<TCommandId> : IKeyBindingService<TCommandId>
	where TCommandId : notnull
{
	// The unknown-override-command diagnostic uses the package log event id 4005.
	[LoggerMessage(
		EventId = 4005,
		EventName = "UnknownOverrideCommand",
		Level = LogLevel.Warning,
		Message = "Key binding override for command '{SerializedId}' ignored because the catalog does not contain it. The entry is preserved."
	)]
	private static partial void LogUnknownOverrideCommand(ILogger logger, string serializedId);

	private readonly ILogger _logger;
	private readonly IKeyDisplayTextFormatter _displayTextFormatter;
	private readonly CommandCatalog<TCommandId> _catalog;
	private readonly IKeyBindingOverridesStore _overridesStore;
	private readonly KeyBindingMapBuilder<TCommandId> _mapBuilder;
	private readonly KeyBindingOverrideCodec _overrideCodec;
	private readonly FrozenSet<string> _contexts;

	private KeyBindingOverrides _appliedOverrides;

	private volatile PublishedMaps<TCommandId> _published;

	/// <summary>
	/// Initializes a new instance of the <see cref="KeyBindingService{TCommandId}"/> class.
	/// </summary>
	/// <param name="catalog">The command catalog that supplies descriptors and default bindings.</param>
	/// <param name="overridesStore">
	/// The store that supplies the initial overrides and persists every committed mutation. Its
	/// <see cref="IKeyBindingOverridesStore.Load"/> is called once, in the constructor.
	/// </param>
	/// <param name="options">The optional display-text formatter and logger for the service.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="catalog"/> or <paramref name="overridesStore"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// The store returned <see langword="null"/>, or its overrides contain a <see langword="null"/>
	/// override list, a <see langword="null"/> entry, a <see langword="null"/> serialized identifier,
	/// an entry with a <see langword="null"/> binding list, or a <see langword="null"/> binding entry.
	/// </exception>
	/// <exception cref="NotSupportedException">
	/// The overrides use a schema version other than <see cref="KeyBindingOverrides.CurrentVersion"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// The overrides contain more than one entry for the same serialized identifier, bind the same
	/// chord to two commands in co-active contexts, bind a chord an unremappable command owns or that
	/// would displace a context-scoped default, or bind two chords where one is a strict prefix of the
	/// other in a co-active context.
	/// </exception>
	/// <remarks>
	/// Malformed overrides are reported before an unsupported schema version.
	/// </remarks>
	public KeyBindingService(
		CommandCatalog<TCommandId> catalog,
		IKeyBindingOverridesStore overridesStore,
		KeyBindingServiceOptions? options = null)
	{
		ArgumentNullException.ThrowIfNull(catalog);
		ArgumentNullException.ThrowIfNull(overridesStore);

		KeyBindingOverrides loadedOverrides = overridesStore.Load()
			?? throw new ArgumentException("The overrides store must return a non-null instance.", nameof(overridesStore));

		options ??= KeyBindingServiceOptions.Default;

		ValidateLoadedOverrides(loadedOverrides, nameof(overridesStore));

		if (loadedOverrides.Version != KeyBindingOverrides.CurrentVersion)
		{
			throw new NotSupportedException(
				$"The overrides use schema version {loadedOverrides.Version}, but this package supports only version {KeyBindingOverrides.CurrentVersion}.");
		}

		_logger = options.Logger ?? NullLogger.Instance;
		_displayTextFormatter = options.DisplayTextFormatter;
		_catalog = catalog;
		_overridesStore = overridesStore;
		_mapBuilder = new KeyBindingMapBuilder<TCommandId>(catalog, _logger, _displayTextFormatter);
		// The codec is stateless apart from its logger, so each owner constructs its own instance.
		_overrideCodec = new KeyBindingOverrideCodec(_logger);
		_contexts = catalog.Descriptors
			.Select(descriptor => descriptor.Context)
			.Where(context => context is not null)
			.Select(context => context!)
			.ToFrozenSet(StringComparer.Ordinal);

		var build = _mapBuilder.Build(loadedOverrides, logDiagnostics: true);

		if (!build.Succeeded)
			throw new InvalidOperationException(build.Reason);

		// The maps are available whenever the build succeeded, which the guard above established.
		PublishedMaps<TCommandId> maps = build.Maps!;

		LogUnknownOverrideEntries(loadedOverrides);

		_appliedOverrides = loadedOverrides.Clone();
		_published = WithDisplayText(maps);
	}

	/// <inheritdoc cref="IKeyBindingService{TCommandId}.BindingsChanged"/>
	public event EventHandler<KeyBindingsChangedEventArgs<TCommandId>>? BindingsChanged;

	/// <inheritdoc cref="IKeyBindingService{TCommandId}.TryGetCommand"/>
	public bool TryGetCommand(KeyChord keyChord, string? context, [NotNullWhen(true)] out TCommandId? command)
	{
		PublishedMaps<TCommandId> published = _published;

		if (published.CommandsByChord.TryGetValue(ContextChord.Create(context, keyChord), out command))
			return true;

		// Only the requested context and the always-active context can be active at the same time, so
		// the always-active bindings are the only fallback a token-scoped lookup can consider.
		return context is not null &&
			published.CommandsByChord.TryGetValue(ContextChord.Create(null, keyChord), out command);
	}

	/// <inheritdoc cref="IKeyBindingService{TCommandId}.IsChordPrefix"/>
	public bool IsChordPrefix(KeyChord keyChord, string? context)
	{
		PublishedMaps<TCommandId> published = _published;

		return published.ChordPrefixes.Contains(ContextChord.Create(context, keyChord)) ||
			(context is not null && published.ChordPrefixes.Contains(ContextChord.Create(null, keyChord)));
	}

	/// <inheritdoc cref="IKeyBindingService{TCommandId}.Contexts"/>
	public IReadOnlySet<string> Contexts => _contexts;

	/// <inheritdoc cref="IKeyBindingService{TCommandId}.GetBindings"/>
	/// <exception cref="ArgumentNullException"><paramref name="command"/> is <see langword="null"/>.</exception>
	public IReadOnlyList<KeyChord> GetBindings(TCommandId command)
	{
		if (command is null)
			throw new ArgumentNullException(nameof(command));

		if (_published.BindingsByCommand.TryGetValue(command, out ImmutableArray<KeyChord> bindings))
			return bindings;

		return [];
	}

	/// <inheritdoc cref="IKeyBindingService{TCommandId}.GetDisplayText"/>
	/// <exception cref="ArgumentNullException"><paramref name="command"/> or <paramref name="fallbackDisplayText"/> is <see langword="null"/>.</exception>
	public string GetDisplayText(TCommandId command, string fallbackDisplayText = "")
	{
		if (command is null)
			throw new ArgumentNullException(nameof(command));

		ArgumentNullException.ThrowIfNull(fallbackDisplayText);

		// The text is precomputed into the published snapshot, so a read neither reruns the formatter nor
		// rebuilds the join; a command without a dispatching binding falls back.
		PublishedMaps<TCommandId> published = _published;

		return published.DisplayTextByCommand.TryGetValue(command, out string? displayText)
			? displayText
			: fallbackDisplayText;
	}

	/// <inheritdoc cref="IKeyBindingService{TCommandId}.Validate(TCommandId, System.Collections.Generic.IReadOnlyList{KeyChord}, KeyBindingConflictPolicy)"/>
	/// <exception cref="ArgumentNullException"><paramref name="command"/> or <paramref name="bindings"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="bindings"/> contains an uninitialized key chord, or
	/// <paramref name="conflictPolicy"/> is not a defined policy value.
	/// </exception>
	public KeyBindingOutcome Validate(
		TCommandId command,
		IReadOnlyList<KeyChord> bindings,
		KeyBindingConflictPolicy conflictPolicy = KeyBindingConflictPolicy.Reject)
	{
		if (command is null)
			throw new ArgumentNullException(nameof(command));

		ValidateProposedBindings(bindings);

		if (!Enum.IsDefined<KeyBindingConflictPolicy>(conflictPolicy))
			throw new ArgumentException("The conflict policy must be a defined value.", nameof(conflictPolicy));

		if (!TryValidateCommandState(command, bindings, out CommandDescriptor<TCommandId>? descriptor, out KeyBindingOutcome failure))
			return failure;

		KeyBindingOverrides? candidate = PlanApply(descriptor, command, bindings, conflictPolicy, out KeyBindingOutcome planOutcome);

		if (candidate is null)
			return planOutcome;

		return TryAnalyzeCandidate(candidate, out _, out KeyBindingOutcome candidateOutcome)
			? KeyBindingOutcome.Succeeded
			: candidateOutcome;
	}

	/// <inheritdoc cref="IKeyBindingService{TCommandId}.Apply"/>
	/// <exception cref="ArgumentNullException"><paramref name="command"/> or <paramref name="bindings"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="bindings"/> contains an uninitialized key chord, or
	/// <paramref name="conflictPolicy"/> is not a defined policy value.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// The accepted binding set cannot produce a consistent runtime state.
	/// </exception>
	public KeyBindingOutcome Apply(
		TCommandId command,
		IReadOnlyList<KeyChord> bindings,
		KeyBindingConflictPolicy conflictPolicy = KeyBindingConflictPolicy.Reject)
	{
		if (command is null)
			throw new ArgumentNullException(nameof(command));

		ValidateProposedBindings(bindings);

		if (!Enum.IsDefined<KeyBindingConflictPolicy>(conflictPolicy))
			throw new ArgumentException("The conflict policy must be a defined value.", nameof(conflictPolicy));

		if (!TryValidateCommandState(command, bindings, out CommandDescriptor<TCommandId>? descriptor, out KeyBindingOutcome failure))
			return failure;

		KeyBindingOverrides? candidate = PlanApply(descriptor, command, bindings, conflictPolicy, out KeyBindingOutcome planOutcome);

		return candidate is null ? planOutcome : Commit(candidate, command);
	}

	/// <summary>
	/// Plans the override state an <see cref="Apply"/> or
	/// <see cref="IKeyBindingService{TCommandId}.Validate(TCommandId, System.Collections.Generic.IReadOnlyList{KeyChord}, KeyBindingConflictPolicy)"/>
	/// operation would produce, or the outcome that refuses the proposal.
	/// </summary>
	/// <remarks>
	/// The conflict analysis and the candidate construction are shared by <see cref="Validate"/> and
	/// <see cref="Apply"/>, so a dry run cannot disagree with what <see cref="Apply"/> does. The candidate is
	/// built from clones, so the runtime state and the overrides store never share mutable entries.
	/// </remarks>
	/// <param name="descriptor">The descriptor of the command the proposal applies to.</param>
	/// <param name="command">The command the proposed binding set applies to.</param>
	/// <param name="bindings">The proposed binding set.</param>
	/// <param name="conflictPolicy">The policy that controls how chords owned by other commands are treated.</param>
	/// <param name="outcome">Receives the outcome that refuses the proposal.</param>
	/// <returns>
	/// The candidate state, or <see langword="null"/> when the proposal is refused and
	/// <paramref name="outcome"/> carries the reason. An empty binding set is stored as an explicit
	/// override that unbinds the command.
	/// </returns>
	private KeyBindingOverrides? PlanApply(
		CommandDescriptor<TCommandId> descriptor,
		TCommandId command,
		IReadOnlyList<KeyChord> bindings,
		KeyBindingConflictPolicy conflictPolicy,
		out KeyBindingOutcome outcome)
	{
		outcome = _mapBuilder.AnalyzeConflicts(descriptor, command, bindings, conflictPolicy, _published, out Dictionary<TCommandId, List<KeyChord>> conflictsByOwner);

		if (outcome != KeyBindingOutcome.Succeeded)
			return null;

		KeyBindingOverrides candidate = BuildSnapshotWithoutOverride(descriptor.SerializedId);
		candidate.Entries.Add(KeyBindingOverrideCodec.CreateEntry(descriptor.SerializedId, bindings));

		foreach ((TCommandId owner, List<KeyChord> ownerConflicts) in conflictsByOwner)
		{
			if (_catalog.TryGetDescriptor(owner, out CommandDescriptor<TCommandId>? ownerDescriptor))
				RemoveOverrideBindings(candidate, ownerDescriptor.SerializedId, ownerConflicts);
		}

		return candidate;
	}

	/// <inheritdoc cref="IKeyBindingService{TCommandId}.Reset"/>
	/// <exception cref="ArgumentNullException"><paramref name="command"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">
	/// The resulting binding set cannot produce a consistent runtime state.
	/// </exception>
	public KeyBindingOutcome Reset(TCommandId command)
	{
		if (command is null)
			throw new ArgumentNullException(nameof(command));

		if (!_catalog.TryGetDescriptor(command, out CommandDescriptor<TCommandId>? descriptor))
			return KeyBindingOutcome.UnknownCommand;

		if (!HasOverride(descriptor.SerializedId))
			return KeyBindingOutcome.Succeeded;

		return Commit(BuildSnapshotWithoutOverride(descriptor.SerializedId), command);
	}

	/// <inheritdoc cref="IKeyBindingService{TCommandId}.ResetAll"/>
	public KeyBindingOutcome ResetAll()
	{
		KeyBindingOverrides snapshot = BuildPreservedOnlySnapshot();

		// Every cataloged override is already cleared when only preserved entries remain, so nothing is
		// persisted and the notification is not raised.
		if (snapshot.Entries.Count == _appliedOverrides.Entries.Count)
			return KeyBindingOutcome.Succeeded;

		return Commit(snapshot, default, allCommands: true);
	}

	private static void ValidateLoadedOverrides(KeyBindingOverrides loadedOverrides, string parameterName)
	{
		if (loadedOverrides.Entries is null)
			throw new ArgumentException("The loaded overrides must have a non-null entry list.", parameterName);

		foreach (KeyBindingOverrideEntry entry in loadedOverrides.Entries)
		{
			if (entry is null)
				throw new ArgumentException("The loaded overrides must not contain null entries.", parameterName);

			if (entry.SerializedId is null)
				throw new ArgumentException("The loaded overrides must not contain an entry with a null serialized identifier.", parameterName);

			if (entry.Bindings is null)
				throw new ArgumentException($"Override entry '{entry.SerializedId}' must have a non-null binding list.", parameterName);

			foreach (KeyBindingOverrideBinding binding in entry.Bindings)
			{
				if (binding is null)
					throw new ArgumentException($"Override entry '{entry.SerializedId}' must not contain null binding entries.", parameterName);

				if (binding.Strokes is null)
					throw new ArgumentException($"A binding of override entry '{entry.SerializedId}' must have a non-null stroke list.", parameterName);

				foreach (KeyBindingOverrideStroke stroke in binding.Strokes)
				{
					if (stroke is null)
						throw new ArgumentException($"A binding of override entry '{entry.SerializedId}' must not contain null strokes.", parameterName);
				}
			}
		}
	}

	private static void ValidateProposedBindings(IReadOnlyList<KeyChord> bindings)
	{
		ArgumentNullException.ThrowIfNull(bindings);

		foreach (KeyChord binding in bindings)
		{
			if (!binding.IsInitialized)
				throw new ArgumentException("The proposed binding set contains an uninitialized key chord.", nameof(bindings));
		}
	}

	/// <summary>
	/// Validates the command's remapping policy and the proposed set itself, without inspecting the
	/// current bindings of other commands.
	/// </summary>
	private bool TryValidateCommandState(
		TCommandId command,
		IReadOnlyList<KeyChord> bindings,
		[NotNullWhen(true)] out CommandDescriptor<TCommandId>? descriptor,
		out KeyBindingOutcome failure)
	{
		failure = KeyBindingOutcome.Succeeded;

		if (!_catalog.TryGetDescriptor(command, out descriptor))
		{
			failure = KeyBindingOutcome.UnknownCommand;
			return false;
		}

		if (descriptor.RemappingPolicy == CommandRemappingPolicy.HostReserved)
		{
			failure = KeyBindingOutcome.Reserved;
			return false;
		}

		if (descriptor.RemappingPolicy != CommandRemappingPolicy.Remappable)
		{
			failure = KeyBindingOutcome.NotRemappable;
			return false;
		}

		var seen = new HashSet<KeyChord>();

		foreach (KeyChord binding in bindings)
		{
			if (!seen.Add(binding))
			{
				failure = KeyBindingOutcome.DuplicateInCommand;
				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// Logs one diagnostic per override entry whose serialized identifier is not cataloged.
	/// </summary>
	private void LogUnknownOverrideEntries(KeyBindingOverrides overrides)
	{
		foreach (KeyBindingOverrideEntry entry in overrides.Entries)
		{
			if (!_mapBuilder.CatalogSerializedIds.Contains(entry.SerializedId))
				LogUnknownOverrideCommand(_logger, entry.SerializedId);
		}
	}

	/// <summary>
	/// Removes the conflicting chords from another command's override entry in a candidate snapshot.
	/// A catalog default is never rewritten: the proposal shadows it instead.
	/// </summary>
	/// <remarks>
	/// An entry that loses its last binding is deleted, so the command falls back to its catalog
	/// defaults; the caller analyzes the candidate afterwards, because a restored default can collide
	/// with a binding another command holds.
	/// </remarks>
	/// <param name="candidate">The candidate state to rewrite.</param>
	/// <param name="serializedId">The serialized identifier of the entry to rewrite.</param>
	/// <param name="conflictingChords">The chords to remove from the entry.</param>
	private void RemoveOverrideBindings(KeyBindingOverrides candidate, string serializedId, IReadOnlyList<KeyChord> conflictingChords)
	{
		KeyBindingOverrideEntry? entry = candidate.Entries
			.FirstOrDefault(candidateEntry => string.Equals(candidateEntry.SerializedId, serializedId, StringComparison.Ordinal));

		if (entry is null || entry.Bindings.Count == 0)
			return;

		// The entry was parsed when it was persisted, so re-parsing it here reports no diagnostics.
		List<KeyChord> remaining = _overrideCodec.ParseEntryBindings(entry, serializedId, logDiagnostics: false);

		if (remaining.RemoveAll(chord => conflictingChords.Contains(chord)) == 0)
			return;

		if (remaining.Count == 0)
		{
			candidate.Entries.Remove(entry);
			return;
		}

		entry.Bindings.Clear();

		foreach (KeyChord chord in remaining)
			entry.Bindings.Add(KeyBindingOverrideCodec.CreateBinding(chord));
	}

	/// <summary>
	/// Persists a candidate override snapshot and publishes its runtime maps.
	/// </summary>
	/// <remarks>
	/// Everything that can fail - the display text formatter, which a host supplies, and the store -
	/// runs before any state is committed, so a failure leaves the persisted, applied, and published
	/// states agreeing. The store receives its own copy of the candidate and the applied state is cloned
	/// from the candidate, so a store that mutates the argument it is handed cannot reach either.
	/// </remarks>
	/// <param name="snapshot">The candidate override state to persist and publish.</param>
	/// <param name="command">
	/// The command whose bindings changed, or the identity's <see langword="default"/> value for a reset
	/// that could change every command; carried in the <see cref="BindingsChanged"/> notification.
	/// </param>
	/// <param name="allCommands">
	/// <see langword="true"/> for a reset that could change every command; carried in the
	/// <see cref="BindingsChanged"/> notification.
	/// </param>
	private KeyBindingOutcome Commit(KeyBindingOverrides snapshot, TCommandId? command, bool allCommands = false)
	{
		// The candidate inherits the applied version, which is the only version the service accepts, so
		// every persisted snapshot is stamped with the current schema version.
		snapshot.Version = KeyBindingOverrides.CurrentVersion;

		if (!TryAnalyzeCandidate(snapshot, out PublishedMaps<TCommandId>? maps, out KeyBindingOutcome outcome))
			return outcome;

		KeyBindingOverrides persisted = snapshot.Clone();
		PublishedMaps<TCommandId> published = WithDisplayText(maps);

		if (!_overridesStore.Save(persisted))
			return KeyBindingOutcome.PersistenceFailed;

		_appliedOverrides = snapshot.Clone();
		_published = published;

		BindingsChanged?.Invoke(this, new KeyBindingsChangedEventArgs<TCommandId>(command, allCommands));

		return KeyBindingOutcome.Succeeded;
	}

	/// <summary>
	/// Determines whether a candidate override state can be published, and returns the maps to publish.
	/// </summary>
	/// <remarks>
	/// A failure a mutation can reach through its own effect on another command is returned as an outcome;
	/// a duplicate entry is the one failure no operation can produce, so it throws. A claim the candidate
	/// introduces must also not collide with a preserved override entry.
	/// </remarks>
	/// <param name="candidate">The candidate state a mutation would persist.</param>
	/// <param name="maps">Receives the runtime maps when the candidate is consistent.</param>
	/// <param name="outcome">Receives the failure outcome when the candidate is not consistent.</param>
	/// <returns><see langword="true"/> when the candidate is consistent; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="InvalidOperationException">The candidate has two entries for one serialized identifier.</exception>
	private bool TryAnalyzeCandidate(KeyBindingOverrides candidate, [NotNullWhen(true)] out PublishedMaps<TCommandId>? maps, out KeyBindingOutcome outcome)
	{
		maps = null;
		outcome = KeyBindingOutcome.Succeeded;

		var build = _mapBuilder.Build(candidate, logDiagnostics: false);

		if (!build.Succeeded)
		{
			// A duplicate entry is the only failure no operation can produce, so it is an invariant
			// violation; every other failure is a state a mutation can reach through its own effect on
			// another command.
			if (build.IsDuplicateEntryFailure)
				throw new InvalidOperationException(build.Reason);

			outcome = build.IsPrefixConflictFailure ? KeyBindingOutcome.PrefixConflict : KeyBindingOutcome.Conflict;
			return false;
		}

		// The maps are available whenever the build succeeded, which the guard above established.
		maps = build.Maps!;

		if (_mapBuilder.HasNewPreservedEntryConflict(maps, _appliedOverrides, _published))
		{
			outcome = KeyBindingOutcome.Conflict;
			return false;
		}

		return true;
	}

	/// <summary>
	/// Returns the maps with the display text of every command that has a dispatching binding built for
	/// them.
	/// </summary>
	/// <remarks>
	/// The display text of a command is fixed by its bindings, so it is computed once when the maps are
	/// published instead of on every read. The texts use the formatter the service was configured with and
	/// the same separator <see cref="GetDisplayText"/> documents.
	/// </remarks>
	/// <param name="maps">The maps to derive the display texts from.</param>
	/// <returns>The maps with their display texts.</returns>
	private PublishedMaps<TCommandId> WithDisplayText(PublishedMaps<TCommandId> maps)
	{
		var displayTextByCommand = new Dictionary<TCommandId, string>(maps.BindingsByCommand.Count);

		foreach (KeyValuePair<TCommandId, ImmutableArray<KeyChord>> entry in maps.BindingsByCommand)
		{
			if (entry.Value.Length > 0)
			{
				displayTextByCommand[entry.Key] = string.Join(
					" / ",
					entry.Value.Select(binding => _displayTextFormatter.GetDisplayText(binding)));
			}
		}

		return maps with { DisplayTextByCommand = displayTextByCommand.ToFrozenDictionary() };
	}

	private bool HasOverride(string serializedId)
		=> _appliedOverrides.Entries.Any(entry => string.Equals(entry.SerializedId, serializedId, StringComparison.Ordinal));

	private KeyBindingOverrides BuildSnapshotWithoutOverride(string serializedId)
	{
		var snapshot = new KeyBindingOverrides { Version = _appliedOverrides.Version };

		foreach (KeyBindingOverrideEntry existing in _appliedOverrides.Entries)
		{
			if (!string.Equals(existing.SerializedId, serializedId, StringComparison.Ordinal))
				snapshot.Entries.Add(existing.Clone());
		}

		return snapshot;
	}

	/// <summary>
	/// Builds the candidate state that <see cref="ResetAll"/> persists: every cataloged override is
	/// dropped while entries for commands the catalog does not contain are preserved, matching the
	/// preservation guarantee documented for written snapshots.
	/// </summary>
	private KeyBindingOverrides BuildPreservedOnlySnapshot()
	{
		var snapshot = new KeyBindingOverrides { Version = _appliedOverrides.Version };

		foreach (KeyBindingOverrideEntry existing in _appliedOverrides.Entries)
		{
			if (!_mapBuilder.CatalogSerializedIds.Contains(existing.SerializedId))
				snapshot.Entries.Add(existing.Clone());
		}

		return snapshot;
	}
}

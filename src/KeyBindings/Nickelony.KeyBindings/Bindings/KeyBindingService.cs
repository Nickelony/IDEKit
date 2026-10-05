using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nickelony.KeyBindings.OverrideEntries;

namespace Nickelony.KeyBindings;

/// <summary>Runtime key binding service: the default <see cref="IKeyBindingService{TCommandId}"/> implementation.</summary>
/// <typeparam name="TCommandId">The command identity type.</typeparam>
/// <remarks>
/// <para>
/// The contract lives on <see cref="IKeyBindingService{TCommandId}"/>. This implementation owns the runtime state; an internal builder constructs
/// the maps and an internal detector analyzes proposals. The overrides store is read once, in the constructor, and each committed mutation persists
/// a snapshot before it publishes the rebuilt maps.
/// </para>
/// </remarks>
public sealed partial class KeyBindingService<TCommandId> : IKeyBindingService<TCommandId>
	where TCommandId : notnull
{
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
	private readonly IKeyBindingOverrideStore _overridesStore;
	private readonly KeyBindingMapBuilder<TCommandId> _mapBuilder;
	private readonly KeyBindingConflictDetector<TCommandId> _conflictDetector;
	private readonly KeyBindingOverrideCodec _overrideCodec;

	private readonly FrozenSet<string> _catalogSerializedIds;
	private readonly FrozenSet<string> _contexts;

	// The applied overrides are swapped as one immutable snapshot, like _published, so a concurrent reader
	// such as Validate observes a consistent state instead of a torn one.
	private volatile KeyBindingOverrides _appliedOverrides;

	private volatile PublishedSnapshot<TCommandId> _published;

	/// <param name="catalog">The command catalog that supplies descriptors and default bindings.</param>
	/// <param name="overridesStore">
	/// The store that supplies the initial overrides and persists every committed mutation.
	/// Its <see cref="IKeyBindingOverrideStore.Load"/> is called once, in the constructor.
	/// </param>
	/// <param name="options">The optional display-text formatter for the service.</param>
	/// <param name="logger">
	/// The logger for override diagnostics: host-reserved, invalid, or unknown entries; <see langword="null"/> discards them.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="catalog"/> or <paramref name="overridesStore"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// The store returned <see langword="null"/>, or its overrides contain a <see langword="null"/> override list, a <see langword="null"/> entry,
	/// a <see langword="null"/> serialized identifier, an entry with a <see langword="null"/> binding list, a <see langword="null"/> binding entry,
	/// a binding with a <see langword="null"/> stroke list, or a <see langword="null"/> stroke element.
	/// </exception>
	/// <exception cref="NotSupportedException">
	/// The overrides use a schema version other than <see cref="KeyBindingOverrides.CurrentVersion"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// The overrides contain more than one entry for the same serialized identifier, bind the same chord to two commands in co-active contexts,
	/// bind a chord an unremappable command owns or that would displace a context-scoped default, or bind two chords where one is a strict prefix
	/// of the other in a co-active context.
	/// </exception>
	/// <remarks>
	/// Malformed overrides are reported before an unsupported schema version.
	/// </remarks>
	public KeyBindingService(
		CommandCatalog<TCommandId> catalog,
		IKeyBindingOverrideStore overridesStore,
		KeyBindingServiceOptions? options = null,
		ILogger? logger = null
	)
	{
		ArgumentNullException.ThrowIfNull(catalog);
		ArgumentNullException.ThrowIfNull(overridesStore);

		KeyBindingOverrides loadedOverrides =
			overridesStore.Load() ?? throw new ArgumentException("The overrides store must return a non-null instance.", nameof(overridesStore));

		options ??= KeyBindingServiceOptions.Default;

		KeyBindingOverrideValidation.ThrowIfMalformed(loadedOverrides, nameof(overridesStore));

		if (loadedOverrides.Version != KeyBindingOverrides.CurrentVersion)
		{
			throw new NotSupportedException(
				$"The overrides use schema version {loadedOverrides.Version}, but this package supports only version {KeyBindingOverrides.CurrentVersion}."
			);
		}

		_logger = logger ?? NullLogger.Instance;
		_displayTextFormatter = options.DisplayTextFormatter;
		_catalog = catalog;
		_overridesStore = overridesStore;
		_mapBuilder = new KeyBindingMapBuilder<TCommandId>(catalog, _logger, _displayTextFormatter);

		// The codec is stateless apart from its logger, so each owner constructs its own instance.
		_overrideCodec = new KeyBindingOverrideCodec(_logger);
		_catalogSerializedIds = catalog.Descriptors.Select(descriptor => descriptor.SerializedId).ToFrozenSet(StringComparer.Ordinal);
		_conflictDetector = new KeyBindingConflictDetector<TCommandId>(catalog, _catalogSerializedIds, _overrideCodec);

		_contexts = catalog.Descriptors.Select(descriptor => descriptor.Context).OfType<string>().ToFrozenSet(StringComparer.Ordinal);

		KeyBindingMapBuilder<TCommandId>.MapBuildResult buildResult = _mapBuilder.Build(loadedOverrides, logDiagnostics: true);

		if (!buildResult.Succeeded)
			throw new InvalidOperationException(buildResult.Reason);

		PublishedSnapshot<TCommandId> maps = buildResult.Maps;

		LogUnknownOverrideEntries(loadedOverrides);

		_appliedOverrides = loadedOverrides.Clone();
		_published = maps.WithDisplayText(_displayTextFormatter);
	}

	/// <inheritdoc cref="IKeyBindingService{TCommandId}.BindingsChanged"/>
	public event EventHandler<BindingsChangedEventArgs<TCommandId>>? BindingsChanged;

	/// <inheritdoc cref="IKeyBindingResolver{TCommandId}.TryGetCommand"/>
	public bool TryGetCommand(KeyChord keyChord, string? context, [NotNullWhen(true)] out TCommandId? command)
	{
		PublishedSnapshot<TCommandId> published = _published;

		if (published.CommandsByChord.TryGetValue(new ContextChord(context, keyChord), out command))
			return true;

		// Only the requested context and the always-active context can be active at the same time, so
		// the always-active bindings are the only fallback a context-scoped lookup can consider.
		return context is not null && published.CommandsByChord.TryGetValue(new ContextChord(ContextChord.Always, keyChord), out command);
	}

	/// <inheritdoc cref="IKeyBindingResolver{TCommandId}.IsChordPrefix"/>
	/// <exception cref="ArgumentNullException"><paramref name="commandPredicate"/> is <see langword="null"/>.</exception>
	public bool IsChordPrefix(KeyChord keyChord, string? context, Func<TCommandId, bool> commandPredicate)
	{
		ArgumentNullException.ThrowIfNull(commandPredicate);

		PublishedSnapshot<TCommandId> published = _published;

		return published.AcceptsCommandUnderPrefix(new ContextChord(context, keyChord), commandPredicate)
			|| (context is not null && published.AcceptsCommandUnderPrefix(new ContextChord(ContextChord.Always, keyChord), commandPredicate));
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
	/// <exception cref="ArgumentNullException">
	/// <paramref name="command"/> or <paramref name="fallbackDisplayText"/> is <see langword="null"/>.
	/// </exception>
	public string GetDisplayText(TCommandId command, string fallbackDisplayText = "")
	{
		if (command is null)
			throw new ArgumentNullException(nameof(command));

		ArgumentNullException.ThrowIfNull(fallbackDisplayText);

		// The text is precomputed into the published snapshot, so a read neither reruns the formatter nor
		// rebuilds the join; a command without a dispatching binding falls back.
		PublishedSnapshot<TCommandId> published = _published;

		return published.DisplayTextByCommand.TryGetValue(command, out string? displayText) ? displayText : fallbackDisplayText;
	}

	/// <inheritdoc cref="IKeyBindingService{TCommandId}.Validate(TCommandId, System.Collections.Generic.IReadOnlyList{KeyChord}, KeyBindingConflictPolicy)"/>
	/// <exception cref="ArgumentNullException"><paramref name="command"/> or <paramref name="bindings"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="bindings"/> contains an uninitialized key chord, or <paramref name="conflictPolicy"/> is not a defined policy value.
	/// </exception>
	public KeyBindingOutcome Validate(
		TCommandId command,
		IReadOnlyList<KeyChord> bindings,
		KeyBindingConflictPolicy conflictPolicy = KeyBindingConflictPolicy.Reject
	)
	{
		if (command is null)
			throw new ArgumentNullException(nameof(command));

		ArgumentNullException.ThrowIfNull(bindings);
		ValidateProposal(bindings, conflictPolicy);

		KeyBindingOverrides? candidate = PlanApply(command, bindings, conflictPolicy, outcome: out KeyBindingOutcome planOutcome);

		if (candidate is null)
			return planOutcome;

		return TryAnalyzeCandidate(candidate, out _, out KeyBindingOutcome candidateOutcome) ? KeyBindingOutcome.Succeeded : candidateOutcome;
	}

	/// <inheritdoc cref="IKeyBindingService{TCommandId}.Apply"/>
	/// <exception cref="ArgumentNullException"><paramref name="command"/> or <paramref name="bindings"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="bindings"/> contains an uninitialized key chord, or <paramref name="conflictPolicy"/> is not a defined policy value.
	/// </exception>
	/// <exception cref="InvalidOperationException">The accepted binding set cannot produce a consistent runtime state.</exception>
	public KeyBindingOutcome Apply(
		TCommandId command,
		IReadOnlyList<KeyChord> bindings,
		KeyBindingConflictPolicy conflictPolicy = KeyBindingConflictPolicy.Reject
	)
	{
		if (command is null)
			throw new ArgumentNullException(nameof(command));

		ArgumentNullException.ThrowIfNull(bindings);
		ValidateProposal(bindings, conflictPolicy);

		KeyBindingOverrides? candidate = PlanApply(command, bindings, conflictPolicy, outcome: out KeyBindingOutcome planOutcome);

		if (candidate is null)
			return planOutcome;

		return Commit(candidate, command);
	}

	/// <summary>
	/// Plans the override state an <see cref="Apply"/>
	/// or <see cref="IKeyBindingService{TCommandId}.Validate(TCommandId, System.Collections.Generic.IReadOnlyList{KeyChord}, KeyBindingConflictPolicy)"/>
	/// operation would produce, or the outcome that refuses the proposal.
	/// </summary>
	/// <param name="command">The command the proposal applies to.</param>
	/// <param name="bindings">The proposed binding set.</param>
	/// <param name="conflictPolicy">The policy that controls how chords owned by other commands are treated.</param>
	/// <param name="outcome">Receives the outcome that refuses the proposal.</param>
	/// <returns>
	/// The candidate state, or <see langword="null"/> when the proposal is refused and <paramref name="outcome"/> carries the reason.
	/// </returns>
	/// <remarks>
	/// An empty binding set is stored as an explicit override that unbinds the command. The conflict analysis
	/// and the candidate construction are shared by <see cref="Validate"/> and <see cref="Apply"/>, so a dry run cannot disagree
	/// with what <see cref="Apply"/> does; the candidate is built from clones, so the runtime state and the overrides store never share
	/// mutable entries.
	/// </remarks>
	private KeyBindingOverrides? PlanApply(
		TCommandId command,
		IReadOnlyList<KeyChord> bindings,
		KeyBindingConflictPolicy conflictPolicy,
		out KeyBindingOutcome outcome
	)
	{
		if (!TryValidateCommandState(command, bindings, out CommandDescriptor<TCommandId>? descriptor, failure: out outcome))
			return null;

		outcome = _conflictDetector.Analyze(
			descriptor,
			bindings,
			conflictPolicy,
			published: _published,
			out Dictionary<TCommandId, List<KeyChord>> conflictsByOwner
		);

		if (outcome != KeyBindingOutcome.Succeeded)
			return null;

		KeyBindingOverrides candidate = _appliedOverrides.CloneWithout(descriptor.SerializedId);
		candidate.Entries.Add(KeyBindingOverrideCodec.CreateEntry(descriptor.SerializedId, bindings));

		foreach ((TCommandId owner, List<KeyChord> ownerConflicts) in conflictsByOwner)
		{
			if (_catalog.TryGetDescriptor(owner, out CommandDescriptor<TCommandId>? ownerDescriptor))
				candidate.RemoveChords(ownerDescriptor.SerializedId, ownerConflicts, _overrideCodec);
		}

		return candidate;
	}

	/// <inheritdoc cref="IKeyBindingService{TCommandId}.Reset"/>
	/// <exception cref="ArgumentNullException"><paramref name="command"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">The resulting binding set cannot produce a consistent runtime state.</exception>
	public KeyBindingOutcome Reset(TCommandId command)
	{
		if (command is null)
			throw new ArgumentNullException(nameof(command));

		if (!_catalog.TryGetDescriptor(command, out CommandDescriptor<TCommandId>? descriptor))
			return KeyBindingOutcome.UnknownCommand;

		if (_appliedOverrides.Find(descriptor.SerializedId) is null)
			return KeyBindingOutcome.Succeeded;

		return Commit(_appliedOverrides.CloneWithout(descriptor.SerializedId), command);
	}

	/// <inheritdoc cref="IKeyBindingService{TCommandId}.ResetAll"/>
	public KeyBindingOutcome ResetAll()
	{
		// Every cataloged override is already cleared when only preserved entries remain, so nothing is
		// persisted and the notification is not raised.
		if (!_appliedOverrides.Entries.Exists(entry => _catalogSerializedIds.Contains(entry.SerializedId)))
			return KeyBindingOutcome.Succeeded;

		// Every cataloged override is dropped while entries for commands the catalog does not contain are preserved, as
		// IKeyBindingService guarantees for every written snapshot.
		KeyBindingOverrides snapshot = _appliedOverrides.CloneWhere(entry => !_catalogSerializedIds.Contains(entry.SerializedId));

		return Commit(snapshot, default, targetsAllCommands: true);
	}

	private static void ValidateProposal(IReadOnlyList<KeyChord> bindings, KeyBindingConflictPolicy conflictPolicy)
	{
		foreach (KeyChord binding in bindings)
		{
			if (!binding.IsInitialized)
				throw new ArgumentException("The proposed binding set contains an uninitialized key chord.", nameof(bindings));
		}

		if (!Enum.IsDefined<KeyBindingConflictPolicy>(conflictPolicy))
			throw new ArgumentException("The conflict policy must be a defined value.", nameof(conflictPolicy));
	}

	/// <summary>
	/// Validates the command's remapping policy and the proposed set itself, without inspecting the current bindings of other commands.
	/// </summary>
	private bool TryValidateCommandState(
		TCommandId command,
		IReadOnlyList<KeyChord> bindings,
		[NotNullWhen(true)] out CommandDescriptor<TCommandId>? descriptor,
		out KeyBindingOutcome failure
	)
	{
		failure = KeyBindingOutcome.Succeeded;

		if (!_catalog.TryGetDescriptor(command, out descriptor))
		{
			failure = KeyBindingOutcome.UnknownCommand;
			return false;
		}

		if (descriptor.RemappingPolicy == CommandRemappingPolicy.HostReserved)
		{
			failure = KeyBindingOutcome.HostReserved;
			return false;
		}

		if (descriptor.RemappingPolicy != CommandRemappingPolicy.Remappable)
		{
			failure = KeyBindingOutcome.HostManaged;
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

	/// <summary>Logs one diagnostic per override entry whose serialized identifier is not cataloged.</summary>
	private void LogUnknownOverrideEntries(KeyBindingOverrides overrides)
	{
		foreach (KeyBindingOverrideEntry entry in overrides.Entries)
		{
			if (!_catalogSerializedIds.Contains(entry.SerializedId))
				LogUnknownOverrideCommand(_logger, entry.SerializedId);
		}
	}

	/// <summary>Persists a candidate override snapshot and publishes its runtime maps.</summary>
	/// <param name="snapshot">The candidate override state to persist and publish.</param>
	/// <param name="command">The command the operation targets; ignored when <paramref name="targetsAllCommands"/> is <see langword="true"/>.</param>
	/// <param name="targetsAllCommands">
	/// <see langword="true"/> for an operation that targets no single command, so the <see cref="BindingsChanged"/> notification
	/// always sets <see cref="BindingsChangedEventArgs{TCommandId}.AffectsAllCommands"/>.
	/// </param>
	/// <remarks>
	/// <para>
	/// Everything that can fail - the display text formatter, which a host supplies, and the store - runs before any state is committed, so a store
	/// that reports failure leaves the applied and published states unchanged. The store receives its own copy of the candidate, and the applied
	/// state is another copy, so a store that mutates its argument cannot change the runtime state.
	/// </para>
	/// <para>
	/// The notification sets <see cref="BindingsChangedEventArgs{TCommandId}.AffectsAllCommands"/> when any command other than the target dispatches
	/// different bindings afterwards, whichever operation caused it: a chord a proposal took from another command, or a default that regains
	/// its chord once an override stops shadowing it.
	/// </para>
	/// </remarks>
	private KeyBindingOutcome Commit(KeyBindingOverrides snapshot, TCommandId? command, bool targetsAllCommands = false)
	{
		// The constructor accepts only CurrentVersion, so stamping it here keeps every persisted snapshot at the current schema version.
		snapshot.Version = KeyBindingOverrides.CurrentVersion;

		if (!TryAnalyzeCandidate(snapshot, out PublishedSnapshot<TCommandId>? maps, out KeyBindingOutcome outcome))
			return outcome;

		KeyBindingOverrides persisted = snapshot.Clone();
		PublishedSnapshot<TCommandId> published = maps.WithDisplayText(_displayTextFormatter);

		if (!_overridesStore.Save(persisted))
			return KeyBindingOutcome.PersistenceFailed;

		bool affectsAllCommands = targetsAllCommands || command is null || _published.DiffersForCommandsOtherThan(published, command);

		_appliedOverrides = snapshot.Clone();
		_published = published;

		BindingsChanged?.Invoke(this, new BindingsChangedEventArgs<TCommandId>(affectsAllCommands ? default : command, affectsAllCommands));

		return KeyBindingOutcome.Succeeded;
	}

	/// <summary>Determines whether a candidate override state can be published, and returns the maps to publish.</summary>
	/// <param name="candidate">The candidate state a mutation would persist.</param>
	/// <param name="maps">Receives the runtime maps when the candidate is consistent.</param>
	/// <param name="outcome">Receives the failure outcome when the candidate is not consistent.</param>
	/// <returns><see langword="true"/> when the candidate is consistent; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="InvalidOperationException">The candidate has two entries for one serialized identifier.</exception>
	/// <remarks>
	/// A failure a mutation can reach through its own effect on another command is returned as an outcome; a duplicate entry is the one failure
	/// no operation can produce, so it throws. A claim the candidate introduces must also not collide with a preserved override entry.
	/// </remarks>
	private bool TryAnalyzeCandidate(
		KeyBindingOverrides candidate,
		[NotNullWhen(true)] out PublishedSnapshot<TCommandId>? maps,
		out KeyBindingOutcome outcome
	)
	{
		maps = null;
		outcome = KeyBindingOutcome.Succeeded;

		KeyBindingMapBuilder<TCommandId>.MapBuildResult buildResult = _mapBuilder.Build(candidate, logDiagnostics: false);

		if (!buildResult.Succeeded)
		{
			// A duplicate entry is the only failure no operation can produce, so it is an invariant
			// violation; every other failure is a state a mutation can reach through its own effect on
			// another command.
			if (buildResult.IsDuplicateEntryFailure)
				throw new InvalidOperationException(buildResult.Reason);

			outcome = buildResult.IsPrefixConflictFailure ? KeyBindingOutcome.PrefixConflict : KeyBindingOutcome.Conflict;
			return false;
		}

		maps = buildResult.Maps;

		if (_conflictDetector.CollidesWithPreservedEntry(maps, _appliedOverrides, _published))
		{
			outcome = KeyBindingOutcome.Conflict;
			return false;
		}

		return true;
	}
}

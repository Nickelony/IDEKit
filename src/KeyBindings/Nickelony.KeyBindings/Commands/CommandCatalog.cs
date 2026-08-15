using System.Diagnostics.CodeAnalysis;

namespace Nickelony.KeyBindings;

/// <summary>
/// Catalog of commands that can participate in key bindings.
/// Each entry supplies a command identity, stable identifier, default bindings,
/// remapping policy, and context token.
/// </summary>
/// <typeparam name="TCommandId">The command identity type.</typeparam>
/// <remarks>
/// A <see cref="CommandDescriptor{TCommandId}.Context"/> token names a mutually exclusive host state.
/// Two default bindings conflict when their contexts can be active at the same time: the same token,
/// or either <see langword="null"/> (always active). The same chord in two different contexts is
/// therefore legal, while a chord claimed by two commands within one context - or by a token-scoped
/// and an always-active command - is rejected. The same co-active and prefix rules, shared with the
/// service, are what this constructor enforces.
/// </remarks>
public sealed class CommandCatalog<TCommandId>
	where TCommandId : notnull
{
	private readonly Dictionary<TCommandId, CommandDescriptor<TCommandId>> _descriptorsByCommand;

	/// <summary>
	/// Initializes a new instance of the <see cref="CommandCatalog{TCommandId}"/> class.
	/// </summary>
	/// <param name="descriptors">The descriptors to include in the catalog.</param>
	/// <exception cref="ArgumentNullException"><paramref name="descriptors"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// The list contains a <see langword="null"/> entry, a descriptor uses the <see langword="default"/>
	/// command value, a command or serialized identifier is duplicated, a context token is empty or
	/// whitespace-only, two descriptors claim the same default chord in co-active contexts, or one
	/// default chord is a strict prefix of another chord in a co-active context.
	/// </exception>
	public CommandCatalog(IReadOnlyList<CommandDescriptor<TCommandId>> descriptors)
	{
		ArgumentNullException.ThrowIfNull(descriptors);

		_descriptorsByCommand = new Dictionary<TCommandId, CommandDescriptor<TCommandId>>(descriptors.Count);
		var serializedIds = new HashSet<string>(StringComparer.Ordinal);
		var defaultClaims = new Dictionary<ContextChord, TCommandId>();
		var scopedChords = new HashSet<KeyChord>();

		foreach (CommandDescriptor<TCommandId> descriptor in descriptors)
		{
			if (descriptor is null)
				throw new ArgumentException("The descriptor list must not contain null entries.", nameof(descriptors));

			if (EqualityComparer<TCommandId>.Default.Equals(descriptor.Command, default))
				throw new ArgumentException("The default command value must not be cataloged.", nameof(descriptors));

			if (_descriptorsByCommand.ContainsKey(descriptor.Command))
				throw new ArgumentException($"Duplicate command in catalog: {descriptor.Command}.", nameof(descriptors));

			if (!serializedIds.Add(descriptor.SerializedId))
				throw new ArgumentException($"Duplicate serialized ID in catalog: {descriptor.SerializedId}.", nameof(descriptors));

			// An empty token is rejected rather than normalized: a silently normalized token would
			// never match the token the host publishes.
			if (descriptor.Context is not null && string.IsNullOrWhiteSpace(descriptor.Context))
			{
				throw new ArgumentException(
					$"The context of command '{descriptor.SerializedId}' must be null or a non-empty, non-whitespace token.",
					nameof(descriptors));
			}

			string? context = descriptor.Context ?? ContextChord.Always;

			foreach (KeyChord binding in descriptor.DefaultBindings)
			{
				// A chord two commands claim in co-active contexts can never resolve to one of them, so
				// the declaration is rejected. Different tokens never overlap, which is what makes the
				// same chord in two host modes legal.
				if (BindingClaimRules.TryGetCoActiveClaim(defaultClaims, new ContextChord(context, binding), out ContextChord conflictingClaim, out _))
				{
					throw new ArgumentException(
						DescribeDuplicateDefaultBinding(context, conflictingClaim.Context, binding),
						nameof(descriptors));
				}

				defaultClaims[new ContextChord(context, binding)] = descriptor.Command;

				if (context is not null)
					scopedChords.Add(binding);
			}

			_descriptorsByCommand[descriptor.Command] = descriptor;
		}

		// A chord that is a strict prefix of a co-active chord can never dispatch: the shorter chord
		// always resolves first and the longer one becomes unreachable, so the declaration is rejected
		// instead of being silently shadowed. A prefix shared by chords in different contexts stays
		// legal, because those chords are never active at the same time.
		foreach (ContextChord claim in defaultClaims.Keys)
		{
			ReadOnlySpan<KeyCombo> strokes = claim.Chord.Strokes;

			for (int length = 1; length < strokes.Length; length++)
			{
				var prefix = new KeyChord(strokes[..length].ToArray());

				if (BindingClaimRules.IsPrefixConflict(defaultClaims, scopedChords, claim, prefix))
				{
					throw new ArgumentException(
						DescribePrefixConflict(claim.Context, prefix, claim.Chord),
						nameof(descriptors));
				}
			}
		}
	}

	/// <summary>
	/// Gets all descriptors in the catalog.
	/// </summary>
	/// <remarks>Enumeration order is not specified.</remarks>
	public IReadOnlyCollection<CommandDescriptor<TCommandId>> Descriptors => _descriptorsByCommand.Values;

	/// <summary>
	/// Tries to get the descriptor for a command identity.
	/// </summary>
	/// <param name="command">The command identity to resolve.</param>
	/// <param name="descriptor">The matching descriptor when the method returns <see langword="true"/>; otherwise, <see langword="null"/>.</param>
	/// <returns><see langword="true"/> when the command is cataloged; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="command"/> is <see langword="null"/>.</exception>
	public bool TryGetDescriptor(TCommandId command, [NotNullWhen(true)] out CommandDescriptor<TCommandId>? descriptor)
	{
		// With a value-type identity (the common enum case) the guard is always false, so an unknown
		// command resolves to false instead of throwing.
		if (command is null)
			throw new ArgumentNullException(nameof(command));

		return _descriptorsByCommand.TryGetValue(command, out descriptor);
	}

	private static string DescribeDuplicateDefaultBinding(string? context, string? coActiveContext, KeyChord chord)
	{
		string describedChord = DescribeChord(chord);

		if (coActiveContext is null)
			return $"The default key binding {describedChord} is declared in context '{context}' and always.";

		if (context is null)
			return $"The default key binding {describedChord} is declared in context '{coActiveContext}' and always.";

		return $"Duplicate default key binding in context '{context}': {describedChord}.";
	}

	private static string DescribePrefixConflict(string? context, KeyChord prefix, KeyChord chord)
	{
		if (context is null)
			return $"A default key binding is a prefix of another: {DescribeChord(prefix)} and {DescribeChord(chord)}.";

		return $"A default key binding is a prefix of another in context '{context}': {DescribeChord(prefix)} and {DescribeChord(chord)}.";
	}

	private static string DescribeChord(KeyChord chord) => KeyDisplayTextFormatter.Default.GetDisplayText(chord);
}

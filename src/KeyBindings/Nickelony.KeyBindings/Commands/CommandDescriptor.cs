namespace Nickelony.KeyBindings;

/// <summary>
/// A command's catalog entry: its command identity, stable serialized identifier, default bindings,
/// remapping policy, and context token.
/// </summary>
/// <typeparam name="TCommandId">The command identity type.</typeparam>
/// <remarks>
/// <see cref="Context"/> is declared here rather than persisted: an override stores chords for a
/// serialized identifier, so a command's context travels with the catalog and never with user data.
/// </remarks>
public sealed class CommandDescriptor<TCommandId>
	where TCommandId : notnull
{
	/// <summary>
	/// Initializes a new instance of the <see cref="CommandDescriptor{TCommandId}"/> class.
	/// </summary>
	/// <param name="command">The command identity.</param>
	/// <param name="serializedId">The stable serialized identifier used for persistence.</param>
	/// <param name="remappingPolicy">The policy that controls who may change the command's bindings.</param>
	/// <param name="defaultBindings">The default bindings defined by the application.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="serializedId"/> or <paramref name="defaultBindings"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="serializedId"/> is empty, <paramref name="defaultBindings"/> contains an
	/// uninitialized chord or repeats a chord, or <paramref name="remappingPolicy"/> is not a
	/// defined policy value.
	/// </exception>
	public CommandDescriptor(
		TCommandId command,
		string serializedId,
		CommandRemappingPolicy remappingPolicy,
		params KeyChord[] defaultBindings)
	{
		ArgumentException.ThrowIfNullOrEmpty(serializedId);
		ArgumentNullException.ThrowIfNull(defaultBindings);

		if (!Enum.IsDefined<CommandRemappingPolicy>(remappingPolicy))
			throw new ArgumentException("The remapping policy must be a defined value.", nameof(remappingPolicy));

		foreach (KeyChord binding in defaultBindings)
		{
			if (!binding.IsInitialized)
				throw new ArgumentException("Default bindings must use initialized chords.", nameof(defaultBindings));
		}

		if (defaultBindings.Distinct().Count() != defaultBindings.Length)
			throw new ArgumentException("Default bindings must not repeat a chord.", nameof(defaultBindings));

		Command = command;
		SerializedId = serializedId;
		RemappingPolicy = remappingPolicy;

		// The bindings are copied so later mutation of the caller's array cannot alter the descriptor.
		DefaultBindings = Array.AsReadOnly<KeyChord>([.. defaultBindings]);
	}

	/// <summary>
	/// Gets the command this descriptor represents.
	/// </summary>
	/// <remarks>
	/// Catalog construction rejects the <see langword="default"/> command value, such as <c>None</c> for an
	/// enum identity.
	/// </remarks>
	public TCommandId Command { get; }

	/// <summary>
	/// Gets the stable identifier associated with this command in persisted overrides.
	/// </summary>
	public string SerializedId { get; }

	/// <summary>
	/// Gets the policy that controls who may change the command's bindings.
	/// </summary>
	/// <remarks>
	/// Validation rejects proposed binding sets for every policy other than
	/// <see cref="CommandRemappingPolicy.Remappable"/>. Loaded overrides are honored for
	/// <see cref="CommandRemappingPolicy.HostManaged"/> commands and ignored for
	/// <see cref="CommandRemappingPolicy.HostReserved"/> commands.
	/// </remarks>
	public CommandRemappingPolicy RemappingPolicy { get; }

	/// <summary>
	/// Gets the default chords used when no override applies.
	/// </summary>
	public IReadOnlyList<KeyChord> DefaultBindings { get; }

	/// <summary>
	/// Gets or initializes the context token this command's bindings belong to, or <see langword="null"/>
	/// when they are always active.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A context token names a mutually exclusive host state, such as one of an editor's view modes.
	/// The host publishes the token that is active when it dispatches a stroke, and only the bindings
	/// of that context and of the always-active <see langword="null"/> context can resolve.
	/// </para>
	/// <para>
	/// Two bindings conflict when their contexts can be active at the same time: the same token, or
	/// either <see langword="null"/>. <see cref="CommandCatalog{TCommandId}"/> rejects an empty or
	/// whitespace-only token instead of normalizing it, because a silently normalized token would
	/// never match the token the host publishes.
	/// </para>
	/// </remarks>
	public string? Context { get; init; }
}

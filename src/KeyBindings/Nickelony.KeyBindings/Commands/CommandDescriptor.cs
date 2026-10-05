namespace Nickelony.KeyBindings;

/// <summary>A command's catalog entry.</summary>
/// <typeparam name="TCommandId">The command identity type.</typeparam>
/// <remarks>
/// <see cref="Context"/> is declared here rather than persisted: an override stores chords for a serialized identifier, so a command's context
/// travels with the catalog and never with user data.
/// </remarks>
public sealed class CommandDescriptor<TCommandId>
	where TCommandId : notnull
{
	/// <param name="command">The command identity.</param>
	/// <param name="serializedId">The stable serialized identifier used for persistence.</param>
	/// <param name="remappingPolicy">The policy that controls who may change the command's bindings.</param>
	/// <param name="defaultBindings">The default bindings defined by the application.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="serializedId"/> or <paramref name="defaultBindings"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="serializedId"/> is empty or whitespace-only, <paramref name="defaultBindings"/> contains an uninitialized chord
	/// or repeats a chord, or <paramref name="remappingPolicy"/> is not a defined policy value.
	/// </exception>
	public CommandDescriptor(TCommandId command, string serializedId, CommandRemappingPolicy remappingPolicy, params KeyChord[] defaultBindings)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(serializedId);
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

	/// <summary>The command identity this descriptor represents.</summary>
	/// <remarks>
	/// Catalog construction rejects the <see langword="default"/> value, such as <c>None</c> for an enum identity.
	/// </remarks>
	public TCommandId Command { get; }

	/// <summary>The stable identifier associated with this command in persisted overrides.</summary>
	public string SerializedId { get; }

	/// <summary>The policy that controls who may change the command's bindings.</summary>
	public CommandRemappingPolicy RemappingPolicy { get; }

	/// <summary>The default chords used when no override applies.</summary>
	public IReadOnlyList<KeyChord> DefaultBindings { get; }

	/// <summary>The context token this command's bindings belong to, or <see langword="null"/> when they are always active.</summary>
	/// <remarks>
	/// A context token names a mutually exclusive host state, such as an editor view mode; the host publishes
	/// the active token when it dispatches a stroke, and only that context's bindings and the always-active <see langword="null"/> context resolve.
	/// <see cref="CommandCatalog{TCommandId}"/> rejects an empty or whitespace-only token instead of normalizing it, because a normalized token
	/// would never match what the host publishes.
	/// </remarks>
	public string? Context { get; init; }

	/// <summary>The human-readable name of the command, or <see langword="null"/> when a command list shows its <see cref="SerializedId"/>.</summary>
	/// <remarks>
	/// The title is display text for a command palette, a menu or a tooltip, so it is not persisted and takes no part in binding resolution.
	/// <see cref="CommandCatalog{TCommandId}"/> rejects an empty or whitespace-only title.
	/// </remarks>
	public string? Title { get; init; }

	/// <summary>The group a command list shows the command under, such as <c>File</c>, or <see langword="null"/> when it has none.</summary>
	/// <remarks>
	/// <see cref="CommandCatalog{TCommandId}"/> rejects an empty or whitespace-only category.
	/// </remarks>
	public string? Category { get; init; }
}

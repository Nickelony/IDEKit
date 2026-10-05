namespace Nickelony.KeyBindings;

/// <summary>Loads and persists the key binding overrides applied by a <c>KeyBindingService</c>.</summary>
/// <remarks>
/// The store owns the settings document and the service owns the runtime state, loading once at construction and saving on every committed mutation.
/// A throwing <see cref="Load"/> propagates from the constructor; a throwing <see cref="Save"/> propagates to the triggering operation and leaves
/// the runtime unchanged, and a store that writes before throwing can leave the persisted document ahead until the next construction.
/// </remarks>
public interface IKeyBindingOverrideStore
{
	/// <summary>Loads the overrides to apply.</summary>
	/// <returns>The overrides to apply; must not be <see langword="null"/> and must be a complete graph.</returns>
	KeyBindingOverrides Load();

	/// <summary>Persists a committed override snapshot.</summary>
	/// <param name="snapshot">The snapshot to persist. The service does not mutate it afterwards.</param>
	/// <returns>
	/// <see langword="true"/> when the snapshot was persisted; <see langword="false"/> makes the operation fail
	/// with <c>KeyBindingOutcome.PersistenceFailed</c> and leaves the runtime state unchanged.
	/// </returns>
	bool Save(KeyBindingOverrides snapshot);
}

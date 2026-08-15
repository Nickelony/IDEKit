namespace Nickelony.KeyBindings;

/// <summary>
/// Loads and persists the key binding overrides applied by a <see cref="KeyBindingService{TCommandId}"/>.
/// </summary>
/// <remarks>
/// <para>
/// The service reads the overrides once through <see cref="Load"/> when it is constructed and calls
/// <see cref="Save"/> for every committed mutation. The store owns the settings document; the
/// service owns the runtime state and never mutates the instances it receives.
/// </para>
/// <para>
/// An exception thrown by <see cref="Load"/> propagates from the service constructor. An exception
/// thrown by <see cref="Save"/> propagates to the operation that triggered it and leaves the runtime
/// state unchanged.
/// </para>
/// <para>
/// The service persists before it publishes, so a store that writes some or all of the snapshot and
/// then throws leaves the persisted document ahead of the runtime state. That divergence is not
/// repaired: the runtime keeps the bindings it had, and the host reconciles the document the next
/// time the service is constructed from it. A store that must not diverge should make
/// <see cref="Save"/> atomic and report failure by returning <see langword="false"/>.
/// </para>
/// </remarks>
public interface IKeyBindingOverridesStore
{
	/// <summary>
	/// Loads the overrides to apply.
	/// </summary>
	/// <returns>The overrides to apply; must not be <see langword="null"/> and must be a complete graph.</returns>
	KeyBindingOverrides Load();

	/// <summary>
	/// Persists a committed override snapshot.
	/// </summary>
	/// <param name="snapshot">The snapshot to persist. The service does not mutate it afterwards.</param>
	/// <returns>
	/// <see langword="true"/> when the snapshot was persisted; <see langword="false"/> makes the
	/// operation fail with <see cref="KeyBindingOutcome.PersistenceFailed"/> and leaves the runtime
	/// state unchanged.
	/// </returns>
	bool Save(KeyBindingOverrides snapshot);
}

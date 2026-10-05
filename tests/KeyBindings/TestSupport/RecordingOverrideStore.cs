namespace Nickelony.KeyBindings.Testing;

/// <summary>In-memory overrides store that records the snapshots it is asked to persist.</summary>
internal sealed class RecordingOverrideStore : IKeyBindingOverrideStore
{
	private readonly KeyBindingOverrides _loaded;
	private readonly Func<KeyBindingOverrides, bool> _save;

	internal RecordingOverrideStore(KeyBindingOverrides? overrides = null, Func<KeyBindingOverrides, bool>? save = null)
	{
		_loaded = overrides ?? new KeyBindingOverrides();
		_save = save ?? (_ => true);
	}

	internal int SaveCount { get; private set; }

	public KeyBindingOverrides Load() => _loaded;

	public bool Save(KeyBindingOverrides snapshot)
	{
		SaveCount++;
		return _save(snapshot);
	}
}

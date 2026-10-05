namespace Nickelony.QuickPick;

/// <summary>Keeps the recently accepted keys of each mode: the latest first, each key once, at most a fixed number per mode.</summary>
/// <remarks>
/// The tracker owns the move-to-front and the capacity rules once, and a host store only reads and writes the document. It loads the store
/// when it is created and saves the whole document after each record. A store that throws is contained: a failed load starts with no keys,
/// and a failed save keeps the keys in memory for the session.
/// </remarks>
internal sealed class QuickPickRecentTracker
{
	private readonly Dictionary<string, List<string>> _keysByPrefix = new(StringComparer.Ordinal);
	private readonly IQuickPickRecentStore? _store;
	private readonly int _capacity;
	private readonly Action<Exception> _reportFault;

	/// <param name="store">The store that persists the keys, or <see langword="null"/> to keep them for the life of the tracker.</param>
	/// <param name="capacity">The number of keys kept per mode.</param>
	/// <param name="reportFault">Reports a fault of the store.</param>
	internal QuickPickRecentTracker(IQuickPickRecentStore? store, int capacity, Action<Exception> reportFault)
	{
		_store = store;
		_capacity = capacity;
		_reportFault = reportFault;

		if (
			store is not null
			&& CallbackContainment.TryRun(store.Load, reportFault, out IReadOnlyDictionary<string, IReadOnlyList<string>>? loaded)
			&& loaded is not null
		)
		{
			Adopt(loaded);
		}
	}

	/// <summary>Gets the keys of a mode.</summary>
	/// <param name="prefix">The prefix the mode was offered under.</param>
	/// <returns>A copy of the keys, the latest first.</returns>
	internal IReadOnlyList<string> GetRecentKeys(string prefix) => _keysByPrefix.TryGetValue(prefix, out List<string>? keys) ? [.. keys] : [];

	/// <summary>Records an accepted key as the latest of its mode and saves the document.</summary>
	/// <param name="prefix">The prefix the mode was offered under.</param>
	/// <param name="key">The key of the accepted item.</param>
	internal void Record(string prefix, string key)
	{
		if (!_keysByPrefix.TryGetValue(prefix, out List<string>? keys))
			_keysByPrefix[prefix] = keys = [];

		keys.Remove(key);
		keys.Insert(0, key);

		if (keys.Count > _capacity)
			keys.RemoveRange(_capacity, keys.Count - _capacity);

		if (_store is not null)
		{
			var snapshot = _keysByPrefix.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)[.. pair.Value], StringComparer.Ordinal);
			CallbackContainment.TryRun(() => _store.Save(snapshot), _reportFault, out _);
		}
	}

	private void Adopt(IReadOnlyDictionary<string, IReadOnlyList<string>> loaded)
	{
		foreach ((string prefix, IReadOnlyList<string> keys) in loaded)
		{
			if (prefix is null || keys is null)
				continue;

			// A hand-edited document can repeat a key or hold more than the capacity, so the tracker restores its own rules.
			List<string> adopted = [.. keys.Where(key => key is not null).Distinct(StringComparer.Ordinal).Take(_capacity)];

			if (adopted.Count > 0)
				_keysByPrefix[prefix] = adopted;
		}
	}
}

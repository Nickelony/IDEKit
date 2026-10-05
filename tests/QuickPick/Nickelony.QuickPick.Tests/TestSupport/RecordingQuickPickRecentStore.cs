namespace Nickelony.QuickPick.Tests;

/// <summary>A recent store that keeps its document in memory and records every save, so a test reads what the controller persisted.</summary>
internal sealed class RecordingQuickPickRecentStore(IReadOnlyDictionary<string, IReadOnlyList<string>>? initial = null) : IQuickPickRecentStore
{
	/// <summary>The documents the controller saved, in order.</summary>
	public List<IReadOnlyDictionary<string, IReadOnlyList<string>>> Saves { get; } = [];

	/// <summary>The document the next load returns.</summary>
	public IReadOnlyDictionary<string, IReadOnlyList<string>> Document { get; set; } = initial ?? new Dictionary<string, IReadOnlyList<string>>();

	/// <summary>The keys of a mode in the last saved document, or of the initial document before any save.</summary>
	public IReadOnlyList<string> KeysOf(string prefix) => Document.TryGetValue(prefix, out IReadOnlyList<string>? keys) ? keys : [];

	public IReadOnlyDictionary<string, IReadOnlyList<string>> Load() => Document;

	public bool Save(IReadOnlyDictionary<string, IReadOnlyList<string>> keysByPrefix)
	{
		Saves.Add(keysByPrefix);
		Document = keysByPrefix;

		return true;
	}
}

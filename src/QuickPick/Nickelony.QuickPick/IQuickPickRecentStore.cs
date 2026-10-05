namespace Nickelony.QuickPick;

/// <summary>
/// Persists the keys of the items a user accepted, so a quick pick lists them first; a host implements it to keep them between runs.
/// </summary>
/// <remarks>
/// <para>
/// The store holds a document that the controller owns, as <c>IKeyBindingOverrideStore</c> does for the key-binding overrides: the controller loads
/// it once when it is created, applies the move-to-front and the capacity rules itself, and saves the whole document after each accept.
/// A store therefore needs no ordering logic. The controller calls both members on its dispatcher thread, one call at a time.
/// </para>
/// <para>
/// The keys are kept by the prefix of the mode that listed the item. A store that fails to read returns an empty document, and one that fails
/// to write returns <see langword="false"/>, so a damaged file costs the recent items and nothing else.
/// </para>
/// </remarks>
public interface IQuickPickRecentStore
{
	/// <summary>Reads the keys kept by an earlier run.</summary>
	/// <returns>The keys of each mode by its prefix, the latest first, or an empty document when nothing was kept.</returns>
	IReadOnlyDictionary<string, IReadOnlyList<string>> Load();

	/// <summary>Keeps the keys of every mode.</summary>
	/// <param name="keysByPrefix">The keys of each mode by its prefix, the latest first, which replace the kept document.</param>
	/// <returns><see langword="true"/> when the keys were kept; otherwise, <see langword="false"/>.</returns>
	bool Save(IReadOnlyDictionary<string, IReadOnlyList<string>> keysByPrefix);
}

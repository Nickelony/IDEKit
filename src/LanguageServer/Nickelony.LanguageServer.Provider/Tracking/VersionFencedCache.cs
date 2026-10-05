namespace Nickelony.LanguageServer.Provider;

/// <summary>Caches the latest version-fenced payload of a tracked document, such as its diagnostics or semantic tokens.</summary>
/// <typeparam name="TItem">The cached item type.</typeparam>
/// <remarks>
/// <para>
/// Items are stored as a read-only snapshot detached from the source collection.
/// Version acceptance follows <see cref="DocumentVersionPolicy.TryAccept"/>; an unversioned payload (version <c>0</c>, or no comparable version)
/// is stored only while no version fence is in force, because it cannot be shown to be newer and must not replace content already cached
/// for a known version. The cache enforces the fence itself rather than trusting its callers, so a stale payload that slips past a caller-side
/// pre-check is still rejected here.
/// </para>
/// <para>
/// <see cref="SourceContent"/> carries the content snapshot the items' offsets refer to when the producer supplies
/// one (the diagnostics path does, so its offsets can be mapped back to positions later).
/// </para>
/// <para>
/// The cache is safe for concurrent readers with a single writer: a write publishes one immutable (version, items, source content)
/// snapshot in a single assignment, and every property read observes one such snapshot. The properties read independently, so a caller that needs
/// a consistent version, item list and source content must read them under the owning tracker's lock.
/// </para>
/// </remarks>
internal sealed class VersionFencedCache<TItem> : IPayloadCache
{
	private static readonly IReadOnlyList<TItem> s_emptyItems = Array.AsReadOnly<TItem>([]);

	private Entry _entry = new(0, s_emptyItems, null);

	internal IReadOnlyList<TItem> Items => Volatile.Read(ref _entry).Items;

	/// <summary>The synchronized document version associated with the cached items.</summary>
	internal int Version => Volatile.Read(ref _entry).Version;

	/// <summary>The content snapshot the cached items' offsets refer to, or <see langword="null"/> when the producer supplied none.</summary>
	internal string? SourceContent => Volatile.Read(ref _entry).SourceContent;

	/// <summary>Clears the cached payload, including the source-content snapshot.</summary>
	public void Clear() => Volatile.Write(ref _entry, new Entry(0, s_emptyItems, null));

	/// <summary>
	/// Drops the synchronized version stamp while preserving the cached payload, so the next stored payload is accepted regardless of the version
	/// it reports.
	/// </summary>
	public void ResetVersionStamp()
	{
		Entry current = Volatile.Read(ref _entry);
		Volatile.Write(ref _entry, new Entry(0, current.Items, current.SourceContent));
	}

	/// <summary>Stores a payload when its version is not stale relative to the current cache.</summary>
	/// <param name="version">The synchronized document version associated with the payload.</param>
	/// <param name="items">The items to cache, or <see langword="null"/> to cache an empty list.</param>
	/// <param name="sourceContent">
	/// The content snapshot the items' offsets refer to, or <see langword="null"/> when the items carry no offsets or the producer supplied none.
	/// </param>
	/// <returns><see langword="true"/> when the payload was stored; otherwise, <see langword="false"/>.</returns>
	internal bool TryStore(int version, IReadOnlyList<TItem>? items, string? sourceContent = null)
	{
		Entry current = Volatile.Read(ref _entry);

		if (!DocumentVersionPolicy.TryAccept(current.Version, version, out int acceptedVersion))
			return false;

		// An unversioned payload does not advance the stamp, so it must not replace content already cached for a
		// known version: without a comparable version it cannot be shown to be newer. It is stored only while no
		// version fence is in force (the fresh cache, a cleared cache, or a cache whose stamp was reset), which
		// preserves the reset contract that the next payload is accepted regardless of the version it reports.
		if (version <= 0 && current.Version > 0)
			return false;

		Volatile.Write(ref _entry, new Entry(acceptedVersion, CreateReadOnlySnapshot(items), sourceContent));
		return true;
	}

	private static IReadOnlyList<TItem> CreateReadOnlySnapshot(IReadOnlyList<TItem>? items) =>
		items is null || items.Count == 0 ? s_emptyItems : Array.AsReadOnly([.. items]);

	/// <summary>
	/// An immutable (version, items, source-content) triple published as one reference so a reader cannot observe the fields of one store mixed
	/// with fields from another write.
	/// </summary>
	private sealed class Entry(int version, IReadOnlyList<TItem> items, string? sourceContent)
	{
		internal int Version { get; } = version;

		internal IReadOnlyList<TItem> Items { get; } = items;

		internal string? SourceContent { get; } = sourceContent;
	}
}

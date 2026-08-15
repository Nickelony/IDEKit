namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Caches the latest version-fenced payload of a tracked document, such as its diagnostics or semantic tokens.
/// </summary>
/// <remarks>
/// <para>
/// Items are stored as a read-only snapshot detached from the source collection. Version acceptance follows
/// <see cref="DocumentVersionPolicy.TryAccept"/>; an unversioned payload (version <c>0</c>, or no comparable
/// version) is stored only while no version fence is in force, because with no comparable version it cannot be
/// shown to be newer and must not replace content already cached for a known version.
/// </para>
/// <para>
/// The cache enforces the fence itself rather than trusting its callers: a caller-side version check is an
/// early filter, and a stale payload is still rejected here when one slips through.
/// </para>
/// <para>
/// <see cref="SourceContent"/> carries the content snapshot the items' offsets refer to when the producer
/// supplies one (the diagnostics path does, so its offsets can be mapped back to positions later).
/// </para>
/// <para>
/// The cache is safe for concurrent readers with a single writer: the version, items, and source content are
/// published together through one immutable snapshot, so a reader can never observe the fields of one store
/// mixed with the fields of another.
/// </para>
/// <para>
/// This is framework-internal caching machinery, so the type is <see langword="internal"/>.
/// </para>
/// </remarks>
/// <typeparam name="TItem">The cached item type.</typeparam>
internal sealed class VersionFencedPayloadCache<TItem>
{
	private static readonly IReadOnlyList<TItem> s_emptyItems = Array.AsReadOnly<TItem>([]);

	private Payload _payload = new(0, s_emptyItems, null);

	/// <summary>
	/// Gets the currently cached items.
	/// </summary>
	public IReadOnlyList<TItem> Items => Volatile.Read(ref _payload).Items;

	/// <summary>
	/// Gets the synchronized document version associated with the cached items.
	/// </summary>
	public int Version => Volatile.Read(ref _payload).Version;

	/// <summary>
	/// Gets the content snapshot the cached items' offsets refer to, or <see langword="null"/> when the
	/// producer supplied none.
	/// </summary>
	public string? SourceContent => Volatile.Read(ref _payload).SourceContent;

	/// <summary>
	/// Clears the cached payload, including the source-content snapshot.
	/// </summary>
	public void Clear() => Volatile.Write(ref _payload, new Payload(0, s_emptyItems, null));

	/// <summary>
	/// Drops the synchronized version stamp while preserving the cached payload, so the next stored payload
	/// is accepted regardless of the version it reports.
	/// </summary>
	public void ResetVersionStamp()
	{
		Payload current = Volatile.Read(ref _payload);
		Volatile.Write(ref _payload, new Payload(0, current.Items, current.SourceContent));
	}

	/// <summary>
	/// Stores a payload when its version is not stale relative to the current cache.
	/// </summary>
	/// <param name="version">The synchronized document version associated with the payload.</param>
	/// <param name="items">The items to cache, or <see langword="null"/> to cache an empty list.</param>
	/// <param name="sourceContent">
	/// The content snapshot the items' offsets refer to, or <see langword="null"/> when the items carry no
	/// offsets or the producer supplied none.
	/// </param>
	/// <returns><see langword="true"/> when the payload was stored; otherwise, <see langword="false"/>.</returns>
	public bool TryStore(int version, IReadOnlyList<TItem>? items, string? sourceContent = null)
	{
		Payload current = Volatile.Read(ref _payload);

		if (!DocumentVersionPolicy.TryAccept(current.Version, version, out int acceptedVersion))
			return false;

		// An unversioned payload does not advance the stamp, so it must not replace content already cached for a
		// known version: without a comparable version it cannot be shown to be newer. It is stored only while no
		// version fence is in force (the fresh cache, a cleared cache, or a cache whose stamp was reset), which
		// preserves the reset contract that the next payload is accepted regardless of the version it reports.
		if (version <= 0 && current.Version > 0)
			return false;

		Volatile.Write(ref _payload, new Payload(acceptedVersion, CreateReadOnlySnapshot(items), sourceContent));
		return true;
	}

	private static IReadOnlyList<TItem> CreateReadOnlySnapshot(IReadOnlyList<TItem>? items)
	{
		return items is null || items.Count == 0
			? s_emptyItems
			: Array.AsReadOnly([.. items]);
	}

	/// <summary>
	/// An immutable (version, items, source-content) triple published as one reference so a reader cannot observe
	/// the fields of one store mixed with the fields of another.
	/// </summary>
	private sealed class Payload(int version, IReadOnlyList<TItem> items, string? sourceContent)
	{
		public int Version { get; } = version;

		public IReadOnlyList<TItem> Items { get; } = items;

		public string? SourceContent { get; } = sourceContent;
	}
}

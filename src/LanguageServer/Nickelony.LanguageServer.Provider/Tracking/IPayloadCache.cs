namespace Nickelony.LanguageServer.Provider;

/// <summary>Caches one feature's server payload for a tracked document, which the tracker clears and unfences on the feature's behalf.</summary>
/// <remarks>
/// The tracker reads and writes a cache only under its own lock, so an implementation needs no synchronization of its own.
/// </remarks>
internal interface IPayloadCache
{
	/// <summary>Drops the cached payload because it describes content that is gone.</summary>
	void Clear();

	/// <summary>
	/// Drops the synchronized version stamp and keeps the cached payload, so the next stored payload is accepted regardless of the version
	/// it reports.
	/// </summary>
	void ResetVersionStamp();
}

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>
/// Covers the version fence of the shared payload cache directly: unknown versions, staleness, null
/// item lists, and the stamp reset.
/// </summary>
[TestClass]
public sealed class VersionFencedPayloadCacheTests
{
	[TestMethod]
	public void TryStore_NullItems_StoresAnEmptySnapshot()
	{
		var cache = new VersionFencedPayloadCache<string>();

		Assert.IsTrue(cache.TryStore(4, null));
		Assert.AreEqual(0, cache.Items.Count);
		Assert.AreEqual(4, cache.Version);
	}

	[TestMethod]
	public void TryStore_UnknownVersion_IntoAnEmptyCache_KeepsTheStampAtZero()
	{
		var cache = new VersionFencedPayloadCache<string>();

		Assert.IsTrue(cache.TryStore(0, ["b"]));

		// An unversioned payload into a fence-free cache is stored without advancing the stamp.
		Assert.AreEqual(0, cache.Version);
		Assert.AreEqual("b", cache.Items[0]);
	}

	[TestMethod]
	public void TryStore_UnknownVersion_DoesNotReplaceVersionedContent()
	{
		var cache = new VersionFencedPayloadCache<string>();

		Assert.IsTrue(cache.TryStore(4, ["a"]));
		Assert.IsFalse(cache.TryStore(0, ["b"]));

		// The versioned payload and its stamp are preserved; an unversioned payload cannot be shown to be newer.
		Assert.AreEqual(4, cache.Version);
		Assert.AreEqual("a", cache.Items[0]);
	}

	[TestMethod]
	public void TryStore_StalePositiveVersion_IsRejected()
	{
		var cache = new VersionFencedPayloadCache<string>();

		Assert.IsTrue(cache.TryStore(4, ["a"]));
		Assert.IsFalse(cache.TryStore(3, ["b"]));
		Assert.AreEqual("a", cache.Items[0]);
	}

	[TestMethod]
	public void TryStore_EmptyList_StoresAnEmptySnapshotAndAdvancesTheStamp()
	{
		var cache = new VersionFencedPayloadCache<string>();

		Assert.IsTrue(cache.TryStore(4, []));

		Assert.AreEqual(0, cache.Items.Count);
		Assert.AreEqual(4, cache.Version);
	}

	[TestMethod]
	public void Clear_ResetsItemsVersionAndSourceContent()
	{
		var cache = new VersionFencedPayloadCache<string>();

		Assert.IsTrue(cache.TryStore(4, ["a"], sourceContent: "source"));
		cache.Clear();

		Assert.AreEqual(0, cache.Items.Count);
		Assert.AreEqual(0, cache.Version);
		Assert.IsNull(cache.SourceContent);
	}

	[TestMethod]
	public void ResetVersionStamp_AcceptsTheNextPayloadRegardlessOfVersion()
	{
		var cache = new VersionFencedPayloadCache<string>();

		Assert.IsTrue(cache.TryStore(4, ["a"]));
		cache.ResetVersionStamp();

		Assert.IsTrue(cache.TryStore(1, ["b"]));
		Assert.AreEqual(1, cache.Version);
		Assert.AreEqual("b", cache.Items[0]);
	}
}

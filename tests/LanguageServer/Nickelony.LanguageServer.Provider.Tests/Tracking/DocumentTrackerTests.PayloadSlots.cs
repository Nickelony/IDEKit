namespace Nickelony.LanguageServer.Provider.Tests;

public sealed partial class DocumentTrackerTests
{
	private static readonly PayloadSlot<RecordingPayloadCache> s_firstSlot = new();
	private static readonly PayloadSlot<RecordingPayloadCache> s_secondSlot = new();

	[TestMethod]
	public void ReadPayload_ForAnUntrackedDocument_ReturnsTheFallbackWithoutRunningTheRead()
	{
		var tracker = new DocumentTracker();
		bool read = false;

		int value = tracker.ReadPayload(
			filePath: Script("untracked.ext"),
			slot: s_firstSlot,

			read: cache =>
			{
				read = true;
				return cache.Value;
			},

			fallbackValue: -1
		);

		Assert.AreEqual(-1, value);
		Assert.IsFalse(read);
	}

	[TestMethod]
	public void TryStorePayload_ForTheCurrentVersion_StoresIntoTheCacheOfTheSlotOnly()
	{
		string filePath = Script("slots.ext");
		DocumentTracker tracker = TrackedDocument(filePath, out DocumentSnapshot document);

		Assert.IsTrue(
			tracker.TryStorePayload(
				filePath,
				slot: s_firstSlot,
				expectedDocumentVersion: document.Version,

				store: cache =>
				{
					cache.Value = 5;
					return true;
				}
			)
		);

		Assert.AreEqual(5, tracker.ReadPayload(filePath, slot: s_firstSlot, read: static cache => cache.Value, fallbackValue: -1));

		Assert.AreEqual(
			0,
			tracker.ReadPayload(filePath, slot: s_secondSlot, read: static cache => cache.Value, fallbackValue: -1),
			"A second slot keeps its own cache."
		);
	}

	[TestMethod]
	public void TryStorePayload_WhenTheTrackedVersionAdvanced_DoesNotRunTheStore()
	{
		string filePath = Script("slots.ext");
		DocumentTracker tracker = TrackedDocument(filePath, out DocumentSnapshot staleDocument);

		tracker.Synchronize(filePath, "return 2");

		bool storeRan = false;

		bool stored = tracker.TryStorePayload(
			filePath,
			slot: s_firstSlot,
			expectedDocumentVersion: staleDocument.Version,

			store: _ =>
			{
				storeRan = true;
				return true;
			}
		);

		Assert.IsFalse(stored);
		Assert.IsFalse(storeRan, "The version fence runs before the cache sees the payload.");
	}

	[TestMethod]
	public void TryStorePayload_WhenTheCacheRejectsThePayload_ReportsNotStored()
	{
		string filePath = Script("slots.ext");
		DocumentTracker tracker = TrackedDocument(filePath, out DocumentSnapshot document);

		Assert.IsFalse(tracker.TryStorePayload(filePath, slot: s_firstSlot, expectedDocumentVersion: document.Version, store: static _ => false));
	}

	[TestMethod]
	public void InvalidateServerSynchronization_ResetsTheVersionStampOfEverySlotThatHoldsACache()
	{
		string filePath = Script("slots.ext");
		DocumentTracker tracker = TrackedDocument(filePath, out _);

		// Reading a slot creates its cache, so both slots hold one before the invalidation.
		_ = tracker.ReadPayload(filePath, slot: s_firstSlot, read: static cache => cache.Value, fallbackValue: -1);
		_ = tracker.ReadPayload(filePath, slot: s_secondSlot, read: static cache => cache.Value, fallbackValue: -1);

		Assert.IsTrue(tracker.InvalidateServerSynchronization(filePath));

		Assert.AreEqual(1, tracker.ReadPayload(filePath, slot: s_firstSlot, read: static cache => cache.ResetCount, fallbackValue: -1));
		Assert.AreEqual(1, tracker.ReadPayload(filePath, slot: s_secondSlot, read: static cache => cache.ResetCount, fallbackValue: -1));
	}

	[TestMethod]
	public void Rename_WithChangedContent_ClearsEverySlotThatHoldsACache()
	{
		string oldFilePath = Script("slots-old.ext");
		string newFilePath = Script("slots-new.ext");

		DocumentTracker tracker = TrackedDocument(oldFilePath, out DocumentSnapshot document);

		Assert.IsTrue(
			tracker.TryStorePayload(
				filePath: oldFilePath,
				slot: s_firstSlot,
				expectedDocumentVersion: document.Version,
				store: static cache => (cache.Value = 7) > 0
			)
		);

		_ = tracker.ReadPayload(filePath: oldFilePath, slot: s_secondSlot, read: static cache => cache.Value, fallbackValue: -1);

		Assert.AreEqual(DocumentRenameOutcome.Renamed, tracker.Rename(oldFilePath, newFilePath, "return 2").Outcome);

		Assert.AreEqual(0, tracker.ReadPayload(filePath: newFilePath, slot: s_firstSlot, read: static cache => cache.Value, fallbackValue: -1));
		Assert.AreEqual(1, tracker.ReadPayload(filePath: newFilePath, slot: s_secondSlot, read: static cache => cache.ClearCount, fallbackValue: -1));
	}

	[TestMethod]
	public void Rename_WithUnchangedContent_KeepsTheCachedPayloads()
	{
		string oldFilePath = Script("slots-old.ext");
		string newFilePath = Script("slots-new.ext");

		DocumentTracker tracker = TrackedDocument(oldFilePath, out DocumentSnapshot document);

		Assert.IsTrue(
			tracker.TryStorePayload(
				filePath: oldFilePath,
				slot: s_firstSlot,
				expectedDocumentVersion: document.Version,
				store: static cache => (cache.Value = 7) > 0
			)
		);

		Assert.AreEqual(DocumentRenameOutcome.Renamed, tracker.Rename(oldFilePath, newFilePath, "return 1").Outcome);

		Assert.AreEqual(7, tracker.ReadPayload(filePath: newFilePath, slot: s_firstSlot, read: static cache => cache.Value, fallbackValue: -1));
	}

	private static DocumentTracker TrackedDocument(string filePath, out DocumentSnapshot document)
	{
		var tracker = new DocumentTracker();
		tracker.Synchronize(filePath, "return 1", leases: DocumentLeaseKinds.Open);

		document = tracker.GetDocumentSnapshot(filePath) ?? throw new InvalidOperationException("The document should be tracked.");

		return tracker;
	}

	private sealed class RecordingPayloadCache : IPayloadCache
	{
		public int Value { get; set; }

		public int ClearCount { get; private set; }

		public int ResetCount { get; private set; }

		public void Clear()
		{
			ClearCount++;
			Value = 0;
		}

		public void ResetVersionStamp() => ResetCount++;
	}
}

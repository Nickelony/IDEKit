namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers the keyed set of tracked records: lookup under the path policy, access ordering, idle eviction and the restart marking.</summary>
[TestClass]
public sealed class TrackedDocumentSetTests
{
	private sealed class TestHost(LocalPathComparisonPolicy? pathComparison = null)
	{
		private readonly object _syncRoot = new();

		internal TrackedDocumentSet Create() => new(_syncRoot, pathComparison ?? LocalPathComparisonPolicy.CaseSensitive);

		internal T Locked<T>(Func<T> action)
		{
			lock (_syncRoot)
				return action();
		}
	}

	private static TrackedDocumentState NewState(
		string fileName,
		long accessStamp = 1,
		bool isOpen = true,
		int openLeases = 0,
		int requestLeases = 0,
		string content = "content"
	)
	{
		string path = TestPaths.Script(fileName);

		return new TrackedDocumentState(
			new TrackedDocumentDescriptor(
				FilePath: path,
				Uri: new Uri(path).AbsoluteUri,
				content,
				Version: 1,
				isOpen,
				OpenLeaseCount: openLeases,
				RequestLeaseCount: requestLeases,
				LastAccessStamp: accessStamp
			)
		);
	}

	private static (TestHost TestHost, TrackedDocumentSet Set) CreateSet(LocalPathComparisonPolicy? pathComparison = null)
	{
		var fixture = new TestHost(pathComparison);
		return (fixture, fixture.Create());
	}

	[TestMethod]
	public void NextAccessStamp_IncreasesWithEveryCall()
	{
		(TestHost fixture, TrackedDocumentSet set) = CreateSet();

		long first = fixture.Locked(set.NextAccessStamp);
		long second = fixture.Locked(set.NextAccessStamp);
		long third = fixture.Locked(set.NextAccessStamp);

		Assert.IsLessThan(second, first);
		Assert.IsLessThan(third, second);
	}

	[TestMethod]
	public void Set_ThenTryGetAndContains_FindTheRecordByItsPath()
	{
		(TestHost fixture, TrackedDocumentSet set) = CreateSet();
		TrackedDocumentState state = NewState("a.test");

		fixture.Locked(() =>
		{
			set.Set(state.FilePath, state);
			return 0;
		});

		TrackedDocumentState? found = null;

		Assert.IsTrue(fixture.Locked(() => set.TryGet(state.FilePath, out found)));
		Assert.AreSame(state, found);
		Assert.IsTrue(fixture.Locked(() => set.Contains(state.FilePath)));
		Assert.IsFalse(fixture.Locked(() => set.Contains(TestPaths.Script("missing.test"))));
	}

	[TestMethod]
	public void Set_ForAPathThatIsTrackedAlready_ReplacesTheRecord()
	{
		(TestHost fixture, TrackedDocumentSet set) = CreateSet();

		TrackedDocumentState original = NewState("a.test", content: "original");
		TrackedDocumentState replacement = NewState("a.test", content: "replacement");

		fixture.Locked(() =>
		{
			set.Set(original.FilePath, original);
			set.Set(replacement.FilePath, replacement);

			return 0;
		});

		TrackedDocumentState? found = null;

		Assert.IsTrue(fixture.Locked(() => set.TryGet(original.FilePath, out found)));
		Assert.AreSame(replacement, found);
	}

	[TestMethod]
	public void Remove_ForgetsTheRecordAndReportsWhetherItWasTracked()
	{
		(TestHost fixture, TrackedDocumentSet set) = CreateSet();
		TrackedDocumentState state = NewState("a.test");

		fixture.Locked(() =>
		{
			set.Set(state.FilePath, state);
			return 0;
		});

		Assert.IsTrue(fixture.Locked(() => set.Remove(state.FilePath)));
		Assert.IsFalse(fixture.Locked(() => set.Remove(state.FilePath)));
		Assert.IsFalse(fixture.Locked(() => set.Contains(state.FilePath)));
	}

	[TestMethod]
	public void Lookup_FollowsThePathComparison()
	{
		(TestHost insensitiveTestHost, TrackedDocumentSet insensitive) = CreateSet(LocalPathComparisonPolicy.CaseInsensitive);
		(TestHost sensitiveTestHost, TrackedDocumentSet sensitive) = CreateSet(LocalPathComparisonPolicy.CaseSensitive);

		TrackedDocumentState lower = NewState("case.test");
		string upperPath = TestPaths.Script("CASE.test");

		insensitiveTestHost.Locked(() =>
		{
			insensitive.Set(lower.FilePath, lower);
			return 0;
		});

		sensitiveTestHost.Locked(() =>
		{
			sensitive.Set(lower.FilePath, lower);
			return 0;
		});

		Assert.IsTrue(insensitiveTestHost.Locked(() => insensitive.Contains(upperPath)));
		Assert.IsFalse(sensitiveTestHost.Locked(() => sensitive.Contains(upperPath)));
	}

	[TestMethod]
	public void GetOpenSnapshots_ReturnsOnlyOpenRecordsInOrdinalPathOrder()
	{
		(TestHost fixture, TrackedDocumentSet set) = CreateSet();

		TrackedDocumentState b = NewState("b.test");
		TrackedDocumentState closed = NewState("closed.test", isOpen: false);
		TrackedDocumentState a = NewState("a.test");

		fixture.Locked(() =>
		{
			set.Set(b.FilePath, b);
			set.Set(closed.FilePath, closed);
			set.Set(a.FilePath, a);

			return 0;
		});

		IReadOnlyList<DocumentSnapshot> snapshots = fixture.Locked(set.GetOpenSnapshots);

		Assert.AreSequenceEqual([a.FilePath, b.FilePath], [.. snapshots.Select(static snapshot => snapshot.FilePath)]);
	}

	[TestMethod]
	public void TrimIdle_EvictsTheOldestIdleRecordsFirstAndReturnsOnlyTheOpenOnes()
	{
		(TestHost fixture, TrackedDocumentSet set) = CreateSet();

		TrackedDocumentState oldestOpen = NewState("oldest.test", accessStamp: 1);
		TrackedDocumentState middleClosed = NewState("middle.test", accessStamp: 2, isOpen: false);
		TrackedDocumentState newest = NewState("newest.test", accessStamp: 3);

		fixture.Locked(() =>
		{
			set.Set(oldestOpen.FilePath, oldestOpen);
			set.Set(middleClosed.FilePath, middleClosed);
			set.Set(newest.FilePath, newest);

			return 0;
		});

		IReadOnlyList<DocumentSnapshot> evicted = fixture.Locked(() => set.TrimIdle(maxCount: 1));

		Assert.AreEqual(oldestOpen.FilePath, Assert.ContainsSingle(evicted).FilePath);
		Assert.IsFalse(fixture.Locked(() => set.Contains(oldestOpen.FilePath)));
		Assert.IsFalse(fixture.Locked(() => set.Contains(middleClosed.FilePath)));
		Assert.IsTrue(fixture.Locked(() => set.Contains(newest.FilePath)));
	}

	[TestMethod]
	public void TrimIdle_NeverEvictsARecordThatHoldsALease()
	{
		(TestHost fixture, TrackedDocumentSet set) = CreateSet();

		TrackedDocumentState opened = NewState("opened.test", accessStamp: 1, openLeases: 1);
		TrackedDocumentState requested = NewState("requested.test", accessStamp: 2, requestLeases: 1);
		TrackedDocumentState idle = NewState("idle.test", accessStamp: 3);

		fixture.Locked(() =>
		{
			set.Set(opened.FilePath, opened);
			set.Set(requested.FilePath, requested);
			set.Set(idle.FilePath, idle);

			return 0;
		});

		IReadOnlyList<DocumentSnapshot> evicted = fixture.Locked(() => set.TrimIdle(maxCount: 0));

		Assert.AreEqual(idle.FilePath, Assert.ContainsSingle(evicted).FilePath);
		Assert.IsTrue(fixture.Locked(() => set.Contains(opened.FilePath)));
		Assert.IsTrue(fixture.Locked(() => set.Contains(requested.FilePath)));
	}

	[TestMethod]
	public void TrimIdle_WhenTheSetIsWithinTheLimit_EvictsNothing()
	{
		(TestHost fixture, TrackedDocumentSet set) = CreateSet();

		TrackedDocumentState first = NewState("first.test", accessStamp: 1);
		TrackedDocumentState second = NewState("second.test", accessStamp: 2);

		fixture.Locked(() =>
		{
			set.Set(first.FilePath, first);
			set.Set(second.FilePath, second);

			return 0;
		});

		Assert.IsEmpty(fixture.Locked(() => set.TrimIdle(maxCount: 2)));
		Assert.IsTrue(fixture.Locked(() => set.Contains(first.FilePath)));
		Assert.IsTrue(fixture.Locked(() => set.Contains(second.FilePath)));
	}

	[TestMethod]
	public void TrimIdle_WhenOnlyFewRecordsAreIdle_EvictsNothing()
	{
		(TestHost fixture, TrackedDocumentSet set) = CreateSet();

		TrackedDocumentState leased = NewState("leased.test", accessStamp: 1, openLeases: 1);
		TrackedDocumentState idle = NewState("idle.test", accessStamp: 2);

		fixture.Locked(() =>
		{
			set.Set(leased.FilePath, leased);
			set.Set(idle.FilePath, idle);

			return 0;
		});

		// Two records exceed the limit of one, but only one of them is idle, so the idle count is already within it.
		Assert.IsEmpty(fixture.Locked(() => set.TrimIdle(maxCount: 1)));
		Assert.IsTrue(fixture.Locked(() => set.Contains(idle.FilePath)));
	}

	[TestMethod]
	public void MarkAllClosedForRestart_ClosesEveryRecordAndReturnsOnlyThoseWithAnOpenLease()
	{
		(TestHost fixture, TrackedDocumentSet set) = CreateSet();

		TrackedDocumentState hostOpen = NewState("host.test", openLeases: 1);
		TrackedDocumentState requestOnly = NewState("request.test", requestLeases: 1);

		fixture.Locked(() =>
		{
			set.Set(hostOpen.FilePath, hostOpen);
			set.Set(requestOnly.FilePath, requestOnly);

			return 0;
		});

		IReadOnlyList<DocumentSnapshot> toReopen = fixture.Locked(set.MarkAllClosedForRestart);

		Assert.AreEqual(hostOpen.FilePath, Assert.ContainsSingle(toReopen).FilePath);
		Assert.IsFalse(hostOpen.IsOpen);
		Assert.IsFalse(requestOnly.IsOpen);
	}
}

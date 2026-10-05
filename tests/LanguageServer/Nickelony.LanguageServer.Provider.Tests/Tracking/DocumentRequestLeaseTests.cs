namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class DocumentRequestLeaseTests
{
	private static TrackedDocumentState CreateState(string fileName = "a.test")
	{
		return new TrackedDocumentState(
			new TrackedDocumentDescriptor(
				FilePath: TestPaths.Script(fileName),
				Uri: "file:///" + fileName,
				Content: "content",
				Version: 1,
				IsOpen: true,
				OpenLeaseCount: 0,
				RequestLeaseCount: 1,
				LastAccessStamp: 0
			)
		);
	}

	[TestMethod]
	public void NewLease_IsNotAcquiredHasNoPathAndCanBeBound()
	{
		var lease = new DocumentRequestLease();

		Assert.IsFalse(lease.IsAcquired);
		Assert.IsNull(lease.CurrentFilePath);
		Assert.IsTrue(lease.CanBind);
	}

	[TestMethod]
	public void Bind_AcquiresTheLeaseAndReportsThePathOfTheRecord()
	{
		var lease = new DocumentRequestLease();

		lease.Bind(CreateState("a.test"));

		Assert.IsTrue(lease.IsAcquired);
		Assert.IsFalse(lease.CanBind);
		Assert.AreEqual(TestPaths.Script("a.test"), lease.CurrentFilePath);
	}

	[TestMethod]
	public void Bind_AnAlreadyBoundLease_Throws()
	{
		var lease = new DocumentRequestLease();
		TrackedDocumentState first = CreateState("a.test");

		lease.Bind(first);

		Assert.ThrowsExactly<InvalidOperationException>(() => lease.Bind(CreateState("b.test")));
		Assert.AreEqual(TestPaths.Script("a.test"), lease.CurrentFilePath);
	}

	[TestMethod]
	public void Bind_AReleasedLease_Throws()
	{
		var lease = new DocumentRequestLease();

		lease.Bind(CreateState());

		bool claimed = lease.TryClaimRelease(out _);

		Assert.IsTrue(claimed);
		Assert.ThrowsExactly<InvalidOperationException>(() => lease.Bind(CreateState("b.test")));
	}

	[TestMethod]
	public void TryClaimRelease_OnAnUnboundLease_FailsWithoutState()
	{
		var lease = new DocumentRequestLease();

		bool claimed = lease.TryClaimRelease(out TrackedDocumentState? state);

		Assert.IsFalse(claimed);
		Assert.IsNull(state);
	}

	[TestMethod]
	public void TryClaimRelease_OnABoundLease_ReturnsTheBoundStateOnlyOnce()
	{
		var lease = new DocumentRequestLease();
		TrackedDocumentState bound = CreateState();

		lease.Bind(bound);

		bool first = lease.TryClaimRelease(out TrackedDocumentState? firstState);
		bool second = lease.TryClaimRelease(out _);

		Assert.IsTrue(first);
		Assert.AreSame(bound, firstState);
		Assert.IsFalse(second);
	}

	[TestMethod]
	public void CurrentFilePath_FollowsTheRecordAcrossAMoveBeforeAndAfterTheRelease()
	{
		var lease = new DocumentRequestLease();
		TrackedDocumentState state = CreateState("a.test");

		lease.Bind(state);
		state.RenameDocument(TestPaths.Script("b.test"), "file:///b.test");

		Assert.AreEqual(TestPaths.Script("b.test"), lease.CurrentFilePath);

		bool claimed = lease.TryClaimRelease(out _);

		state.RenameDocument(TestPaths.Script("c.test"), "file:///c.test");

		Assert.IsTrue(claimed);
		Assert.AreEqual(TestPaths.Script("c.test"), lease.CurrentFilePath);
		Assert.IsTrue(lease.IsAcquired);
	}

	[TestMethod]
	public async Task TryClaimRelease_UnderContention_IsWonByExactlyOneCaller()
	{
		const int Claimers = 32;

		var lease = new DocumentRequestLease();

		lease.Bind(CreateState());

		int winners = 0;

		using var startGate = new ManualResetEventSlim();

		var claimers = new List<Task>();

		for (int i = 0; i < Claimers; i++)
		{
			claimers.Add(
				Task.Run(
					() =>
					{
						startGate.Wait(TestContext.CancellationToken);

						if (lease.TryClaimRelease(out _))
							Interlocked.Increment(ref winners);
					},

					TestContext.CancellationToken
				)
			);
		}

		startGate.Set();

		await Task.WhenAll(claimers).ConfigureAwait(false);

		Assert.AreEqual(1, winners);
	}

	public required TestContext TestContext { get; set; }
}

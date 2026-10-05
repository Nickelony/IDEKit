namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers the commit of a synchronization: the record it creates or updates, the leases it acquires, and the directive it returns.</summary>
[TestClass]
public sealed class DocumentSynchronizationCommitterTests
{
	private static readonly string s_path = TestPaths.Script("committer.test");

	private sealed class TestHost
	{
		private readonly object _syncRoot = new();

		internal TestHost()
		{
			Set = new TrackedDocumentSet(_syncRoot, LocalPathComparisonPolicy.CaseSensitive);
			Committer = new DocumentSynchronizationCommitter(_syncRoot, Set);
		}

		internal TrackedDocumentSet Set { get; }

		internal DocumentSynchronizationCommitter Committer { get; }

		internal DocumentSynchronizationDirective? Synchronize(
			string content,
			DocumentLeaseKinds leases = DocumentLeaseKinds.None,
			bool includeChangeRange = true,
			DocumentRequestLease? requestLease = null
		) => Committer.Synchronize(normalizedFilePath: s_path, content, leases, includeChangeRange, requestLease);

		internal TrackedDocumentState? Find()
		{
			lock (_syncRoot)
				return Set.TryGet(s_path, out TrackedDocumentState? state) ? state : null;
		}

		internal long NextAccessStamp()
		{
			lock (_syncRoot)
				return Set.NextAccessStamp();
		}
	}

	[TestMethod]
	public void Synchronize_ForAnUntrackedPath_CreatesAnOpenRecordAtVersionOneWithTheRequestedLeases()
	{
		var fixture = new TestHost();

		DocumentSynchronizationDirective? directive = fixture.Synchronize("local a = 1", DocumentLeaseKinds.Open);

		Assert.IsNotNull(directive);
		Assert.AreEqual(DocumentSynchronizationKind.Open, directive.Value.Kind);
		Assert.AreEqual(1, directive.Value.Document.Version);
		Assert.AreEqual("local a = 1", directive.Value.Document.Content);
		Assert.AreEqual(s_path, directive.Value.Document.FilePath);
		Assert.IsNull(directive.Value.ChangeRange);

		TrackedDocumentState state = fixture.Find() ?? throw new AssertFailedException("The record should be tracked.");

		Assert.IsTrue(state.IsOpen);
		Assert.AreEqual(1, state.Leases.OpenLeaseCount);
		Assert.AreEqual(0, state.Leases.RequestLeaseCount);
	}

	[TestMethod]
	public void Synchronize_WithARequestLease_BindsTheLeaseToTheRecordAndCountsIt()
	{
		var fixture = new TestHost();
		var lease = new DocumentRequestLease();

		fixture.Synchronize("local a = 1", DocumentLeaseKinds.Request, requestLease: lease);

		TrackedDocumentState state = fixture.Find() ?? throw new AssertFailedException("The record should be tracked.");

		Assert.IsTrue(lease.IsAcquired);
		Assert.AreEqual(s_path, lease.CurrentFilePath);
		Assert.AreEqual(1, state.Leases.RequestLeaseCount);
		Assert.AreEqual(0, state.Leases.OpenLeaseCount);
	}

	[TestMethod]
	public void Synchronize_ForATrackedDocumentWithUnchangedContent_ReturnsNothingButKeepsTheLeasesAndRefreshesTheStamp()
	{
		var fixture = new TestHost();

		fixture.Synchronize("local a = 1", DocumentLeaseKinds.Open);

		TrackedDocumentState state = fixture.Find() ?? throw new AssertFailedException("The record should be tracked.");
		long stampBefore = state.LastAccessStamp;

		DocumentSynchronizationDirective? directive = fixture.Synchronize("local a = 1", DocumentLeaseKinds.Open);

		Assert.IsNull(directive);
		Assert.AreEqual(2, state.Leases.OpenLeaseCount);
		Assert.AreEqual(1, state.Version);
		Assert.IsGreaterThan(stampBefore, state.LastAccessStamp);
	}

	[TestMethod]
	public void Synchronize_ForARecordThatIsNotOpenOnTheServer_ReopensItWithANewVersion()
	{
		var fixture = new TestHost();

		fixture.Synchronize("local a = 1", DocumentLeaseKinds.Open);

		TrackedDocumentState state = fixture.Find() ?? throw new AssertFailedException("The record should be tracked.");

		state.MarkDocumentClosed();

		DocumentSynchronizationDirective? directive = fixture.Synchronize("local a = 1");

		Assert.IsNotNull(directive);
		Assert.AreEqual(DocumentSynchronizationKind.Open, directive.Value.Kind);
		Assert.AreEqual(2, directive.Value.Document.Version);
		Assert.IsTrue(state.IsOpen);
	}

	[TestMethod]
	public void Synchronize_WithChangedContent_ReturnsTheChangeWithItsRangeAndTheCommittedVersion()
	{
		var fixture = new TestHost();

		fixture.Synchronize("local a = 1", DocumentLeaseKinds.Open);

		DocumentSynchronizationDirective? directive = fixture.Synchronize("local b = 1");

		Assert.IsNotNull(directive);
		Assert.AreEqual(DocumentSynchronizationKind.Change, directive.Value.Kind);
		Assert.AreEqual(2, directive.Value.Document.Version);
		Assert.AreEqual("local b = 1", directive.Value.Document.Content);
		Assert.IsNotNull(directive.Value.ChangeRange);
		Assert.AreEqual("b", directive.Value.ChangeRange.Value.Text);
		Assert.AreEqual("local b = 1", fixture.Find()?.Content);
	}

	[TestMethod]
	public void Synchronize_WithChangedContentAndNoChangeRangeRequested_ReturnsTheChangeWithoutARange()
	{
		var fixture = new TestHost();

		fixture.Synchronize("local a = 1", DocumentLeaseKinds.Open);

		DocumentSynchronizationDirective? directive = fixture.Synchronize("local b = 1", includeChangeRange: false);

		Assert.IsNotNull(directive);
		Assert.AreEqual(DocumentSynchronizationKind.Change, directive.Value.Kind);
		Assert.IsNull(directive.Value.ChangeRange);
		Assert.AreEqual(2, directive.Value.Document.Version);
	}

	[TestMethod]
	public void Synchronize_WithARequestLeaseButNoRequestFlag_ThrowsAndTracksNothing()
	{
		var fixture = new TestHost();

		Assert.ThrowsExactly<ArgumentException>(() =>
			fixture.Synchronize("local a = 1", DocumentLeaseKinds.Open, requestLease: new DocumentRequestLease())
		);

		Assert.IsNull(fixture.Find());
	}

	[TestMethod]
	public void Synchronize_WithTheRequestFlagButNoLease_ThrowsAndTracksNothing()
	{
		var fixture = new TestHost();

		Assert.ThrowsExactly<ArgumentException>(() => fixture.Synchronize("local a = 1", DocumentLeaseKinds.Request));
		Assert.IsNull(fixture.Find());
	}

	[TestMethod]
	public void Synchronize_WithALeaseThatWasBoundAlready_ThrowsAndLeavesTheRecordUntouched()
	{
		var fixture = new TestHost();
		var lease = new DocumentRequestLease();

		fixture.Synchronize("local a = 1", DocumentLeaseKinds.Request, requestLease: lease);

		TrackedDocumentState state = fixture.Find() ?? throw new AssertFailedException("The record should be tracked.");

		Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Synchronize("local b = 1", DocumentLeaseKinds.Request, requestLease: lease));

		Assert.AreEqual(1, state.Leases.RequestLeaseCount);
		Assert.AreEqual("local a = 1", state.Content);
	}

	[TestMethod]
	public void Synchronize_TakesAFreshAccessStampForEveryCall()
	{
		var fixture = new TestHost();

		fixture.Synchronize("local a = 1", DocumentLeaseKinds.Open);

		long created = (fixture.Find() ?? throw new AssertFailedException("The record should be tracked.")).LastAccessStamp;
		long unrelated = fixture.NextAccessStamp();

		fixture.Synchronize("local a = 1");

		long refreshed = (fixture.Find() ?? throw new AssertFailedException("The record should be tracked.")).LastAccessStamp;

		Assert.IsGreaterThan(created, unrelated);
		Assert.IsGreaterThan(unrelated, refreshed);
	}
}

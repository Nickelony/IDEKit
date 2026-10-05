using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Tests;

/// <summary>Covers the store's operation gate directly: the preamble checks, the stop check and the release of what an operation held.</summary>
[TestClass]
public sealed class OperationGateTests
{
	private sealed class TestHost : StoreTestHost
	{
		internal static WorkspaceDocumentRequestIdentity IdentityOf(LogicalDocument document, long? version = null) =>
			new(document.DocumentKey, document.DocumentId, version ?? document.Version);

		internal LogicalDocument AddDocument() => AddDocument("doc");

		internal OperationLease BeginOperation(LogicalDocument document)
		{
			return OperationGate.Begin(IdentityOf(document), captureSnapshot: false).Operation
				?? throw new InvalidOperationException("The operation gate did not hold an operation lease.");
		}
	}

	public required TestContext TestContext { get; set; }

	[TestMethod]
	public void Begin_ForATrackedDocument_AdmitsTheOperationAndHoldsTheDiskGate()
	{
		var fixture = new TestHost();
		LogicalDocument document = fixture.AddDocument();

		OperationBeginResult begin = fixture.OperationGate.Begin(TestHost.IdentityOf(document), captureSnapshot: false);

		Assert.IsTrue(begin.Succeeded);
		Assert.AreSame(document, begin.Document);
		Assert.IsNull(begin.CapturedSnapshot);
		Assert.AreEqual(0, document.DiskOperationGate.CurrentCount);
		Assert.AreSame(begin.Operation, Assert.ContainsSingle(fixture.Stop()));
	}

	[TestMethod]
	public void Begin_WithCaptureSnapshot_CapturesTheSnapshotOfTheDocument()
	{
		var fixture = new TestHost();
		LogicalDocument document = fixture.AddDocument();

		OperationBeginResult begin = fixture.OperationGate.Begin(TestHost.IdentityOf(document), captureSnapshot: true);

		Assert.IsTrue(begin.Succeeded);
		Assert.AreEqual(document.DocumentKey, begin.CapturedSnapshot?.DocumentKey);
		Assert.AreEqual(document.DocumentId, begin.CapturedSnapshot?.DocumentId);
	}

	[TestMethod]
	public void Begin_ForAnUnknownDocument_ReportsDocumentNotFoundAndTakesNothing()
	{
		var fixture = new TestHost();
		LogicalDocument document = fixture.AddDocument();
		var unknown = new WorkspaceDocumentRequestIdentity(document.DocumentKey, "unknown", 0);

		OperationBeginResult begin = fixture.OperationGate.Begin(unknown, captureSnapshot: true);

		Assert.AreEqual(OperationBeginOutcome.DocumentNotFound, begin.Outcome);
		Assert.IsNull(begin.Document);
		Assert.IsNull(begin.FailureSnapshot);
		Assert.AreEqual(1, document.DiskOperationGate.CurrentCount);
		Assert.IsEmpty(fixture.Stop());
	}

	[TestMethod]
	public void Begin_ForAnotherInstanceAtTheId_ReportsStaleDocumentInstanceWithTheFailureSnapshot()
	{
		var fixture = new TestHost();
		LogicalDocument document = fixture.AddDocument();
		var other = new WorkspaceDocumentRequestIdentity(new WorkspaceDocumentKey(Guid.NewGuid()), document.DocumentId, document.Version);

		OperationBeginResult begin = fixture.OperationGate.Begin(other, captureSnapshot: false);

		Assert.AreEqual(OperationBeginOutcome.StaleDocumentInstance, begin.Outcome);
		Assert.AreEqual(document.DocumentKey, begin.FailureSnapshot?.DocumentKey);
		Assert.AreEqual(1, document.DiskOperationGate.CurrentCount);
	}

	[TestMethod]
	public void Begin_ForAnOutdatedVersion_ReportsStaleDocumentWithTheCurrentSnapshot()
	{
		var fixture = new TestHost();
		LogicalDocument document = fixture.AddDocument();

		for (int index = 0; index < 3; index++)
			fixture.Edit(document, $"content {index}");

		OperationBeginResult begin = fixture.OperationGate.Begin(TestHost.IdentityOf(document, version: 1), captureSnapshot: false);

		Assert.AreEqual(OperationBeginOutcome.StaleDocument, begin.Outcome);
		Assert.AreEqual(3, begin.FailureSnapshot?.Version);
		Assert.AreEqual(1, document.DiskOperationGate.CurrentCount);
	}

	[TestMethod]
	public void Begin_WhenThePreGateCheckRejects_CarriesItsOutcomeAndTakesNoGate()
	{
		var fixture = new TestHost();
		LogicalDocument document = fixture.AddDocument();

		OperationBeginResult begin = fixture.OperationGate.Begin(
			TestHost.IdentityOf(document),
			captureSnapshot: false,
			preGateCheck: _ => OperationPreGateOutcome.DestinationBusy
		);

		Assert.IsFalse(begin.Succeeded);
		Assert.AreEqual(OperationPreGateOutcome.DestinationBusy, begin.PreGateOutcome);
		Assert.IsNull(begin.Outcome);
		Assert.IsNotNull(begin.FailureSnapshot);
		Assert.AreEqual(1, document.DiskOperationGate.CurrentCount);
	}

	[TestMethod]
	public void Begin_WhileADeleteIsInFlight_ReportsOperationInProgressUntilTheDeleteCompletes()
	{
		var fixture = new TestHost();
		LogicalDocument document = fixture.AddDocument();

		// The delete reserves the document inside its post-gate setup, so the reservation exists only while its operation holds the gate.
		OperationBeginResult delete = fixture.OperationGate.Begin(
			TestHost.IdentityOf(document),
			captureSnapshot: false,
			postGateSetup: setupDocument => fixture.Tracker.ReserveDelete(setupDocument)
		);

		Assert.IsTrue(delete.Succeeded);
		Assert.IsTrue(fixture.Locked(tracker => tracker.IsDeleteActive(document)));

		OperationBeginResult refused = fixture.OperationGate.Begin(TestHost.IdentityOf(document), captureSnapshot: true);

		Assert.AreEqual(OperationBeginOutcome.OperationInProgress, refused.Outcome);
		Assert.IsNotNull(refused.FailureSnapshot);

		fixture.OperationGate.CompleteDelete(document);
		fixture.OperationGate.Complete(delete.Operation!);

		Assert.IsTrue(fixture.OperationGate.Begin(TestHost.IdentityOf(document), captureSnapshot: false).Succeeded);
	}

	[TestMethod]
	public void Begin_WhenTheDiskGateIsHeld_ReportsOperationInProgress()
	{
		var fixture = new TestHost();
		LogicalDocument document = fixture.AddDocument();

		Assert.IsTrue(document.DiskOperationGate.Wait(0));

		OperationBeginResult begin = fixture.OperationGate.Begin(TestHost.IdentityOf(document), captureSnapshot: false);

		Assert.AreEqual(OperationBeginOutcome.OperationInProgress, begin.Outcome);
		Assert.IsNotNull(begin.FailureSnapshot);
		Assert.IsEmpty(fixture.Stop());
	}

	[TestMethod]
	public void Begin_RunsThePostGateSetupAfterTakingTheGate()
	{
		var fixture = new TestHost();

		LogicalDocument document = fixture.AddDocument();
		int? gateCountDuringSetup = null;

		OperationBeginResult begin = fixture.OperationGate.Begin(
			TestHost.IdentityOf(document),
			captureSnapshot: false,
			postGateSetup: setupDocument => gateCountDuringSetup = setupDocument.DiskOperationGate.CurrentCount
		);

		Assert.IsTrue(begin.Succeeded);
		Assert.AreEqual(0, gateCountDuringSetup);
	}

	[TestMethod]
	public void Begin_WhenThePostGateSetupThrows_ReleasesTheGateAndTheDeleteReservationAndRethrows()
	{
		var fixture = new TestHost();

		LogicalDocument document = fixture.AddDocument();
		DeleteReservation? reservation = null;

		InvalidOperationException exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
			fixture.OperationGate.Begin(
				TestHost.IdentityOf(document),
				captureSnapshot: false,

				postGateSetup: setupDocument =>
				{
					lock (fixture.SyncRoot)
						reservation = fixture.Tracker.ReserveDelete(setupDocument);

					throw new InvalidOperationException("The setup failed.");
				}
			)
		);

		Assert.AreEqual("The setup failed.", exception.Message);
		Assert.AreEqual(1, document.DiskOperationGate.CurrentCount);
		Assert.IsFalse(fixture.Locked(tracker => tracker.IsDeleteActive(document)));
		Assert.IsTrue(reservation?.Completion.Task.IsCompletedSuccessfully);
		Assert.IsEmpty(fixture.Stop());
	}

	[TestMethod]
	public void Begin_AfterAStopBegan_ThrowsObjectDisposedAndTakesNothing()
	{
		var fixture = new TestHost();
		LogicalDocument document = fixture.AddDocument();

		fixture.Stop();

		Assert.ThrowsExactly<ObjectDisposedException>(() => fixture.OperationGate.Begin(TestHost.IdentityOf(document), captureSnapshot: false));
		Assert.AreEqual(1, document.DiskOperationGate.CurrentCount);
	}

	[TestMethod]
	[DataRow("")]
	[DataRow("  ")]
	public void Begin_WithABlankDocumentId_Throws(string documentId)
	{
		var fixture = new TestHost();
		var identity = new WorkspaceDocumentRequestIdentity(new WorkspaceDocumentKey(Guid.NewGuid()), documentId, 0);

		Assert.ThrowsExactly<ArgumentException>(() => fixture.OperationGate.Begin(identity, captureSnapshot: false));
	}

	[TestMethod]
	[Timeout(15000, CooperativeCancellation = true)]
	public async Task Begin_WhenAStopBeginsWhileItWaitsForTheLock_ThrowsInsteadOfAdmitting()
	{
		var fixture = new TestHost();

		LogicalDocument document = fixture.AddDocument();

		using var lockHeld = new ManualResetEventSlim();
		using var releaseLock = new ManualResetEventSlim();

		// Another thread holds the store lock and begins the stop inside it, the way disposal does.
		var holder = Task.Run(
			() =>
			{
				lock (fixture.SyncRoot)
				{
					lockHeld.Set();
					releaseLock.Wait(TestContext.CancellationToken);

					fixture.Gate.TryBeginStop(out _, out _);
					fixture.Gate.RecordStopTask(Task.CompletedTask);
				}
			},

			TestContext.CancellationToken
		);

		lockHeld.Wait(TestContext.CancellationToken);

		Task<Exception?> attempt = Task.Run(
			() =>
			{
				try
				{
					fixture.OperationGate.Begin(TestHost.IdentityOf(document), captureSnapshot: false);
					return null;
				}
				catch (Exception exception)
				{
					return exception;
				}
			},

			TestContext.CancellationToken
		);

		// The attempt must wait for the lock instead of admitting the operation behind the stop check.
		Task finishedFirst = await Task.WhenAny(attempt, Task.Delay(250, TestContext.CancellationToken)).ConfigureAwait(false);

		Assert.AreNotSame(attempt, finishedFirst);

		releaseLock.Set();

		await holder.WaitAsync(TestContext.CancellationToken).ConfigureAwait(false);

		Assert.IsInstanceOfType<ObjectDisposedException>(await attempt.WaitAsync(TestContext.CancellationToken).ConfigureAwait(false));
		Assert.AreEqual(1, document.DiskOperationGate.CurrentCount);
	}

	[TestMethod]
	public void Begin_MarksTheCallingFlowSoAStopRequestedFromInsideTheOperationThrows()
	{
		var fixture = new TestHost();
		LogicalDocument document = fixture.AddDocument();

		fixture.OperationGate.Begin(TestHost.IdentityOf(document), captureSnapshot: false);

		lock (fixture.SyncRoot)
			Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Gate.TryBeginStop(out _, out _));
	}

	[TestMethod]
	[Timeout(15000, CooperativeCancellation = true)]
	public void Begin_WhenAStopWaitsForTheLockDuringThePostGateSetup_RegistersTheOperationBeforeTheStopBegins()
	{
		var fixture = new TestHost();

		LogicalDocument document = fixture.AddDocument();
		StoreTestHost.PendingStop? stop = null;
		bool stopWaitedForTheLock = false;

		OperationBeginResult begin = fixture.OperationGate.Begin(
			TestHost.IdentityOf(document),
			captureSnapshot: false,

			postGateSetup: _ =>
			{
				stop = fixture.BeginStop();
				stopWaitedForTheLock = SpinWait.SpinUntil(() => stop.IsBlocked, TimeSpan.FromSeconds(5));
			}
		);

		Assert.IsTrue(stopWaitedForTheLock);
		Assert.AreSame(begin.Operation, Assert.ContainsSingle(stop!.Join()));
	}

	[TestMethod]
	public void Admit_RegistersAnOperationTheStopWaitsFor()
	{
		var fixture = new TestHost();
		OperationLease operation;

		lock (fixture.SyncRoot)
			operation = fixture.OperationGate.Admit([]);

		Assert.AreSame(operation, Assert.ContainsSingle(fixture.Stop()));
	}

	[TestMethod]
	public void Admit_AfterAStopBegan_Throws()
	{
		var fixture = new TestHost();

		fixture.Stop();

		lock (fixture.SyncRoot)
			Assert.ThrowsExactly<ObjectDisposedException>(() => fixture.OperationGate.Admit([]));
	}

	[TestMethod]
	public void Complete_ReleasesTheDiskGateTheRegistrationAndTheWaiterOnce()
	{
		var fixture = new TestHost();
		LogicalDocument document = fixture.AddDocument();

		OperationLease operation = fixture.BeginOperation(document);

		fixture.OperationGate.Complete(operation);

		Assert.AreEqual(1, document.DiskOperationGate.CurrentCount);
		Assert.IsTrue(operation.Completion.Task.IsCompletedSuccessfully);
		Assert.IsEmpty(fixture.Stop());

		fixture.OperationGate.Complete(operation);

		Assert.AreEqual(1, document.DiskOperationGate.CurrentCount);
	}

	[TestMethod]
	[Timeout(15000, CooperativeCancellation = true)]
	public async Task Complete_ResumesTheWaitersOutsideTheLock()
	{
		var fixture = new TestHost();

		LogicalDocument document = fixture.AddDocument();

		OperationLease operation = fixture.BeginOperation(document);
		Task<bool> resumedUnderLock = TestContinuations.RunsUnderLock(operation.Completion.Task, fixture.SyncRoot);

		fixture.OperationGate.Complete(operation);

		Assert.IsFalse(await resumedUnderLock.WaitAsync(TestContext.CancellationToken).ConfigureAwait(false));
	}

	[TestMethod]
	public void CompleteDestinationReservation_ReleasesTheReservationAndItsWaiters()
	{
		var fixture = new TestHost();
		DestinationReservation reservation = fixture.Locked(tracker => tracker.ReserveDestination("destination"));

		fixture.OperationGate.CompleteDestinationReservation(reservation);

		Assert.IsFalse(fixture.Locked(tracker => tracker.IsDestinationUnavailable("destination")));
		Assert.IsTrue(reservation.Completion.Task.IsCompletedSuccessfully);
	}

	[TestMethod]
	public void CompleteDirectoryOperation_ReleasesTheReservationAndItsWaiters()
	{
		var fixture = new TestHost();
		var reservation = new DirectoryOperationReservation("directory");

		fixture.Locked(tracker => tracker.BeginDirectoryOperation(reservation, []));
		fixture.OperationGate.CompleteDirectoryOperation(reservation);

		Assert.IsNull(fixture.Locked(tracker => tracker.FindDirectoryOperationUnder("directory")));
		Assert.IsTrue(reservation.Completion.Task.IsCompletedSuccessfully);
	}

	[TestMethod]
	public void CompleteDelete_ReleasesTheDeleteReservationAndItsWaiters()
	{
		var fixture = new TestHost();

		LogicalDocument document = fixture.AddDocument();
		DeleteReservation reservation = fixture.Locked(tracker => tracker.ReserveDelete(document));

		fixture.OperationGate.CompleteDelete(document);

		Assert.IsFalse(fixture.Locked(tracker => tracker.IsDeleteActive(document)));
		Assert.IsTrue(reservation.Completion.Task.IsCompletedSuccessfully);
	}
}

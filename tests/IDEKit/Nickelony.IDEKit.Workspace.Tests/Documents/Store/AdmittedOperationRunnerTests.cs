using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Tests;

/// <summary>
/// Covers the runner of admitted operations directly: the refusal, the completion on every exit, the release order and the exception hooks.
/// </summary>
[TestClass]
public sealed class AdmittedOperationRunnerTests
{
	public required TestContext TestContext { get; set; }

	private static AdmittedOperationDescriptor<string> PlanFor(
		LogicalDocument document,
		Func<AdmittedOperation, CancellationToken, Task<string>>? execute = null,
		AdmittedOperationCancellationMode cancellation = AdmittedOperationCancellationMode.Linked
	)
	{
		return new AdmittedOperationDescriptor<string>
		{
			Identity = StoreCollaboratorsTestHost.IdentityOf(document),
			Cancellation = cancellation,
			CreateRefusal = begin => $"refused:{begin.Outcome}",
			ExecuteAsync = execute ?? ((_, _) => Task.FromResult("ran")),
		};
	}

	[TestMethod]
	public async Task RunAsync_ForAnAdmittedDocument_RunsTheStepAndReleasesTheGateAndTheRegistration()
	{
		var host = new StoreCollaboratorsTestHost();

		LogicalDocument document = host.AddDocument("doc");
		int gateCountDuringStep = -1;

		string result = await host
			.Runner.RunAsync(
				PlanFor(
					document,

					(operation, _) =>
					{
						gateCountDuringStep = operation.Document.DiskOperationGate.CurrentCount;
						return Task.FromResult("ran");
					}
				),

				TestContext.CancellationToken
			)
			.ConfigureAwait(false);

		Assert.AreEqual("ran", result);
		Assert.AreEqual(0, gateCountDuringStep);
		Assert.AreEqual(1, document.DiskOperationGate.CurrentCount);
		Assert.IsEmpty(host.Stop());
	}

	[TestMethod]
	public async Task RunAsync_ForAStaleVersion_ReturnsTheRefusalAndNeverRunsTheStep()
	{
		var host = new StoreCollaboratorsTestHost();

		LogicalDocument document = host.AddDocument("doc");
		bool ran = false;

		var identity = new WorkspaceDocumentRequestIdentity(document.DocumentKey, document.DocumentId, document.Version + 1);

		var plan = new AdmittedOperationDescriptor<string>
		{
			Identity = identity,
			CreateRefusal = begin => $"refused:{begin.Outcome}",

			ExecuteAsync = (_, _) =>
			{
				ran = true;
				return Task.FromResult("ran");
			},
		};

		string result = await host.Runner.RunAsync(plan, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.AreEqual("refused:StaleDocument", result);
		Assert.IsFalse(ran);
		Assert.AreEqual(1, document.DiskOperationGate.CurrentCount);
	}

	[TestMethod]
	public async Task RunAsync_ReleasesThePlanReservationBeforeTheRegistrationCompletes()
	{
		var host = new StoreCollaboratorsTestHost();

		LogicalDocument document = host.AddDocument("doc");
		int gateCountAtRelease = -1;

		var plan = new AdmittedOperationDescriptor<string>
		{
			Identity = StoreCollaboratorsTestHost.IdentityOf(document),
			CreateRefusal = _ => "refused",
			ExecuteAsync = (_, _) => Task.FromResult("ran"),
			Release = released => gateCountAtRelease = released.DiskOperationGate.CurrentCount,
		};

		await host.Runner.RunAsync(plan, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.AreEqual(0, gateCountAtRelease);
		Assert.AreEqual(1, document.DiskOperationGate.CurrentCount);
	}

	[TestMethod]
	public async Task RunAsync_WhenTheStepCancels_ReturnsTheCanceledResultAndStillCompletes()
	{
		var host = new StoreCollaboratorsTestHost();
		LogicalDocument document = host.AddDocument("doc");

		var plan = new AdmittedOperationDescriptor<string>
		{
			Identity = StoreCollaboratorsTestHost.IdentityOf(document),
			CreateRefusal = _ => "refused",
			ExecuteAsync = (_, _) => throw new OperationCanceledException(),
			CreateCanceled = canceled => $"canceled:{canceled.DocumentId}",
		};

		string result = await host.Runner.RunAsync(plan, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.AreEqual("canceled:doc", result);
		Assert.AreEqual(1, document.DiskOperationGate.CurrentCount);
		Assert.IsEmpty(host.Stop());
	}

	[TestMethod]
	public async Task RunAsync_WhenTheStepFaults_ReturnsTheFaultedResultWithTheException()
	{
		var host = new StoreCollaboratorsTestHost();
		LogicalDocument document = host.AddDocument("doc");

		var plan = new AdmittedOperationDescriptor<string>
		{
			Identity = StoreCollaboratorsTestHost.IdentityOf(document),
			CreateRefusal = _ => "refused",
			ExecuteAsync = (_, _) => throw new InvalidOperationException("boom"),
			CreateFaulted = (_, exception) => $"faulted:{exception.Message}",
		};

		string result = await host.Runner.RunAsync(plan, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.AreEqual("faulted:boom", result);
		Assert.AreEqual(1, document.DiskOperationGate.CurrentCount);
	}

	[TestMethod]
	public async Task RunAsync_WithoutAFaultHook_PropagatesTheExceptionAfterReleasingTheGate()
	{
		var host = new StoreCollaboratorsTestHost();
		LogicalDocument document = host.AddDocument("doc");

		AdmittedOperationDescriptor<string> plan = PlanFor(document, (_, _) => throw new InvalidOperationException("boom"));

		await Assert
			.ThrowsExactlyAsync<InvalidOperationException>(() => host.Runner.RunAsync(plan, TestContext.CancellationToken))
			.ConfigureAwait(false);

		Assert.AreEqual(1, document.DiskOperationGate.CurrentCount);
		Assert.IsEmpty(host.Stop());
	}

	[TestMethod]
	public async Task RunAsync_WithLinkedCancellation_EndsACanceledCallerTokenBeforeTheStepStarts()
	{
		var host = new StoreCollaboratorsTestHost();

		LogicalDocument document = host.AddDocument("doc");
		bool ran = false;

		using var cancellation = new CancellationTokenSource();
		await cancellation.CancelAsync().ConfigureAwait(false);

		var plan = new AdmittedOperationDescriptor<string>
		{
			Identity = StoreCollaboratorsTestHost.IdentityOf(document),
			CreateRefusal = _ => "refused",

			ExecuteAsync = (_, _) =>
			{
				ran = true;
				return Task.FromResult("ran");
			},

			CreateCanceled = _ => "canceled",
		};

		string result = await host.Runner.RunAsync(plan, cancellation.Token).ConfigureAwait(false);

		Assert.AreEqual("canceled", result);
		Assert.IsFalse(ran);
	}

	[TestMethod]
	public async Task RunAsync_WithLinkedCancellation_CancelsTheStepTokenWhenTheLifetimeIsCanceled()
	{
		var host = new StoreCollaboratorsTestHost();

		LogicalDocument document = host.AddDocument("doc");
		bool canceledDuringStep = false;

		AdmittedOperationDescriptor<string> plan = PlanFor(
			document,

			(_, token) =>
			{
				host.Lifetime.Cancel();
				canceledDuringStep = token.IsCancellationRequested;
				return Task.FromResult("ran");
			}
		);

		await host.Runner.RunAsync(plan, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.IsTrue(canceledDuringStep);
	}

	[TestMethod]
	public async Task RunAsync_WithCallerCancellation_HandsTheCallersTokenToTheStep()
	{
		var host = new StoreCollaboratorsTestHost();

		LogicalDocument document = host.AddDocument("doc");
		CancellationToken seen = default;

		AdmittedOperationDescriptor<string> plan = PlanFor(
			document,

			(_, token) =>
			{
				seen = token;
				return Task.FromResult("ran");
			},

			AdmittedOperationCancellationMode.Caller
		);

		await host.Runner.RunAsync(plan, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.AreEqual(TestContext.CancellationToken, seen);
	}

	[TestMethod]
	public async Task RunAsync_AfterTheStopBegan_ThrowsObjectDisposed()
	{
		var host = new StoreCollaboratorsTestHost();
		LogicalDocument document = host.AddDocument("doc");
		host.Stop();

		await Assert
			.ThrowsExactlyAsync<ObjectDisposedException>(() => host.Runner.RunAsync(PlanFor(document), TestContext.CancellationToken))
			.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task RunAsync_WithOnlyAFaultHook_LetsACancellationPropagateInsteadOfReportingAFault()
	{
		var host = new StoreCollaboratorsTestHost();
		LogicalDocument document = host.AddDocument("doc");

		var plan = new AdmittedOperationDescriptor<string>
		{
			Identity = StoreCollaboratorsTestHost.IdentityOf(document),
			CreateRefusal = _ => "refused",
			ExecuteAsync = (_, _) => throw new OperationCanceledException(),
			CreateFaulted = (_, _) => "faulted",
		};

		await Assert
			.ThrowsExactlyAsync<OperationCanceledException>(() => host.Runner.RunAsync(plan, TestContext.CancellationToken))
			.ConfigureAwait(false);

		Assert.AreEqual(1, document.DiskOperationGate.CurrentCount);
	}

	[TestMethod]
	public async Task RunAsync_WhenTheReleaseThrows_StillCompletesTheAdmission()
	{
		var host = new StoreCollaboratorsTestHost();
		LogicalDocument document = host.AddDocument("doc");

		var plan = new AdmittedOperationDescriptor<string>
		{
			Identity = StoreCollaboratorsTestHost.IdentityOf(document),
			CreateRefusal = _ => "refused",
			ExecuteAsync = (_, _) => Task.FromResult("ran"),
			Release = _ => throw new InvalidOperationException("release failed"),
		};

		await Assert
			.ThrowsExactlyAsync<InvalidOperationException>(() => host.Runner.RunAsync(plan, TestContext.CancellationToken))
			.ConfigureAwait(false);

		Assert.AreEqual(1, document.DiskOperationGate.CurrentCount);
		Assert.IsEmpty(host.Stop());
	}
}

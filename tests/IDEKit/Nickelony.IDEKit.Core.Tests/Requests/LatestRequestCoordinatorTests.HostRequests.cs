namespace Nickelony.IDEKit.Core.Tests;

public sealed partial class LatestRequestCoordinatorTests
{
	[TestMethod]
	public void BeginRequest_MintsACurrentRequestWithAnUncanceledToken()
	{
		var coordinator = new LatestRequestCoordinator();

		RequestHandle request = coordinator.BeginRequest();

		Assert.AreNotEqual(RequestHandle.None, request);
		Assert.IsTrue(coordinator.IsCurrent(request));
		Assert.IsFalse(request.CancellationToken.IsCancellationRequested);
	}

	[TestMethod]
	public void IsCurrent_NoneHandleAndNeverIssuedIdentifier_AreNeverCurrent()
	{
		var coordinator = new LatestRequestCoordinator();

		// Neither the handle of no request nor an identifier the coordinator never minted can pass the
		// check, even after a request was admitted.
		coordinator.BeginRequest();

		Assert.IsFalse(coordinator.IsCurrent(RequestHandle.None));
		Assert.IsFalse(coordinator.IsCurrent(new RequestHandle(12345, CancellationToken.None)));
	}

	[TestMethod]
	public void CanPublish_ReportsTheLatestUncanceledRequest()
	{
		var coordinator = new LatestRequestCoordinator();

		Assert.IsFalse(coordinator.CanPublish(RequestHandle.None));
		Assert.IsFalse(coordinator.CanPublish(new RequestHandle(12345, CancellationToken.None)));

		RequestHandle request = coordinator.BeginRequest();

		Assert.IsTrue(coordinator.CanPublish(request));

		coordinator.CancelPendingRequest();

		Assert.IsFalse(coordinator.CanPublish(request), "A canceled request must not be publishable.");

		RequestHandle newer = coordinator.BeginRequest();

		Assert.IsFalse(coordinator.CanPublish(request));
		Assert.IsTrue(coordinator.CanPublish(newer));
	}

	[TestMethod]
	public void CanPublish_SupersededRequest_ReportsFalseEvenWhenTheNewerTokenIsLive()
	{
		var coordinator = new LatestRequestCoordinator();

		RequestHandle first = coordinator.BeginRequest();
		RequestHandle second = coordinator.BeginRequest();

		Assert.IsTrue(first.CancellationToken.IsCancellationRequested, "Superseding cancels the superseded request's own token.");
		Assert.IsFalse(second.CancellationToken.IsCancellationRequested);
		Assert.IsFalse(coordinator.CanPublish(first), "The check must evaluate the token of the inspected request, not the latest one.");
		Assert.IsTrue(coordinator.CanPublish(second));
	}

	[TestMethod]
	public void BeginRequest_AdmissionPredicateRejects_ReturnsNoneAndLeavesStateUntouched()
	{
		var coordinator = new LatestRequestCoordinator();

		RequestHandle first = coordinator.BeginRequest();

		RequestHandle rejected = coordinator.BeginRequestIf(static () => false);

		Assert.AreEqual(RequestHandle.None, rejected);
		Assert.IsTrue(coordinator.IsCurrent(first));
		Assert.IsFalse(first.CancellationToken.IsCancellationRequested, "A rejected admission must not supersede the outstanding request.");
		Assert.IsTrue(coordinator.CanPublish(first), "A rejected admission must leave the outstanding request publishable.");
	}

	[TestMethod]
	public void BeginRequest_AdmissionPredicateAccepts_AdmitsAndSupersedes()
	{
		var coordinator = new LatestRequestCoordinator();

		RequestHandle first = coordinator.BeginRequest();

		RequestHandle second = coordinator.BeginRequestIf(static () => true);

		Assert.AreNotEqual(RequestHandle.None, second);
		Assert.IsTrue(first.CancellationToken.IsCancellationRequested);
		Assert.IsFalse(coordinator.IsCurrent(first));
		Assert.IsTrue(coordinator.IsCurrent(second));
	}

	[TestMethod]
	public void BeginRequest_NullAdmissionPredicate_Throws()
	{
		var coordinator = new LatestRequestCoordinator();

		Assert.ThrowsExactly<ArgumentNullException>(() => coordinator.BeginRequestIf(null!));
	}

	[TestMethod]
	public void CancelPendingRequest_BySupersededHandle_LeavesTheNewerRequestUntouched()
	{
		var coordinator = new LatestRequestCoordinator();

		RequestHandle first = coordinator.BeginRequest();
		RequestHandle second = coordinator.BeginRequest();

		coordinator.CancelPendingRequest(first);

		Assert.IsFalse(second.CancellationToken.IsCancellationRequested);
		Assert.IsTrue(coordinator.CanPublish(second));

		coordinator.CancelPendingRequest(second);

		Assert.IsTrue(second.CancellationToken.IsCancellationRequested);
		Assert.IsFalse(coordinator.CanPublish(second));
	}

	[TestMethod]
	public void CancelPendingRequest_RejectingPredicate_LeavesTheRequestUncanceled()
	{
		var coordinator = new LatestRequestCoordinator();

		RequestHandle request = coordinator.BeginRequest();

		coordinator.CancelPendingRequestIf(static () => false);

		Assert.IsFalse(request.CancellationToken.IsCancellationRequested, "A rejected cancellation must leave the token untouched.");
		Assert.IsTrue(coordinator.CanPublish(request));
	}

	[TestMethod]
	public void CancelPendingRequest_AcceptingPredicate_CancelsTheRequest()
	{
		var coordinator = new LatestRequestCoordinator();

		RequestHandle request = coordinator.BeginRequest();

		coordinator.CancelPendingRequestIf(static () => true);

		Assert.IsTrue(request.CancellationToken.IsCancellationRequested);
		Assert.IsTrue(coordinator.IsCurrent(request), "Cancellation is not invalidation: the request stays current.");
		Assert.IsFalse(coordinator.CanPublish(request));
	}

	[TestMethod]
	public void CancelPendingRequest_NullPredicate_Throws()
	{
		var coordinator = new LatestRequestCoordinator();

		Assert.ThrowsExactly<ArgumentNullException>(() => coordinator.CancelPendingRequestIf(null!));
	}

	[TestMethod]
	public void Invalidate_RejectingPredicate_KeepsTheRequestCurrent()
	{
		var coordinator = new LatestRequestCoordinator();

		RequestHandle request = coordinator.BeginRequest();

		coordinator.InvalidateIf(static () => false);

		Assert.IsTrue(coordinator.IsCurrent(request));
		Assert.IsFalse(request.CancellationToken.IsCancellationRequested);
		Assert.IsTrue(coordinator.CanPublish(request));
	}

	[TestMethod]
	public void Invalidate_AcceptingPredicate_RejectsTheRequestWithoutCanceling()
	{
		var coordinator = new LatestRequestCoordinator();

		RequestHandle request = coordinator.BeginRequest();

		coordinator.InvalidateIf(static () => true);

		Assert.IsFalse(coordinator.IsCurrent(request));
		Assert.IsFalse(request.CancellationToken.IsCancellationRequested);
	}

	[TestMethod]
	public void Invalidate_NullPredicate_Throws()
	{
		var coordinator = new LatestRequestCoordinator();

		Assert.ThrowsExactly<ArgumentNullException>(() => coordinator.InvalidateIf(null!));
	}

	[TestMethod]
	public void BeginRequest_SupersedesThePreviousRequestAndCancelsItsToken()
	{
		var coordinator = new LatestRequestCoordinator();

		RequestHandle first = coordinator.BeginRequest();
		RequestHandle second = coordinator.BeginRequest();

		Assert.IsTrue(first.CancellationToken.IsCancellationRequested);
		Assert.IsFalse(coordinator.IsCurrent(first));
		Assert.IsTrue(coordinator.IsCurrent(second));
		Assert.IsFalse(second.CancellationToken.IsCancellationRequested);
	}

	[TestMethod]
	public void CancelPendingRequest_KeepsTheRequestCurrentAndReportsTheCanceledToken()
	{
		var coordinator = new LatestRequestCoordinator();

		RequestHandle request = coordinator.BeginRequest();

		coordinator.CancelPendingRequest();

		// Canceling stops the work; it does not invalidate the request, and the canceled token stays
		// observable through the request's handle.
		Assert.IsTrue(request.CancellationToken.IsCancellationRequested);
		Assert.IsTrue(coordinator.IsCurrent(request));
	}

	[TestMethod]
	public void Invalidate_RejectsTheOutstandingRequestWithoutCancelingItsToken()
	{
		var coordinator = new LatestRequestCoordinator();

		RequestHandle request = coordinator.BeginRequest();

		coordinator.Invalidate();

		Assert.IsFalse(coordinator.IsCurrent(request));
		Assert.IsFalse(request.CancellationToken.IsCancellationRequested);
	}

	[TestMethod]
	[Timeout(30_000)]
	public async Task BeginRequest_SupersedesAnOutstandingRun()
	{
		var coordinator = new LatestRequestCoordinator();
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var runStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		bool runTokenObservedCancellation = false;
		int appliedCount = 0;

		Task<RequestOutcome> run = coordinator.RunAsync<int, int>(
			0,
			async (state, token) =>
			{
				token.Register(() => runTokenObservedCancellation = true);
				runStarted.SetResult();
				await release.Task.ConfigureAwait(true);
				return state;
			},
			static (state, result) => true,
			_ => appliedCount++);

		await runStarted.Task;

		// A host-driven request supersedes the run: its token is canceled and its result is discarded.
		RequestHandle request = coordinator.BeginRequest();

		release.SetResult();

		Assert.AreEqual(RequestOutcome.Superseded, await run);
		Assert.IsTrue(runTokenObservedCancellation);
		Assert.AreEqual(0, appliedCount);
		Assert.IsTrue(coordinator.IsCurrent(request));
	}

	[TestMethod]
	public async Task RunAsync_SupersedesAnOutstandingHostDrivenRequest()
	{
		var coordinator = new LatestRequestCoordinator();

		RequestHandle request = coordinator.BeginRequest();
		int appliedCount = 0;

		RequestOutcome outcome = await coordinator.RunAsync(
			42,
			static (state, token) => Task.FromResult(state),
			static (state, result) => true,
			_ => appliedCount++);

		Assert.AreEqual(RequestOutcome.Completed, outcome);
		Assert.AreEqual(1, appliedCount);
		Assert.IsTrue(request.CancellationToken.IsCancellationRequested);
		Assert.IsFalse(coordinator.IsCurrent(request));
	}

	[TestMethod]
	public void RunAsync_CompletedRun_ReleasesItsSourceWithoutChangingTheToken()
	{
		var coordinator = new LatestRequestCoordinator();
		CancellationToken observedToken = default;

		RequestOutcome outcome = coordinator.RunAsync(
			42,
			(state, token) =>
			{
				observedToken = token;
				return Task.FromResult(state);
			},
			static (state, result) => true,
			_ => { }).GetAwaiter().GetResult();

		Assert.AreEqual(RequestOutcome.Completed, outcome);

		// The run's own source is owned by the coordinator and released when the run completes, but that
		// release is invisible to a token the compute delegate kept: the completion does not cancel it, and
		// a late registration on it stays safe and simply never fires - the same tolerance the host-driven
		// path provides for a provider that registers after its request ended.
		Assert.IsFalse(observedToken.IsCancellationRequested);

		bool callbackRan = false;
		using CancellationTokenRegistration registration = observedToken.Register(() => callbackRan = true);

		Assert.IsFalse(callbackRan);
	}
}

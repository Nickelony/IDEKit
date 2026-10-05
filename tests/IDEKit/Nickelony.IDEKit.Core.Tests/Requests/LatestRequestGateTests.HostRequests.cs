namespace Nickelony.IDEKit.Core.Tests;

public sealed partial class LatestRequestGateTests
{
	[TestMethod]
	public void BeginRequest_MintsACurrentRequestWithAnUncanceledToken()
	{
		var gate = new LatestRequestGate();

		RequestHandle request = gate.BeginRequest();

		Assert.AreNotEqual(RequestHandle.None, request);
		Assert.IsTrue(gate.IsCurrent(request));
		Assert.IsFalse(request.CancellationToken.IsCancellationRequested);
	}

	[TestMethod]
	public void IsCurrent_NoneHandleAndNeverIssuedIdentifier_AreNeverCurrent()
	{
		var gate = new LatestRequestGate();

		// Neither the handle of no request nor an identifier the gate never minted can pass the
		// check, even after a request was admitted.
		gate.BeginRequest();

		Assert.IsFalse(gate.IsCurrent(RequestHandle.None));
		Assert.IsFalse(gate.IsCurrent(new RequestHandle(12345, CancellationToken.None)));
	}

	[TestMethod]
	public void CanPublish_ReportsTheLatestUncanceledRequest()
	{
		var gate = new LatestRequestGate();

		Assert.IsFalse(gate.CanPublish(RequestHandle.None));
		Assert.IsFalse(gate.CanPublish(new RequestHandle(12345, CancellationToken.None)));

		RequestHandle request = gate.BeginRequest();

		Assert.IsTrue(gate.CanPublish(request));

		gate.CancelPendingRequest();

		Assert.IsFalse(gate.CanPublish(request), "A canceled request must not be publishable.");

		RequestHandle newer = gate.BeginRequest();

		Assert.IsFalse(gate.CanPublish(request));
		Assert.IsTrue(gate.CanPublish(newer));
	}

	[TestMethod]
	public void CanPublish_SupersededRequest_ReportsFalseEvenWhenTheNewerTokenIsLive()
	{
		var gate = new LatestRequestGate();

		RequestHandle first = gate.BeginRequest();
		RequestHandle second = gate.BeginRequest();

		Assert.IsTrue(first.CancellationToken.IsCancellationRequested, "Superseding cancels the superseded request's own token.");
		Assert.IsFalse(second.CancellationToken.IsCancellationRequested);
		Assert.IsFalse(gate.CanPublish(first), "The check must evaluate the token of the inspected request, not the latest one.");
		Assert.IsTrue(gate.CanPublish(second));
	}

	[TestMethod]
	public void Invalidate_RejectingPredicate_KeepsTheRequestCurrent()
	{
		var gate = new LatestRequestGate();

		RequestHandle request = gate.BeginRequest();

		gate.InvalidateIf(static () => false);

		Assert.IsTrue(gate.IsCurrent(request));
		Assert.IsFalse(request.CancellationToken.IsCancellationRequested);
		Assert.IsTrue(gate.CanPublish(request));
	}

	[TestMethod]
	public void Invalidate_AcceptingPredicate_RejectsTheRequestWithoutCanceling()
	{
		var gate = new LatestRequestGate();

		RequestHandle request = gate.BeginRequest();

		gate.InvalidateIf(static () => true);

		Assert.IsFalse(gate.IsCurrent(request));
		Assert.IsFalse(request.CancellationToken.IsCancellationRequested);
	}

	[TestMethod]
	public void Invalidate_NullPredicate_Throws()
	{
		var gate = new LatestRequestGate();
		Assert.ThrowsExactly<ArgumentNullException>(() => gate.InvalidateIf(null!));
	}

	[TestMethod]
	public void BeginRequest_SupersedesThePreviousRequestAndCancelsItsToken()
	{
		var gate = new LatestRequestGate();

		RequestHandle first = gate.BeginRequest();
		RequestHandle second = gate.BeginRequest();

		Assert.IsTrue(first.CancellationToken.IsCancellationRequested);
		Assert.IsFalse(gate.IsCurrent(first));
		Assert.IsTrue(gate.IsCurrent(second));
		Assert.IsFalse(second.CancellationToken.IsCancellationRequested);
	}

	[TestMethod]
	public void CancelPendingRequest_KeepsTheRequestCurrentAndReportsTheCanceledToken()
	{
		var gate = new LatestRequestGate();

		RequestHandle request = gate.BeginRequest();

		gate.CancelPendingRequest();

		// Canceling stops the work; it does not invalidate the request, and the canceled token stays
		// observable through the request's handle.
		Assert.IsTrue(request.CancellationToken.IsCancellationRequested);
		Assert.IsTrue(gate.IsCurrent(request));
	}

	[TestMethod]
	public void Invalidate_RejectsTheOutstandingRequestWithoutCancelingItsToken()
	{
		var gate = new LatestRequestGate();

		RequestHandle request = gate.BeginRequest();

		gate.Invalidate();

		Assert.IsFalse(gate.IsCurrent(request));
		Assert.IsFalse(request.CancellationToken.IsCancellationRequested);
	}

	[TestMethod]
	[Timeout(30_000, CooperativeCancellation = true)]
	public async Task BeginRequest_SupersedesAnOutstandingRun()
	{
		var gate = new LatestRequestGate();
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var runStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		bool runTokenObservedCancellation = false;
		int appliedCount = 0;

		Task<RequestOutcome> run = gate.RunAsync<int, int>(
			state: 0,

			computeAsync: async (state, token) =>
			{
				token.Register(() => runTokenObservedCancellation = true);
				runStarted.SetResult();
				await release.Task.ConfigureAwait(true);
				return state;
			},

			canApply: static (state, result) => true,
			apply: _ => appliedCount++,
			cancellationToken: TestContext.CancellationToken
		);

		await runStarted.Task;

		// A host-driven request supersedes the run: its token is canceled and its result is discarded.
		RequestHandle request = gate.BeginRequest();

		release.SetResult();

		Assert.AreEqual(RequestOutcome.Superseded, await run);
		Assert.IsTrue(runTokenObservedCancellation);
		Assert.AreEqual(0, appliedCount);
		Assert.IsTrue(gate.IsCurrent(request));
	}

	[TestMethod]
	public async Task RunAsync_SupersedesAnOutstandingHostDrivenRequest()
	{
		var gate = new LatestRequestGate();

		RequestHandle request = gate.BeginRequest();
		int appliedCount = 0;

		RequestOutcome outcome = await gate.RunAsync(
			state: 42,
			computeAsync: static (state, token) => Task.FromResult(state),
			canApply: static (state, result) => true,
			apply: _ => appliedCount++,
			cancellationToken: TestContext.CancellationToken
		);

		Assert.AreEqual(RequestOutcome.Completed, outcome);
		Assert.AreEqual(1, appliedCount);
		Assert.IsTrue(request.CancellationToken.IsCancellationRequested);
		Assert.IsFalse(gate.IsCurrent(request));
	}

	[TestMethod]
	public void RunAsync_CompletedRun_ReleasesItsSourceWithoutChangingTheToken()
	{
		var gate = new LatestRequestGate();
		CancellationToken observedToken = default;

		RequestOutcome outcome = gate.RunAsync(
				state: 42,

				computeAsync: (state, token) =>
				{
					observedToken = token;
					return Task.FromResult(state);
				},

				canApply: static (state, result) => true,
				apply: _ => { },
				cancellationToken: TestContext.CancellationToken
			)
			.GetAwaiter()
			.GetResult();

		Assert.AreEqual(RequestOutcome.Completed, outcome);

		// The run's own source is owned by the gate and released when the run completes, but that
		// release is invisible to a token the compute delegate kept: the completion does not cancel it, and
		// a late registration on it stays safe and never fires - the same tolerance the host-driven
		// path provides for a provider that registers after its request ended.
		Assert.IsFalse(observedToken.IsCancellationRequested);

		bool callbackRan = false;
		using CancellationTokenRegistration registration = observedToken.Register(() => callbackRan = true);

		Assert.IsFalse(callbackRan);
	}
}

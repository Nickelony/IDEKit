using Nickelony.IDEKit.Core.Requests;
using Nickelony.IDEKit.IntelliSense.Completion;

namespace Nickelony.IDEKit.IntelliSense.Tests.Completion;

/// <summary>
/// Verifies the request-lifetime session the completion controller exposes: supersession, cancellation,
/// invalidation, disposal, and the safe defaults after disposal.
/// </summary>
[TestClass]
public sealed class TextCompletionRequestSessionTests
{
	[TestMethod]
	public void BeginRequest_SupersedesThePreviousRequest()
	{
		using var session = new TextCompletionRequestSession();

		RequestHandle first = session.BeginRequest();
		RequestHandle second = session.BeginRequest();

		Assert.AreNotEqual(first, second);
		Assert.IsTrue(first.CancellationToken.IsCancellationRequested, "Starting a newer request must cancel the previous token.");
		Assert.IsFalse(session.IsCurrent(first));
		Assert.IsTrue(session.IsCurrent(second));
		Assert.IsFalse(second.CancellationToken.IsCancellationRequested);
	}

	[TestMethod]
	public void CancelInFlightRequest_CancelsTheTokenWithoutInvalidatingTheRequest()
	{
		using var session = new TextCompletionRequestSession();

		RequestHandle request = session.BeginRequest();
		session.CancelInFlightRequest();

		Assert.IsTrue(request.CancellationToken.IsCancellationRequested);
		Assert.IsTrue(session.IsCurrent(request), "Cancellation is not invalidation: the request stays current.");
	}

	[TestMethod]
	public void InvalidateRequests_RejectsRequestsWithoutCancelingTheToken()
	{
		using var session = new TextCompletionRequestSession();

		RequestHandle request = session.BeginRequest();

		session.InvalidateRequests();

		Assert.IsFalse(session.IsCurrent(request));
		Assert.IsFalse(request.CancellationToken.IsCancellationRequested);
	}

	[TestMethod]
	public void BeginRequest_AdmitsAValidUncanceledPublishableRequest()
	{
		using var session = new TextCompletionRequestSession();

		RequestHandle request = session.BeginRequest();

		Assert.AreNotEqual(RequestHandle.None, request);
		Assert.IsFalse(request.CancellationToken.IsCancellationRequested);
		Assert.IsTrue(session.CanPublish(request));
	}

	[TestMethod]
	public void Dispose_InvalidatesAndCancels_AndKeepsTheTokenObservable()
	{
		var session = new TextCompletionRequestSession();

		RequestHandle request = session.BeginRequest();

		session.Dispose();

		Assert.IsTrue(request.CancellationToken.IsCancellationRequested);
		Assert.IsFalse(session.IsCurrent(request));
	}

	[TestMethod]
	public void PublicOperations_AfterDisposal_ReportSafeDefaultsAndDoNotThrow()
	{
		var session = new TextCompletionRequestSession();

		session.Dispose();
		session.Dispose();

		Assert.AreEqual(RequestHandle.None, session.BeginRequest());
		Assert.IsFalse(session.IsCurrent(RequestHandle.None));

		// Both are idempotent no-ops on a disposed session.
		session.CancelInFlightRequest();
		session.InvalidateRequests();
	}

	[TestMethod]
	public void Coordinator_SharesOneLifetimeWithTheSession()
	{
		using var session = new TextCompletionRequestSession();

		RequestHandle direct = session.BeginRequest();
		RequestHandle viaCoordinator = session.Coordinator.BeginRequest();

		Assert.IsFalse(session.Coordinator.IsCurrent(direct));
		Assert.IsTrue(session.IsCurrent(viaCoordinator));
	}

	[TestMethod]
	public void BeginRequest_SupersedesARequestAdmittedThroughTheCoordinator()
	{
		using var session = new TextCompletionRequestSession();

		RequestHandle viaCoordinator = session.Coordinator.BeginRequest();
		RequestHandle direct = session.BeginRequest();

		Assert.IsFalse(
			session.Coordinator.IsCurrent(viaCoordinator),
			"A session admission must supersede a request admitted through the coordinator.");
		Assert.IsTrue(viaCoordinator.CancellationToken.IsCancellationRequested);
		Assert.IsTrue(session.IsCurrent(direct));
	}

	[TestMethod]
	public void CancelAndInvalidate_WithoutAPendingRequest_AreNoOps()
	{
		using var session = new TextCompletionRequestSession();

		session.CancelInFlightRequest();
		session.InvalidateRequests();

		RequestHandle request = session.BeginRequest();

		Assert.IsTrue(session.IsCurrent(request));
		Assert.IsFalse(request.CancellationToken.IsCancellationRequested);
	}

	[TestMethod]
	public void CanPublish_RequiresCurrentAndUncanceled()
	{
		using var session = new TextCompletionRequestSession();

		RequestHandle request = session.BeginRequest();
		Assert.IsTrue(session.CanPublish(request));

		session.CancelInFlightRequest();

		Assert.IsTrue(session.IsCurrent(request), "Cancellation is not invalidation.");
		Assert.IsFalse(session.CanPublish(request), "A canceled request must not be publishable.");
	}

	[TestMethod]
	public void CanPublish_SupersededAndDisposedSessions_ReportFalse()
	{
		var session = new TextCompletionRequestSession();

		RequestHandle first = session.BeginRequest();
		RequestHandle second = session.BeginRequest();

		Assert.IsFalse(session.CanPublish(first));
		Assert.IsTrue(session.CanPublish(second));

		session.Dispose();

		Assert.IsFalse(session.CanPublish(second));
	}

	[TestMethod]
	public void IsCurrent_NoneHandle_ReportsFalse()
	{
		using var session = new TextCompletionRequestSession();

		Assert.IsFalse(session.IsCurrent(RequestHandle.None));
	}

	[TestMethod]
	public void BeginRequest_AfterInvalidateRequests_AdmitsACurrentRequestAgain()
	{
		using var session = new TextCompletionRequestSession();

		RequestHandle first = session.BeginRequest();
		session.InvalidateRequests();

		Assert.IsFalse(session.IsCurrent(first));

		RequestHandle second = session.BeginRequest();

		Assert.IsTrue(session.IsCurrent(second));
		Assert.IsFalse(session.IsCurrent(first));
	}

	[TestMethod]
	public void Coordinator_StaysUsableAfterDisposal_WithoutReportingCurrentThroughTheSession()
	{
		var session = new TextCompletionRequestSession();
		session.Dispose();

		RequestHandle viaCoordinator = session.Coordinator.BeginRequest();

		Assert.AreNotEqual(RequestHandle.None, viaCoordinator);
		Assert.IsTrue(session.Coordinator.IsCurrent(viaCoordinator));
		Assert.IsFalse(session.IsCurrent(viaCoordinator), "The disposed session's gate still rejects the request.");
	}

	[TestMethod]
	public void CanPublish_AfterInvalidateRequests_ReportsFalse()
	{
		using var session = new TextCompletionRequestSession();

		RequestHandle request = session.BeginRequest();
		session.InvalidateRequests();

		Assert.IsFalse(session.CanPublish(request));
	}

	[TestMethod]
	public void BeginRequest_AfterDisposal_LeavesCoordinatorAdmittedRequestUntouched()
	{
		var session = new TextCompletionRequestSession();
		session.Dispose();

		RequestHandle viaCoordinator = session.Coordinator.BeginRequest();

		Assert.AreEqual(RequestHandle.None, session.BeginRequest());
		Assert.IsTrue(
			session.Coordinator.IsCurrent(viaCoordinator),
			"A rejected admission must not supersede a request it did not admit.");
		Assert.IsFalse(viaCoordinator.CancellationToken.IsCancellationRequested);
	}

	[TestMethod]
	public void CancelInFlightRequest_AfterDisposal_DoesNotCancelCoordinatorAdmittedRequest()
	{
		var session = new TextCompletionRequestSession();
		session.Dispose();

		RequestHandle viaCoordinator = session.Coordinator.BeginRequest();

		session.CancelInFlightRequest();

		Assert.IsFalse(
			viaCoordinator.CancellationToken.IsCancellationRequested,
			"A disposed session's cancellation must never reach a request admitted through the coordinator.");
		Assert.IsTrue(session.Coordinator.CanPublish(viaCoordinator));
	}

	[TestMethod]
	public void InvalidateRequests_AfterDisposal_DoesNotInvalidateCoordinatorAdmittedRequest()
	{
		var session = new TextCompletionRequestSession();
		session.Dispose();

		RequestHandle viaCoordinator = session.Coordinator.BeginRequest();

		session.InvalidateRequests();

		Assert.IsTrue(
			session.Coordinator.IsCurrent(viaCoordinator),
			"A disposed session's invalidation must never reach a request admitted through the coordinator.");
	}
}

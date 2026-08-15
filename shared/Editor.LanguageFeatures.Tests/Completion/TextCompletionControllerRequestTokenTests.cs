#if AVALONIAEDIT
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.Core.Requests;
using Nickelony.IDEKit.IntelliSense.Completion;
using static Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests.CompletionTestHost;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;
#else
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.Core.Requests;
using Nickelony.IDEKit.IntelliSense.Completion;
using static Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests.CompletionTestHost;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;
#endif

/// <summary>
/// Verifies the completion request session's token semantics: which operation cancels or invalidates
/// the current token without touching the other mechanism, and that manually driven requests and the
/// standard request pipeline supersede each other.
/// </summary>
[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed class TextCompletionControllerRequestTokenTests
{
	[TestMethod]
	public void InvalidateRequests_RejectsRequestsWithoutCancelingTheToken()
	{
		using TextCompletionController controller = CreateController(CreateEditor());

		RequestHandle request = controller.Requests.BeginRequest();

		Assert.IsTrue(controller.Requests.IsCurrent(request));
		Assert.IsFalse(request.CancellationToken.IsCancellationRequested);

		controller.Requests.InvalidateRequests();

		// Invalidating only rejects the result; the provider call keeps running until it is canceled explicitly.
		Assert.IsFalse(controller.Requests.IsCurrent(request));
		Assert.IsFalse(request.CancellationToken.IsCancellationRequested);
	}

	[TestMethod]
	public void CancelInFlightRequest_CancelsTheTokenWithoutInvalidatingTheRequest()
	{
		using TextCompletionController controller = CreateController(CreateEditor());

		RequestHandle request = controller.Requests.BeginRequest();

		controller.Requests.CancelInFlightRequest();

		// Canceling stops the provider call; the request stays current until it is invalidated.
		Assert.IsTrue(request.CancellationToken.IsCancellationRequested);
		Assert.IsTrue(controller.Requests.IsCurrent(request));

		// Invalidation rejects a request without touching its token, and a following cancel still stops
		// the provider, so the two operations compose in either order.
		RequestHandle second = controller.Requests.BeginRequest();

		controller.Requests.InvalidateRequests();
		controller.Requests.CancelInFlightRequest();

		Assert.IsFalse(controller.Requests.IsCurrent(second));
		Assert.IsTrue(second.CancellationToken.IsCancellationRequested);
	}

	[TestMethod]
	public void BeginRequest_SupersededToken_IsCanceled()
	{
		using TextCompletionController controller = CreateController(CreateEditor());

		RequestHandle first = controller.Requests.BeginRequest();

		RequestHandle second = controller.Requests.BeginRequest();

		// The superseded request's token is canceled immediately, while the newest request's token is live.
		Assert.IsTrue(first.CancellationToken.IsCancellationRequested);
		Assert.IsFalse(second.CancellationToken.IsCancellationRequested);
		Assert.IsFalse(controller.Requests.IsCurrent(first));
		Assert.IsTrue(controller.Requests.IsCurrent(second));
	}

	[TestMethod]
	public void BeginRequest_WhileStandardRequestInFlight_SupersedesTheStandardRequest()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CompletionTestHost.CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				var providerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
				var providerGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

				Task<bool> request = hosted.Controller.RequestAsync(async _ =>
				{
					providerStarted.SetResult();
					await providerGate.Task.ConfigureAwait(true);
					return TextCompletionSessionDecision.Open([new TextCompletionItem("stale")], 0, 5);
				});

				DispatcherTestUtils.PumpUntil(() => providerStarted.Task.IsCompleted);

				// Both flows share one request lifetime: a manually driven request supersedes the standard
				// request, whose late decision must not open a window.
				RequestHandle manualRequest = hosted.Controller.Requests.BeginRequest();

				providerGate.SetResult();
				DispatcherTestUtils.PumpUntil(() => request.IsCompleted);

				Assert.IsFalse(request.GetAwaiter().GetResult());
				Assert.IsTrue(hosted.Controller.Requests.IsCurrent(manualRequest));
				Assert.IsNull(hosted.Coordinator.ActiveWindow);
			}
		}
	}

	[TestMethod]
	public void RequestAsync_WhileManualRequestTracked_SupersedesTheManualRequest()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CompletionTestHost.CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				RequestHandle manualRequest = hosted.Controller.Requests.BeginRequest();

				Task<bool> request = hosted.Controller.RequestAsync(
					_ => Task.FromResult(TextCompletionSessionDecision.Open([new TextCompletionItem("sample")], 0, 5)));

				DispatcherTestUtils.PumpUntil(() => request.IsCompleted);

				// The standard request supersedes the tracked manual request: its token is canceled and its
				// handle no longer passes the current check.
				Assert.IsTrue(request.GetAwaiter().GetResult());
				Assert.IsTrue(manualRequest.CancellationToken.IsCancellationRequested);
				Assert.IsFalse(hosted.Controller.Requests.IsCurrent(manualRequest));
			}
		}
	}
}

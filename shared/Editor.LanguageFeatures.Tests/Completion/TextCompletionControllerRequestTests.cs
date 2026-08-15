#if AVALONIAEDIT
using AvaloniaEdit.CodeCompletion;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.IntelliSense.Completion;
using static Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests.CompletionTestHost;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;
#else
using ICSharpCode.AvalonEdit.CodeCompletion;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.IntelliSense.Completion;
using static Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests.CompletionTestHost;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;
#endif

[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed partial class TextCompletionControllerRequestTests
{
	[TestMethod]
	public void RequestAsync_ThrowingCallback_IsContainedAndLogged()
	{
		var logger = new CapturingLogger();
		var controller = CreateController(CreateEditor(), logger: logger);

		try
		{
			Task<bool> request = controller.RequestAsync(
				_ => throw new InvalidOperationException("Completion provider failed."));

			DispatcherTestUtils.PumpUntil(() => request.IsCompleted);

			// A provider failure must not fault the caller: it is contained and logged like a failure on the
			// scheduled path, and the request reports its not-applied result.
			Assert.IsFalse(request.GetAwaiter().GetResult());
			Assert.AreEqual(1, logger.Entries.Count);
			Assert.AreEqual(1000, logger.Entries[0].EventId.Id);

			// The controller stays usable after the contained failure.
			Assert.IsFalse(controller.CurrentPresentation.IsListVisible);
		}
		finally
		{
			controller.Dispose();
		}
	}

	[TestMethod]
	public void RequestAsync_ProviderDecision_AppliesTheDecision()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Task<bool> request = hosted.Controller.RequestAsync(
					_ => Task.FromResult(TextCompletionSessionDecision.Open([new TextCompletionItem("sample")], 0, 5)));

				DispatcherTestUtils.PumpUntil(() => request.IsCompleted);

				// The standard pipeline runs the provider callback, validates the request, and applies the
				// decision without host-side token plumbing.
				Assert.IsTrue(request.GetAwaiter().GetResult());
				Assert.IsTrue(hosted.Controller.CurrentPresentation.IsListVisible);
			}
		}
	}

	[TestMethod]
	public void RequestAsync_ProviderReturningItems_MapsThroughTheFactoryHook()
	{
		var mappedItem = new TestCompletionData("sample");
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CompletionTestHost.CreateHostedController(hooks: new TextCompletionControllerHooks
			{
				CompletionItemFactory = _ => mappedItem
			});

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Task<bool> request = hosted.Controller.RequestAsync(
					_ => Task.FromResult(TextCompletionSessionDecision.Open([new TextCompletionItem("sample")], 0, 5)));

				DispatcherTestUtils.PumpUntil(() => request.IsCompleted);

				// The automatic pipeline uses the same single mapping seam as the manual ApplyDecision path.
				Assert.IsTrue(request.GetAwaiter().GetResult());

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				Assert.AreSame(mappedItem, completionWindow.CompletionList.CompletionData[0]);
			}
		}
	}

	[TestMethod]
	public void RequestAsync_CallbackReportingCancellation_LeavesStateIntact()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Task<bool> request = hosted.Controller.RequestAsync(
					_ => throw new OperationCanceledException());

				DispatcherTestUtils.PumpUntil(() => request.IsCompleted);

				// A canceled request is not a failure and does not open or change a window.
				Assert.IsFalse(request.GetAwaiter().GetResult());
				Assert.IsFalse(hosted.Controller.CurrentPresentation.IsListVisible);
			}
		}
	}

	[TestMethod]
	public void RequestAsync_SupersededRequest_DoesNotApplyTheStaleDecision()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				var firstRequestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
				var firstRequestGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

				Task<bool> firstRequest = hosted.Controller.RequestAsync(async _ =>
				{
					firstRequestStarted.SetResult();
					await firstRequestGate.Task.ConfigureAwait(true);
					return TextCompletionSessionDecision.Open([new TextCompletionItem("stale")], 0, 5);
				});

				DispatcherTestUtils.PumpUntil(() => firstRequestStarted.Task.IsCompleted);

				// The newest request wins: the first callback completes later with a stale decision that must
				// not be applied.
				Task<bool> secondRequest = hosted.Controller.RequestAsync(
					_ => Task.FromResult(TextCompletionSessionDecision.Open([new TextCompletionItem("sample")], 0, 5)));

				DispatcherTestUtils.PumpUntil(() => secondRequest.IsCompleted);

				firstRequestGate.SetResult();
				DispatcherTestUtils.PumpUntil(() => firstRequest.IsCompleted);

				Assert.IsTrue(secondRequest.GetAwaiter().GetResult());
				Assert.IsFalse(firstRequest.GetAwaiter().GetResult());
				Assert.AreEqual("sample", hosted.Controller.WindowCoordinator.ActiveWindow!.CompletionList.CompletionData[0].Text);
			}
		}
	}

	[TestMethod]
	public void RequestAsync_ProviderCompletingAfterCancelInFlightRequest_DoesNotApplyTheDecision()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				// The provider observes the cancellation but returns a decision anyway, like a provider that
				// cannot stop early; the canceled result must not open a window.
				Task<bool> request = hosted.Controller.RequestAsync(_ =>
				{
					hosted.Controller.Requests.CancelInFlightRequest();
					return Task.FromResult(TextCompletionSessionDecision.Open([new TextCompletionItem("sample")], 0, 6));
				});

				DispatcherTestUtils.PumpUntil(() => request.IsCompleted);

				Assert.IsFalse(request.GetAwaiter().GetResult());
				Assert.IsFalse(hosted.Controller.CurrentPresentation.IsListVisible);
				Assert.IsNull(hosted.Coordinator.ActiveWindow);
			}
		}
	}

	[TestMethod]
	public void RequestAsync_ProviderCompletingOnAPoolThreadWithoutSynchronizationContext_AppliesTheDecisionOnTheEditorThread()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				// The test thread carries no synchronization context, so this provider's continuation
				// resumes on a thread-pool thread; the completed decision must still be admitted and applied
				// on the editor thread instead of failing with a cross-thread error.
				var providerGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

				Task<bool> request = hosted.Controller.RequestAsync(async _ =>
				{
					await providerGate.Task.ConfigureAwait(false);
					return TextCompletionSessionDecision.Open([new TextCompletionItem("sample")], 0, 6);
				});

				DispatcherTestUtils.PumpFrames(TimeSpan.FromMilliseconds(20.0));
				providerGate.SetResult();
				DispatcherTestUtils.PumpUntil(() => request.IsCompleted);

				Assert.IsTrue(request.GetAwaiter().GetResult());
				Assert.IsTrue(hosted.Controller.CurrentPresentation.IsListVisible);
				Assert.IsNotNull(hosted.Coordinator.ActiveWindow);
			}
		}
	}
}

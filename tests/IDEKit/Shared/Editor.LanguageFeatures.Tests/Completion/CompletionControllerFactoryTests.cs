#if AVALONIAEDIT
using AvaloniaEdit.CodeCompletion;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;
using static Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests.CompletionTestHelpers;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using ICSharpCode.AvalonEdit.CodeCompletion;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;
using static Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests.CompletionTestHelpers;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed partial class CompletionControllerFactoryTests
{
	[TestMethod]
	public void RequestAsync_ProviderAnswer_OpensTheWindowWithoutAHostHook()
	{
		var provider = ScriptedCompletionProvider.Answering("sample");

		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CreateHostedController(provider: provider);

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					Task<bool> request = hosted.Controller.RequestAsync();

					DispatcherTestHelpers.PumpUntil(() => request.IsCompleted);

					// The controller asks its provider for the document and the caret, and the kernel's decision opens the window.
					Assert.IsTrue(request.GetAwaiter().GetResult());
					Assert.IsTrue(hosted.Controller.CurrentPresentation.IsListVisible);
					Assert.HasCount(1, provider.Requests);
					Assert.AreEqual(TestDocumentId, provider.Requests[0].DocumentId);
					Assert.AreEqual("sample", provider.Requests[0].DocumentText);
					Assert.AreEqual(CompletionContext.Invoked, provider.Requests[0].Context);
				}
			}
		}
	}

	[TestMethod]
	public void RequestAsync_ProviderReturningItems_MapsThroughTheFactoryHook()
	{
		var mappedItem = new StubCompletionData("sample");

		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CompletionTestHelpers.CreateHostedController(
				hooks: new CompletionPresenterHooks { CompletionItemFactory = _ => mappedItem },
				provider: ScriptedCompletionProvider.Answering("sample")
			);

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					Task<bool> request = hosted.Controller.RequestAsync();

					DispatcherTestHelpers.PumpUntil(() => request.IsCompleted);

					// The automatic pipeline uses the same single item-mapping hook as the manual ApplyDirective path.
					Assert.IsTrue(request.GetAwaiter().GetResult());

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					Assert.AreSame(mappedItem, completionWindow.CompletionList.CompletionData[0]);
				}
			}
		}
	}

	[TestMethod]
	public void RequestAsync_ContextVersionChangedInFlight_DiscardsTheDecision()
	{
		int contextVersion = 1;
		var provider = new ScriptedCompletionProvider();

		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CreateHostedController(
				controllerHooks: new CompletionControllerHooks { ContextVersionProvider = () => contextVersion },
				provider: provider
			);

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					using var request = new GatedCompletionRequest(hosted.Controller, provider);

					// The hook reaches the controller through the factory: the host state moved on while the provider was working, so the
					// decision belongs to the earlier state.
					contextVersion++;

					request.Release();
					DispatcherTestHelpers.PumpUntil(() => request.Result.IsCompleted);

					Assert.IsFalse(request.Result.GetAwaiter().GetResult());
					Assert.IsNull(hosted.Presenter.ActiveWindow);
					Assert.IsFalse(request.Token.IsCancellationRequested, "The version check discards the decision without canceling the provider.");
				}
			}
		}
	}

	[TestMethod]
	public void RequestAsync_ProviderCompletingOnAPoolThreadWithoutSynchronizationContext_AppliesTheDecisionOnTheEditorThread()
	{
		var provider = new ScriptedCompletionProvider();

		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CreateHostedController(provider: provider);

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					// The test thread carries no synchronization context, so this provider's continuation
					// resumes on a thread-pool thread; the completed decision must still be admitted and applied
					// on the editor thread instead of failing with a cross-thread error.
					var providerGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
					var providerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

					provider.Answer = async (_, _) =>
					{
						providerStarted.SetResult();

						await providerGate.Task.ConfigureAwait(false);
						return [new CompletionItem("sample")];
					};

					Task<bool> request = hosted.Controller.RequestAsync();

					// Wait for the provider to reach the gate instead of a fixed window, so the release cannot race
					// the provider's start on a loaded machine.
					DispatcherTestHelpers.PumpUntil(() => providerStarted.Task.IsCompleted);
					providerGate.SetResult();
					DispatcherTestHelpers.PumpUntil(() => request.IsCompleted);

					Assert.IsTrue(request.GetAwaiter().GetResult());
					Assert.IsTrue(hosted.Controller.CurrentPresentation.IsListVisible);
					Assert.IsNotNull(hosted.Presenter.ActiveWindow);
				}
			}
		}
	}
}

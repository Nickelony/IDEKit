#if AVALONIAEDIT
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

public sealed partial class CompletionControllerFactoryTests
{
	// A failure inside the item mapping runs inside the controller-owned request pipeline, so it is contained and logged instead of faulting the
	// caller that started the request.
	[TestMethod]
	public void RequestAsync_ThrowingItemFactory_IsContainedLoggedAndLeavesNoWindow()
	{
		var logger = new RecordingLogger();

		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CompletionTestHelpers.CreateHostedController(
				hooks: new CompletionPresenterHooks
				{
					CompletionItemFactory = static _ => throw new InvalidOperationException("The item factory failed."),
				},

				logger: logger,
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

					// The mapping failure happens after the provider answered, so it is reported
					// exactly like a provider failure: contained, logged with event id 1000, and not applied.
					Assert.IsFalse(request.GetAwaiter().GetResult());
					Assert.HasCount(1, logger.Entries);
					Assert.AreEqual(1000, logger.Entries[0].EventId.Id);
					Assert.IsFalse(hosted.Presenter.IsWindowOpen);
					Assert.IsFalse(hosted.Controller.CurrentPresentation.IsListVisible);
				}
			}
		}
	}
}

#if AVALONIAEDIT
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.IntelliSense.Completion;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;
#else
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.IntelliSense.Completion;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;
#endif

/// <summary>
/// Covers apply-stage failure containment of the completion request pipeline: a failure inside the item
/// mapping runs inside the controller-owned pipeline, so it is contained and logged instead of faulting
/// the caller that started the request.
/// </summary>
[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed class TextCompletionControllerRequestFailureTests
{
	[TestMethod]
	public void RequestAsync_ThrowingItemFactory_IsContainedLoggedAndLeavesNoWindow()
	{
		var logger = new CapturingLogger();
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CompletionTestHost.CreateHostedController(
				hooks: new TextCompletionControllerHooks
				{
					CompletionItemFactory = static _ => throw new InvalidOperationException("The item factory failed.")
				},
				logger: logger);

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Task<bool> request = hosted.Controller.RequestAsync(
					_ => Task.FromResult(TextCompletionSessionDecision.Open([new TextCompletionItem("sample")], 0, 5)));

				DispatcherTestUtils.PumpUntil(() => request.IsCompleted);

				// The mapping failure happens after the provider computed its decision, so it is reported
				// exactly like a provider failure: contained, logged with event id 1000, and not applied.
				Assert.IsFalse(request.GetAwaiter().GetResult());
				Assert.AreEqual(1, logger.Entries.Count);
				Assert.AreEqual(1000, logger.Entries[0].EventId.Id);
				Assert.IsFalse(hosted.Coordinator.IsWindowOpen);
				Assert.IsFalse(hosted.Controller.CurrentPresentation.IsListVisible);
			}
		}
	}
}

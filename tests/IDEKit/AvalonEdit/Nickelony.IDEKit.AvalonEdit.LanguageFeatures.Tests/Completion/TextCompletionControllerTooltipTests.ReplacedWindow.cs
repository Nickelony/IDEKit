using ICSharpCode.AvalonEdit.CodeCompletion;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using System.Windows.Controls;
using static Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests.CompletionTestHost;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

public sealed partial class TextCompletionControllerTooltipTests
{
	[TestMethod]
	public void Tooltip_SelectionChangeOnAReplacedWindowsListBox_DoesNotScheduleStaleTooltipWork()
	{
		int resolveCount = 0;
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CompletionTestHost.CreateHostedController(hooks: new TextCompletionControllerHooks
			{
				ResolveDescriptionAsync = (_, _) =>
				{
					resolveCount++;
					return Task.FromResult<object?>(null);
				}
			});

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				// The description is null, so only the controller's own debounced path would resolve one.
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample", description: null)], 0, 5));

				CompletionWindow replacedWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The first completion window was not tracked.");
				ToolTip replacedTooltip = GetCompletionTooltip(replacedWindow);

				// A different replacement start closes the first window and opens a new one in its place.
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample", description: null)], 1, 6));
				Assert.AreNotSame(replacedWindow, hosted.Coordinator.ActiveWindow);

				// The replaced window's list box must no longer schedule tooltip work: its selection handler
				// is detached when the window closes, so a queued selection change cannot arm the debounce.
				replacedWindow.CompletionList.SelectItem("sample");

				Assert.IsFalse(hosted.Controller.TestHooks.IsTooltipUpdatePending);

				DispatcherTestUtils.PumpFrames(TimeSpan.FromMilliseconds(20.0));

				// Nothing scheduled means the description resolver never ran for the stale window, and the
				// replaced tooltip was not reopened.
				Assert.AreEqual(0, resolveCount);
				Assert.IsFalse(replacedTooltip.IsOpen);
				Assert.IsFalse(hosted.Controller.CurrentPresentation.IsTooltipVisible);
			}
		}
	}
}

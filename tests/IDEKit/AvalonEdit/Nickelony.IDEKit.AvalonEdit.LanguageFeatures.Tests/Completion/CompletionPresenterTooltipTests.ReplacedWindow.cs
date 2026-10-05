using System.Windows.Controls;
using ICSharpCode.AvalonEdit.CodeCompletion;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;
using static Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests.CompletionTestHelpers;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

public sealed partial class CompletionPresenterTooltipTests
{
	[TestMethod]
	public void Tooltip_SelectionChangeOnAReplacedWindowsListBox_DoesNotScheduleStaleTooltipWork()
	{
		int resolveCount = 0;

		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CompletionTestHelpers.CreateHostedController(
				hooks: new CompletionPresenterHooks
				{
					ResolveDescriptionAsync = (_, _) =>
					{
						resolveCount++;
						return Task.FromResult<object?>(null);
					},
				}
			);

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					// The description is null, so only the controller's own debounced path would resolve one.
					Assert.IsTrue(hosted.Completion.OpenOrRefresh([CreateItem("sample", description: null)], TextRange.FromBounds(0, 5)));

					CompletionWindow replacedWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The first completion window was not tracked.");

					ToolTip replacedTooltip = GetCompletionTooltip(replacedWindow);

					// A different replacement start closes the first window and opens a new one in its place.
					Assert.IsTrue(hosted.Completion.OpenOrRefresh([CreateItem("sample", description: null)], TextRange.FromBounds(1, 6)));
					Assert.AreNotSame(replacedWindow, hosted.Presenter.ActiveWindow);

					// The replaced window's list box must stop scheduling tooltip work: its selection handler
					// is detached when the window closes, so a queued selection change cannot arm the debounce.
					replacedWindow.CompletionList.SelectItem("sample");

					Assert.IsFalse(hosted.Completion.IsTooltipUpdatePending);

					DispatcherTestHelpers.PumpFrames(TimeSpan.FromMilliseconds(20.0));

					// Nothing scheduled means the description resolver never ran for the stale window, and the
					// replaced tooltip was not reopened.
					Assert.AreEqual(0, resolveCount);
					Assert.IsFalse(replacedTooltip.IsOpen);
					Assert.IsFalse(hosted.Controller.CurrentPresentation.IsTooltipVisible);
				}
			}
		}
	}
}

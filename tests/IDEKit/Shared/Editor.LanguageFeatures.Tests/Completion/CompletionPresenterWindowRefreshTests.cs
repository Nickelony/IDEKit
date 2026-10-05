using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;
#if AVALONIAEDIT
using Avalonia.Controls;
using Avalonia.Threading;
using AvaloniaEdit.CodeCompletion;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using static Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests.CompletionTestHelpers;
using static Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests.CompletionWindowTestHelpers;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using System.Windows.Controls;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.CodeCompletion;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using static Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests.CompletionTestHelpers;
using static Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests.CompletionWindowTestHelpers;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

/// <summary>Covers the completion window's in-place refresh path.</summary>
[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed class CompletionPresenterWindowRefreshTests
{
	[TestMethod]
	public void OpenOrRefresh_EmptyQueryRefresh_DisplaysTheReplacedItems()
	{
		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					// An empty replacement range makes the completion query empty; the window shows every
					// candidate instead of filtering them.
					Assert.IsTrue(hosted.Completion.OpenOrRefresh([CreateItem("sample"), CreateItem("second")], TextRange.FromBounds(0, 0)));

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					ListBox listBox = completionWindow.CompletionList.ListBox;

					AwaitListSteadyState(completionWindow, expectedItemCount: 2);

					// The refresh replaces the items for the same empty query; the list must display the new
					// set instead of the memoized snapshot of the previous one.
					Assert.IsTrue(hosted.Completion.OpenOrRefresh([CreateItem("alpha"), CreateItem("beta")], TextRange.FromBounds(0, 0)));

					string[] displayedTexts = [.. listBox.Items.Cast<ICompletionData>().Select(item => item.Text)];

					Assert.AreSequenceEqual(["alpha", "beta"], displayedTexts);

					// The refresh re-applies the engine's best-match selection; for an empty query the first
					// candidate wins, so the displayed order and the selection agree.
					SettleDeferredSelection();
					var selectedItem = listBox.SelectedItem as ICompletionData;

					Assert.IsNotNull(selectedItem);
					Assert.AreEqual(displayedTexts[0], selectedItem.Text);
				}
			}
		}
	}

	[TestMethod]
	public void OpenOrRefresh_RefreshAppliesPreselection_LikeTheOpenPath()
	{
		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					Assert.IsTrue(hosted.Completion.OpenOrRefresh([CreateItem("sample"), CreateItem("second")], TextRange.FromBounds(0, 1)));

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					// Let the posted initial selection reach its steady state before the refresh replaces the set.
					DispatcherTestHelpers.PumpUntil(
						() => completionWindow.CompletionList.ListBox.SelectedItem is not null,
						timeout: null,
						priority: DispatcherPriority.ContextIdle
					);

					var items = new ICompletionData[]
					{
						new CompletionItemAdapter(new CompletionItem("sample") { Priority = 10.0 }),
						new CompletionItemAdapter(new CompletionItem("second") { IsPreselected = true }),
					};

					// The refresh re-applies the preselection policy synchronously: the provider's hint wins over
					// the engine's higher-priority best match, exactly as on the open path.
					Assert.IsTrue(hosted.Completion.OpenOrRefresh(items, TextRange.FromBounds(0, 1)));
					Assert.AreSame(items[1], completionWindow.CompletionList.ListBox.SelectedItem);
				}
			}
		}
	}

	[TestMethod]
	public void OpenOrRefresh_RefreshWithNonMatchingItems_ClosesTheEmptyWindowAndReportsApplied()
	{
		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					Assert.IsTrue(hosted.Completion.OpenOrRefresh([CreateItem("sample")], TextRange.FromBounds(0, 6)));

					// The replacement range spells "sample" and the refreshed item set matches nothing, so the
					// refresh closes the emptied window; the call still reports that the refresh was applied.
					Assert.IsTrue(hosted.Completion.OpenOrRefresh([CreateItem("zeta")], TextRange.FromBounds(0, 6)));

					Assert.IsFalse(hosted.Presenter.IsWindowOpen);
					Assert.IsFalse(hosted.Controller.CurrentPresentation.IsListVisible);
				}
			}
		}
	}

	[TestMethod]
	public void OpenOrRefresh_RefreshHookThrows_ClosesTheWindowAndRethrows()
	{
		int invocationCount = 0;

		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CompletionTestHelpers.CreateHostedController(
				hooks: new CompletionPresenterHooks
				{
					ConfigureWindow = _ =>
					{
						invocationCount++;

						// The first (open) pass succeeds; the refresh pass fails.
						if (invocationCount > 1)
							throw new InvalidOperationException("The window hook failed.");
					},
				}
			);

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					Assert.IsTrue(hosted.Completion.OpenOrRefresh([CreateItem("sample")], TextRange.FromBounds(0, 5)));

					// A throwing hook during a refresh must not leave a half-updated window behind: the window
					// is closed, the tracked state is reset, and the failure propagates to the refresh caller.
					InvalidOperationException exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
						hosted.Completion.OpenOrRefresh([CreateItem("sample")], TextRange.FromBounds(0, 5))
					);

					Assert.AreEqual("The window hook failed.", exception.Message);
					Assert.IsNull(hosted.Presenter.ActiveWindow);
					Assert.IsFalse(hosted.Controller.CurrentPresentation.IsListVisible);
				}
			}
		}
	}

	[TestMethod]
	public void OpenOrRefresh_SameStartOffset_RefreshesTheOpenWindowInPlace()
	{
		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					Assert.IsTrue(hosted.Completion.OpenOrRefresh([CreateItem("sample"), CreateItem("sampler")], TextRange.FromBounds(0, 5)));

					CompletionWindow firstWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					// A visible tooltip describes the earlier selection, so a refresh must not leave its content in place.
					Action assertStaleTooltipCleared = CompletionTooltipTestHelpers.ArmStaleTooltip(firstWindow);

					// A new request for the same replacement start must keep the window: it is refreshed in
					// place instead of being closed and recreated.
					Assert.IsTrue(
						hosted.Completion.OpenOrRefresh(
							[CreateItem("sample"), CreateItem("sampler"), CreateItem("sampled")],
							TextRange.FromBounds(0, 5)
						)
					);

					CompletionWindow refreshedWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The refreshed completion window was not tracked.");

					Assert.AreSame(firstWindow, refreshedWindow);
					assertStaleTooltipCleared();

					Assert.AreEqual(0, refreshedWindow.StartOffset);
					Assert.AreEqual(5, refreshedWindow.EndOffset);
					Assert.HasCount(3, refreshedWindow.CompletionList.CompletionData);
					Assert.AreEqual("sampled", refreshedWindow.CompletionList.CompletionData[2].Text);

					// The list was re-filtered for the window's query, so the initial selection is established
					// without the deferred open path.
					SettleDeferredSelection();
					Assert.IsNotNull(refreshedWindow.CompletionList.ListBox.SelectedItem);
				}
			}
		}
	}

	[TestMethod]
	public void OpenOrRefresh_ShiftedEndOffset_UpdatesTheRefreshedWindowRange()
	{
		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					Assert.IsTrue(hosted.Completion.OpenOrRefresh([CreateItem("sample")], TextRange.FromBounds(0, 5)));

					CompletionWindow firstWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					// The replacement end moved while the start stayed anchored: the refreshed window must adopt
					// the new range so the commit replaces the text the user actually typed over.
					Assert.IsTrue(hosted.Completion.OpenOrRefresh([CreateItem("sample"), CreateItem("sampler")], TextRange.FromBounds(0, 4)));

					CompletionWindow refreshedWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The refreshed completion window was not tracked.");

					Assert.AreSame(firstWindow, refreshedWindow);
					Assert.AreEqual(4, refreshedWindow.EndOffset);
				}
			}
		}
	}

	[TestMethod]
	public void OpenOrRefresh_ShiftedStartOffset_ReplacesTheWindow()
	{
		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					Assert.IsTrue(hosted.Completion.OpenOrRefresh([CreateItem("sample")], TextRange.FromBounds(0, 5)));

					CompletionWindow firstWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					// A different replacement start starts a new session: the old window is closed and a new one
					// is opened for the new range.
					Assert.IsTrue(hosted.Completion.OpenOrRefresh([CreateItem("pawn")], TextRange.FromBounds(1, 5)));

					CompletionWindow replacementWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The replacement completion window was not tracked.");

					Assert.AreNotSame(firstWindow, replacementWindow);
					Assert.IsFalse(IsShown(firstWindow));
					Assert.AreEqual(1, replacementWindow.StartOffset);
					Assert.AreEqual(5, replacementWindow.EndOffset);
					Assert.IsTrue(hosted.Controller.CurrentPresentation.IsListVisible);
				}
			}
		}
	}
}

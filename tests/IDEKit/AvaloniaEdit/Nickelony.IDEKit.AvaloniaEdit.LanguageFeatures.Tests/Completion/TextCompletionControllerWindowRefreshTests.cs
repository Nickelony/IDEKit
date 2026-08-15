using Avalonia.Controls;
using Avalonia.Threading;
using AvaloniaEdit.CodeCompletion;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.IntelliSense.Completion;
using static Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests.CompletionTestHost;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

[AvaloniaTestClass]
public sealed class TextCompletionControllerWindowRefreshTests
{
	[TestMethod]
	public void OpenOrRefresh_EmptyQueryRefresh_DisplaysTheReplacedItems()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted = CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				// An empty replacement range makes the completion query empty; the window shows every
				// candidate instead of filtering them.
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample"), CreateItem("second")], 0, 0));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				ListBox listBox = completionWindow.CompletionList.ListBox;

				// Let the posted initial selection run so the list is in its steady state. The WPF reference
				// waited for the ItemsSource to swap away from the raw completion data; the mirror waits for
				// the steady count instead because AvaloniaEdit owns that swap internally.
				DispatcherTestUtils.PumpUntil(
					() => listBox.ItemCount == 2,
					timeout: null,
					priority: DispatcherPriority.ContextIdle);

				Assert.AreEqual(2, listBox.ItemCount);

				// The refresh replaces the items for the same empty query; the list must display the new
				// set instead of the memoized snapshot of the previous one.
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("alpha"), CreateItem("beta")], 0, 0));

				string[] displayedTexts = listBox.Items.Cast<ICompletionData>().Select(item => item.Text).ToArray();

				CollectionAssert.AreEqual(new[] { "alpha", "beta" }, displayedTexts);

				// Avalonia divergence: AvaloniaEdit applies the best-match selection asynchronously, so the
				// deferred selection is pumped in before asserting it.
				DispatcherTestUtils.PumpUntilIdle();

				Assert.IsNotNull(listBox.SelectedItem);
			}
		}
	}

	[TestMethod]
	public void OpenOrRefresh_RefreshAppliesPreselection_LikeTheOpenPath()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted = CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample"), CreateItem("second")], 0, 1));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				// Let the posted initial selection reach its steady state before the refresh replaces the set.
				DispatcherTestUtils.PumpUntil(
					() => completionWindow.CompletionList.ListBox.SelectedItem is not null,
					timeout: null,
					priority: DispatcherPriority.ContextIdle);

				var items = new ICompletionData[]
				{
					new TextCompletionItemCompletionData(new TextCompletionItem("sample") { Priority = 10.0 }),
					new TextCompletionItemCompletionData(new TextCompletionItem("second") { IsPreselected = true })
				};

				// The refresh re-applies the preselection policy synchronously: the provider's hint wins over
				// the engine's higher-priority best match, exactly as on the open path.
				Assert.IsTrue(hosted.Controller.OpenOrRefresh(items, 0, 1));
				Assert.AreSame(items[1], completionWindow.CompletionList.ListBox.SelectedItem);
			}
		}
	}

	[TestMethod]
	public void OpenOrRefresh_RefreshWithNonMatchingItems_ClosesTheEmptyWindowAndReportsApplied()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted = CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample")], 0, 6));

				// The replacement range spells "sample" and the refreshed item set matches nothing, so the
				// refresh closes the emptied window; the call still reports that the refresh was applied.
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("zeta")], 0, 6));

				Assert.IsFalse(hosted.Coordinator.IsWindowOpen);
				Assert.IsFalse(hosted.Controller.CurrentPresentation.IsListVisible);
			}
		}
	}

	[TestMethod]
	public void OpenOrRefresh_RefreshHookThrows_ClosesTheWindowAndRethrows()
	{
		int invocationCount = 0;
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CompletionTestHost.CreateHostedController(hooks: new TextCompletionControllerHooks
			{
				ConfigureWindow = _ =>
				{
					invocationCount++;

					// The first (open) pass succeeds; the refresh pass fails.
					if (invocationCount > 1)
						throw new InvalidOperationException("The window hook failed.");
				}
			});

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample")], 0, 5));

				// A throwing hook during a refresh must not leave a half-updated window behind: the window
				// is closed, the tracked state is reset, and the failure propagates to the refresh caller.
				var exception = Assert.ThrowsExactly<InvalidOperationException>(
					() => hosted.Controller.OpenOrRefresh([CreateItem("sample")], 0, 5));

				Assert.AreEqual("The window hook failed.", exception.Message);
				Assert.IsNull(hosted.Coordinator.ActiveWindow);
				Assert.IsFalse(hosted.Controller.CurrentPresentation.IsListVisible);
			}
		}
	}

	[TestMethod]
	public void OpenOrRefresh_SameStartOffset_RefreshesTheOpenWindowInPlace()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted = CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample"), CreateItem("sampler")], 0, 5));

				CompletionWindow firstWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				// A visible tooltip would describe the previously selected item, so a refresh must not leave
				// its content in place. Avalonia divergence: AvaloniaEdit exposes no tooltip member, so the
				// tooltip path is disabled and there is no tooltip content to invalidate; the refresh still
				// has to keep the window and replace its items, which the assertions below pin.
				Assert.IsFalse(CompletionWindowTooltipAccess.TryGetTooltip(firstWindow, out _));

				// A new request for the same replacement start must keep the window: it is refreshed in
				// place instead of being closed and recreated.
				Assert.IsTrue(hosted.Controller.OpenOrRefresh(
					[CreateItem("sample"), CreateItem("sampler"), CreateItem("sampled")],
					0,
					5));

				CompletionWindow refreshedWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The refreshed completion window was not tracked.");

				Assert.AreSame(firstWindow, refreshedWindow);
				Assert.AreEqual(0, refreshedWindow.StartOffset);
				Assert.AreEqual(5, refreshedWindow.EndOffset);
				Assert.AreEqual(3, refreshedWindow.CompletionList.CompletionData.Count);
				Assert.AreEqual("sampled", refreshedWindow.CompletionList.CompletionData[2].Text);

				// The list was re-filtered for the window's query, so the initial selection is established
				// without the deferred open path. Avalonia divergence: AvaloniaEdit applies that selection
				// asynchronously, so the deferred selection is pumped in before asserting it.
				DispatcherTestUtils.PumpUntilIdle();

				Assert.IsNotNull(refreshedWindow.CompletionList.ListBox.SelectedItem);
			}
		}
	}

	[TestMethod]
	public void OpenOrRefresh_ShiftedEndOffset_UpdatesTheRefreshedWindowRange()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted = CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample")], 0, 5));

				CompletionWindow firstWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				// The replacement end moved while the start stayed anchored: the refreshed window must adopt
				// the new range so the commit replaces the text the user actually typed over.
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample"), CreateItem("sampler")], 0, 4));

				CompletionWindow refreshedWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The refreshed completion window was not tracked.");

				Assert.AreSame(firstWindow, refreshedWindow);
				Assert.AreEqual(4, refreshedWindow.EndOffset);
			}
		}
	}

	[TestMethod]
	public void OpenOrRefresh_ShiftedStartOffset_ReplacesTheWindow()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted = CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample")], 0, 5));

				CompletionWindow firstWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				// A different replacement start starts a new session: the old window is closed and a new one
				// is opened for the new range.
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("pawn")], 1, 5));

				CompletionWindow replacementWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The replacement completion window was not tracked.");

				Assert.AreNotSame(firstWindow, replacementWindow);
				Assert.IsFalse(firstWindow.IsOpen);
				Assert.AreEqual(1, replacementWindow.StartOffset);
				Assert.AreEqual(5, replacementWindow.EndOffset);
				Assert.IsTrue(hosted.Controller.CurrentPresentation.IsListVisible);
			}
		}
	}
}

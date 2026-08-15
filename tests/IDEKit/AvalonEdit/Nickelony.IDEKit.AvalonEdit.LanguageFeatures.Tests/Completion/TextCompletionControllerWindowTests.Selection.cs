using ICSharpCode.AvalonEdit.CodeCompletion;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.IntelliSense.Completion;
using System.Windows.Controls;
using System.Windows.Threading;
using static Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests.CompletionTestHost;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

/// <summary>
/// Covers the completion window's initial-selection policy and the close-when-empty behavior: which item
/// becomes selected for the window query, how preselection overrides the best-match ranking, and when an
/// empty filtered list closes the window.
/// </summary>
public sealed partial class TextCompletionControllerWindowTests
{
	[TestMethod]
	public void ScheduleCloseIfEmpty_WhenListIsEmpty_ClosesWindow()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CompletionTestHost.CreateHostedController(text: "sample");

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample")], 0, 5));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				completionWindow.CompletionList.CompletionData.Clear();
				hosted.Controller.ScheduleCloseIfEmpty();

				DispatcherTestUtils.PumpUntil(() => !hosted.Coordinator.IsWindowOpen);

				Assert.IsFalse(hosted.Controller.CurrentPresentation.IsListVisible);
			}
		}
	}

	[TestMethod]
	public void OpenOrRefresh_PostedInitialSelection_EmptyFilteredList_ClosesTheWindow()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted = CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				// The replacement range spells "sample"; no item matches it, so the posted initial selection
				// empties the filtered list and the automatic close path must close the empty window.
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("zeta")], 0, 6));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				// The initial selection is posted at context-idle priority; pumping below that priority lets
				// the posted close run before the pump exits.
				DispatcherTestUtils.PumpUntil(
					() => !hosted.Coordinator.IsWindowOpen,
					priority: DispatcherPriority.ApplicationIdle);

				Assert.IsFalse(hosted.Controller.CurrentPresentation.IsListVisible);
				Assert.IsFalse(completionWindow.IsVisible);
			}
		}
	}

	[TestMethod]
	public void OpenOrRefresh_CloseWhenEmptyDisabled_KeepsTheEmptyWindowOpen()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CompletionTestHost.CreateHostedController(options: CompletionTestHost.FastOptions with
			{
				CloseWhenEmpty = false
			});

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("zeta")], 0, 6));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				ListBox listBox = completionWindow.CompletionList.ListBox;

				// Wait for the posted context-idle selection to run and filter the list empty; with
				// CloseWhenEmpty disabled the window must stay open instead of closing itself.
				DispatcherTestUtils.PumpUntil(
					() => listBox.Items.Count == 0,
					priority: DispatcherPriority.ApplicationIdle);

				Assert.IsTrue(hosted.Coordinator.IsWindowOpen);
				Assert.IsTrue(hosted.Controller.CurrentPresentation.IsListVisible);
			}
		}
	}

	[TestMethod]
	public void OpenOrRefresh_ScheduledInitialSelection_SelectsTheBestMatchForTheWindowQuery()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted = CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample"), CreateItem("second")], 0, 5));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				// The initial selection is posted at context-idle priority so it applies after the window's
				// layout; pumping below that priority lets the posted selection run before the pump exits.
				DispatcherTestUtils.PumpUntil(
					() => (completionWindow.CompletionList.ListBox.SelectedItem as ICompletionData)?.Text == "sample",
					priority: DispatcherPriority.ApplicationIdle);

				Assert.IsTrue(hosted.Coordinator.IsWindowOpen);
			}
		}
	}

	[TestMethod]
	public void OpenOrRefresh_PreselectedItem_IsSelectedOverTheBestMatch()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted = CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				// The higher priority makes the first item AvalonEdit's best match; the provider's
				// preselection hint on the second item must win over that ranking.
				var items = new ICompletionData[]
				{
					new TextCompletionItemCompletionData(new TextCompletionItem("sample") { Priority = 10.0 }),
					new TextCompletionItemCompletionData(new TextCompletionItem("second") { IsPreselected = true })
				};

				Assert.IsTrue(hosted.Controller.OpenOrRefresh(items, 0, 1));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				// The posted context-idle selection applies the provider's preselection once the window's
				// layout ran; both items match the one-character query, so the preselected one is available.
				DispatcherTestUtils.PumpUntil(
					() => ReferenceEquals(completionWindow.CompletionList.ListBox.SelectedItem, items[1]),
					priority: DispatcherPriority.ApplicationIdle);

				Assert.IsTrue(hosted.Coordinator.IsWindowOpen);
			}
		}
	}

	[TestMethod]
	public void OpenOrRefresh_CustomDataImplementingThePreselectionInterface_IsSelectedOverTheBestMatch()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted = CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				// A host item type opts into preselection through the interface, so the policy is not limited
				// to the package's default completion-data adapter.
				var items = new ICompletionData[]
				{
					new TestCompletionData("sample"),
					new TestCompletionData("second") { IsPreselected = true }
				};

				Assert.IsTrue(hosted.Controller.OpenOrRefresh(items, 0, 1));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				DispatcherTestUtils.PumpUntil(
					() => ReferenceEquals(completionWindow.CompletionList.ListBox.SelectedItem, items[1]),
					priority: DispatcherPriority.ApplicationIdle);

				Assert.IsTrue(hosted.Coordinator.IsWindowOpen);
			}
		}
	}

	[TestMethod]
	public void OpenOrRefresh_PreselectedItemBeyondTheViewport_IsScrolledIntoView()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted = CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				// The window caps at its configured height, so an item at the end of a long list starts outside
				// the visible window; the preselection must scroll it into view instead of selecting an item the
				// user cannot see (AvalonEdit's selected-item setter does not scroll).
				ICompletionData[] items =
				[
					.. Enumerable.Range(0, 60).Select(index => (ICompletionData)new TextCompletionItemCompletionData(
						new TextCompletionItem($"item{index:00}") { IsPreselected = index == 59 }))
				];

				Assert.IsTrue(hosted.Controller.OpenOrRefresh(items, 0, 0));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				ListBox listBox = completionWindow.CompletionList.ListBox;

				DispatcherTestUtils.PumpUntil(
					() => ReferenceEquals(listBox.SelectedItem, items[59]),
					priority: DispatcherPriority.ApplicationIdle);

				completionWindow.UpdateLayout();

				// A realized container for the selected item proves the list scrolled to it: a virtualizing
				// list realizes only the visible items.
				DispatcherTestUtils.PumpUntil(
					() => listBox.ItemContainerGenerator.ContainerFromIndex(59) is not null);
			}
		}
	}
}

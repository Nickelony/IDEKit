#if AVALONIAEDIT
using Avalonia.Controls;
using Avalonia.Threading;
using AvaloniaEdit.CodeCompletion;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;
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
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;
using static Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests.CompletionTestHelpers;
using static Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests.CompletionWindowTestHelpers;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

/// <summary>
/// Covers the completion window's initial-selection policy and the close-when-empty behavior: which item becomes selected for the window query,
/// how preselection overrides the best-match ranking, and when an empty filtered list closes the window.
/// </summary>
public sealed partial class CompletionPresenterWindowTests
{
	[TestMethod]
	public void ScheduleDismissWhenEmpty_WhenListIsEmpty_ClosesWindow()
	{
		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CompletionTestHelpers.CreateHostedController(text: "sample");

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					Assert.IsTrue(hosted.Completion.OpenOrRefresh([CreateItem("sample")], TextRange.FromBounds(0, 5)));

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					completionWindow.CompletionList.CompletionData.Clear();
					hosted.Completion.ScheduleDismissWhenEmpty();

					DispatcherTestHelpers.PumpUntil(() => !hosted.Presenter.IsWindowOpen);

					Assert.IsFalse(hosted.Controller.CurrentPresentation.IsListVisible);
				}
			}
		}
	}

	[TestMethod]
	public void OpenOrRefresh_PostedInitialSelection_EmptyFilteredList_ClosesTheWindow()
	{
		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					// The replacement range spells "sample"; no item matches it, so the posted initial selection
					// empties the filtered list and the automatic close path must close the empty window.
					Assert.IsTrue(hosted.Completion.OpenOrRefresh([CreateItem("zeta")], TextRange.FromBounds(0, 6)));

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					// The initial selection is posted at context-idle priority; pumping below that priority lets
					// the posted close run before the pump exits.
					DispatcherTestHelpers.PumpUntil(
						() => !hosted.Presenter.IsWindowOpen,
						timeout: null,
						priority: DispatcherPriority.ApplicationIdle
					);

					Assert.IsFalse(hosted.Controller.CurrentPresentation.IsListVisible);
					Assert.IsFalse(IsShown(completionWindow));
				}
			}
		}
	}

	[TestMethod]
	public void OpenOrRefresh_DismissWhenEmptyDisabled_KeepsTheEmptyWindowOpen()
	{
		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CompletionTestHelpers.CreateHostedController(
				presentation: CompletionTestHelpers.CreatePresentation(CompletionWindowOptions.Default with { DismissWhenEmpty = false })
			);

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					Assert.IsTrue(hosted.Completion.OpenOrRefresh([CreateItem("zeta")], TextRange.FromBounds(0, 6)));

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					ListBox listBox = completionWindow.CompletionList.ListBox;

					// Wait for the posted context-idle selection to run and filter the list empty; with
					// DismissWhenEmpty disabled the window must stay open instead of closing itself.
					DispatcherTestHelpers.PumpUntil(() => listBox.Items.Count == 0, priority: DispatcherPriority.ApplicationIdle);

					Assert.IsTrue(hosted.Presenter.IsWindowOpen);
					Assert.IsTrue(hosted.Controller.CurrentPresentation.IsListVisible);
				}
			}
		}
	}

	[TestMethod]
	public void OpenOrRefresh_ScheduledInitialSelection_SelectsTheBestMatchForTheWindowQuery()
	{
		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					Assert.IsTrue(hosted.Completion.OpenOrRefresh([CreateItem("sample"), CreateItem("second")], TextRange.FromBounds(0, 5)));

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					// The initial selection is posted at context-idle priority so it applies after the window's
					// layout; pumping below that priority lets the posted selection run before the pump exits.
					DispatcherTestHelpers.PumpUntil(
						() => completionWindow.CompletionList.ListBox.SelectedItem is ICompletionData { Text: "sample" },
						timeout: null,
						priority: DispatcherPriority.ApplicationIdle
					);

					Assert.IsTrue(hosted.Presenter.IsWindowOpen);
				}
			}
		}
	}

	[TestMethod]
	public void OpenOrRefresh_PreselectedItem_IsSelectedOverTheBestMatch()
	{
		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					// The higher priority makes the first item the engine's best match; the provider's
					// preselection hint on the second item is the policy's preferred selection.
					var items = new ICompletionData[]
					{
						new CompletionItemAdapter(new CompletionItem("sample") { Priority = 10.0 }),
						new CompletionItemAdapter(new CompletionItem("second") { IsPreselected = true }),
					};

					Assert.IsTrue(hosted.Completion.OpenOrRefresh(items, TextRange.FromBounds(0, 1)));

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					AssertOpenPathPreselection(hosted.Presenter, completionWindow, items[1]);
				}
			}
		}
	}

	[TestMethod]
	public void OpenOrRefresh_CustomDataImplementingThePreselectionInterface_IsSelectedOverTheBestMatch()
	{
		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					// A host item type opts into preselection through the interface, so the policy is not limited
					// to the package's default completion-data adapter.
					var items = new ICompletionData[]
					{
						new StubCompletionData("sample"),
						new StubCompletionData("second") { IsPreselected = true },
					};

					Assert.IsTrue(hosted.Completion.OpenOrRefresh(items, TextRange.FromBounds(0, 1)));

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					AssertOpenPathPreselection(hosted.Presenter, completionWindow, items[1]);
				}
			}
		}
	}

	[TestMethod]
	public void OpenOrRefresh_PreselectedItemBeyondTheViewport_IsScrolledIntoView()
	{
		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					// The window caps at its configured height, so an item at the end of a long list starts outside
					// the visible window; the preselection must scroll it into view instead of selecting an item the
					// user cannot see (the engine's selected-item setter does not scroll).
					ICompletionData[] items =
					[
						.. Enumerable
							.Range(0, 60)
							.Select(index =>
								(ICompletionData)new CompletionItemAdapter(new CompletionItem($"item{index:00}") { IsPreselected = index == 59 })
							),
					];

					Assert.IsTrue(hosted.Completion.OpenOrRefresh(items, TextRange.FromBounds(0, 0)));

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					AssertPreselectedItemScrolledIntoView(presenter: hosted.Presenter, window: completionWindow, items, preselectedIndex: 59);
				}
			}
		}
	}
}

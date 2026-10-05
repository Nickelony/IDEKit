using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;
#if AVALONIAEDIT
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.Core.Text;
using static Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests.CompletionTestHelpers;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.Core.Text;
using static Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests.CompletionTestHelpers;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

/// <summary>
/// Covers the refresh-path policy of <see cref="CompletionController"/>: the empty-list close
/// rule honors <see cref="CompletionWindowOptions.DismissWhenEmpty"/> on the refresh path too, and the posted initial selection tolerates a document
/// that was edited after the window was opened.
/// </summary>
[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed class CompletionPresenterRefreshPolicyTests
{
	[TestMethod]
	public void OpenOrRefresh_RefreshEmptiesTheListWithDismissWhenEmptyDisabled_KeepsTheWindowOpen()
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
					Assert.IsTrue(hosted.Completion.OpenOrRefresh([CreateItem("sample")], TextRange.FromBounds(0, 6)));

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					// The replacement range still spells "sample"; the refreshed item set matches nothing, so
					// the refresh empties the filtered list.
					Assert.IsTrue(hosted.Completion.OpenOrRefresh([CreateItem("zeta")], TextRange.FromBounds(0, 6)));

					// DismissWhenEmpty is disabled, so the refresh keeps the emptied window instead of closing it
					// like the default policy does (covered by the open-path and refresh-path close tests).
					Assert.IsTrue(hosted.Presenter.IsWindowOpen);
					Assert.IsTrue(hosted.Controller.CurrentPresentation.IsListVisible);
					Assert.AreSame(completionWindow, hosted.Presenter.ActiveWindow);
				}
			}
		}
	}

	[TestMethod]
	public void OpenOrRefresh_UnchangedQuery_SwapsTheVisibleItemSet()
	{
		(
			TextEditor Editor,
			CompletionController Controller,
			CompletionWindowPresenter Presenter,
			HostWindow HostWindow,
			CompletionPresenter Completion
		) hosted = CreateHostedEditorController(text: "sa");

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					Assert.IsTrue(hosted.Completion.OpenOrRefresh([CreateItem("sample")], TextRange.FromBounds(0, 2)));

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					DispatcherTestHelpers.PumpUntil(() => GetVisibleItemTexts(completionWindow).Count == 1);

					// The refreshed set matches the same query ("sa"), so the completion list would early-return
					// on the query it already applied; the in-place refresh must invalidate that memoized query so
					// the replaced item set is displayed instead of the previous one.
					Assert.IsTrue(hosted.Completion.OpenOrRefresh([CreateItem("sand")], TextRange.FromBounds(0, 2)));

					DispatcherTestHelpers.PumpUntil(() => GetVisibleItemTexts(completionWindow).Contains("sand"));

					Assert.AreSequenceEqual(
						["sand"],
						GetVisibleItemTexts(completionWindow),
						Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder
					);
				}
			}
		}
	}

	private static List<string> GetVisibleItemTexts(CompletionWindow completionWindow) =>
		[.. completionWindow.CompletionList.ListBox.Items.Cast<ICompletionData>().Select(item => item.Text)];

	[TestMethod]
	public void OpenOrRefresh_DocumentEditedBeforeInitialSelection_KeepsTheWindowCoherent()
	{
		(
			TextEditor Editor,
			CompletionController Controller,
			CompletionWindowPresenter Presenter,
			HostWindow HostWindow,
			CompletionPresenter Completion
		) hosted = CreateHostedEditorController(text: "sample");

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					Assert.IsTrue(hosted.Completion.OpenOrRefresh([CreateItem("sample")], TextRange.FromBounds(0, 6)));

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					// The document is edited before the posted initial selection runs, so the window performs
					// its query against a document whose offsets have shifted since it was opened; the
					// query and selection must stay coherent instead of reading out of range.
					hosted.Editor.Document.Remove(4, 2);

					DispatcherTestHelpers.PumpUntil(
						() => completionWindow.CompletionList.ListBox.SelectedItem is not null,
						priority: DispatcherPriority.ApplicationIdle
					);

					Assert.IsTrue(hosted.Presenter.IsWindowOpen);
					Assert.IsTrue(hosted.Controller.CurrentPresentation.IsListVisible);
				}
			}
		}
	}
}

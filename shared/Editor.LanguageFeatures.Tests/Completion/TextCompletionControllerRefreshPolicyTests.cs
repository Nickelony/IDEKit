#if AVALONIAEDIT
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using static Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests.CompletionTestHost;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;
#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using System.Windows.Threading;
using static Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests.CompletionTestHost;
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;
#endif

/// <summary>
/// Covers the refresh-path policy of <see cref="TextCompletionController"/>: the empty-list close rule
/// honors <see cref="TextCompletionControllerOptions.CloseWhenEmpty"/> on the refresh path too, and the
/// posted initial selection tolerates a document that was edited after the window was opened.
/// </summary>
[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed class TextCompletionControllerRefreshPolicyTests
{
	[TestMethod]
	public void OpenOrRefresh_RefreshEmptiesTheListWithCloseWhenEmptyDisabled_KeepsTheWindowOpen()
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
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample")], 0, 6));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				// The replacement range still spells "sample"; the refreshed item set matches nothing, so
				// the refresh empties the filtered list.
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("zeta")], 0, 6));

				// CloseWhenEmpty is disabled, so the refresh keeps the emptied window instead of closing it
				// like the default policy does (covered by the open-path and refresh-path close tests).
				Assert.IsTrue(hosted.Coordinator.IsWindowOpen);
				Assert.IsTrue(hosted.Controller.CurrentPresentation.IsListVisible);
				Assert.AreSame(completionWindow, hosted.Coordinator.ActiveWindow);
			}
		}
	}

	[TestMethod]
	public void OpenOrRefresh_DocumentEditedBeforeInitialSelection_KeepsTheWindowCoherent()
	{
		(TextEditor Editor, TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CreateHostedEditorController(text: "sample");

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample")], 0, 6));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				// The document is edited before the posted initial selection runs, so the window performs
				// its query against a document that no longer carries the offsets it was opened with; the
				// query and selection must stay coherent instead of reading out of range.
				hosted.Editor.Document.Remove(4, 2);

				DispatcherTestUtils.PumpUntil(
					() => completionWindow.CompletionList.ListBox.SelectedItem is not null,
					priority: DispatcherPriority.ApplicationIdle);

				Assert.IsTrue(hosted.Coordinator.IsWindowOpen);
				Assert.IsTrue(hosted.Controller.CurrentPresentation.IsListVisible);
			}
		}
	}
}

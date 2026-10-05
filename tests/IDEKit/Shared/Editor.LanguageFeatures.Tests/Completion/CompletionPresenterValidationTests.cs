using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;
#if AVALONIAEDIT
using Avalonia;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using System.Windows;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

/// <summary>Verifies the argument validation of the completion presenter, its presentation options and the completion themes.</summary>
[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed class CompletionPresenterValidationTests
{
	[TestMethod]
	public void PresentationOptions_NullMembers_ThrowAtAssignment()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => CompletionPresentationOptions.Default with { WindowTheme = null! });
		Assert.ThrowsExactly<ArgumentNullException>(() => CompletionPresentationOptions.Default with { WindowOptions = null! });
#if !AVALONIAEDIT
		Assert.ThrowsExactly<ArgumentNullException>(() => CompletionPresentationOptions.Default with { TooltipTheme = null! });
#endif
	}

	[TestMethod]
	public void PresentationOptions_Default_PinsTheDocumentedValues()
	{
		// The defaults are pinned so the option values and their XML docs cannot drift apart.
		CompletionPresentationOptions options = CompletionPresentationOptions.Default;
		Assert.IsTrue(options.AcceptOnCommitCharacters);
#if !AVALONIAEDIT
		Assert.AreEqual(TimeSpan.FromMilliseconds(120.0), options.TooltipResolveDelay);
#endif
	}

#if !AVALONIAEDIT
	[TestMethod]
	public void PresentationOptions_NegativeTooltipResolveDelay_ThrowsAtAssignment()
	{
		ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			CompletionPresentationOptions.Default with
			{
				TooltipResolveDelay = TimeSpan.FromMilliseconds(-1.0),
			}
		);

		Assert.AreEqual("TooltipResolveDelay", exception.ParamName);
	}

	[TestMethod]
	public void PresentationOptions_ZeroTooltipResolveDelay_IsAccepted()
	{
		CompletionPresentationOptions options = CompletionPresentationOptions.Default with { TooltipResolveDelay = TimeSpan.Zero };
		Assert.AreEqual(TimeSpan.Zero, options.TooltipResolveDelay);
	}
#endif

	[TestMethod]
	public void Constructor_MaxWidthBelowMinimumContentWidthPlusChrome_ThrowsArgumentOutOfRangeException()
	{
		// The relationship between the content-space floor and the window-space maximum can only be checked
		// when the controller is created; a maximum that cannot hold the floor plus the chrome is rejected.
		CompletionPresentationOptions presentation = CompletionTestHelpers.CreatePresentation(
			CompletionWindowOptions.Default with
			{
				MinContentWidth = 500,
				MaxWidth = 400,
			}
		);

		ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CreateController(presentation));

		Assert.AreEqual("presentation", exception.ParamName);
	}

	[TestMethod]
	public void Constructor_MaxWidthBelowChromeInclusiveFloor_ThrowsEvenWhenLargerThanMinContentWidth()
	{
		// 920 is larger than the 900 content floor but too small for it plus the default window chrome
		// (CompletionWindowOptions.HorizontalChrome); the cross-space relationship is what makes
		// the record impossible, not the raw ordering of the values.
		CompletionPresentationOptions presentation = CompletionTestHelpers.CreatePresentation(
			CompletionWindowOptions.Default with
			{
				MinContentWidth = 900,
				MaxWidth = 920,
			}
		);

		ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CreateController(presentation));

		Assert.AreEqual("presentation", exception.ParamName);
	}

	[TestMethod]
	public void Constructor_OffEditorThread_ThrowsInvalidOperationException()
	{
		TextEditor editor = CompletionTestHelpers.CreateEditor();

		// The controller verifies the editor thread up front, so constructing it elsewhere fails fast with
		// the dispatcher's threading error instead of misbehaving later.
		Exception? captured = TestThreads.CaptureFailure(() => _ = CompletionTestHelpers.CreateController(editor));

		Assert.IsInstanceOfType<InvalidOperationException>(captured);
	}

	[TestMethod]
	public void OpenOrRefresh_NullItems_ThrowsArgumentNullException()
	{
		CompletionTestHelpers.CompletionSession session = CreateController();

		using (session)
		{
			ArgumentNullException exception = Assert.ThrowsExactly<ArgumentNullException>(() =>
				session.Completion.OpenOrRefresh(null!, TextRange.FromBounds(0, 5))
			);

			Assert.AreEqual("items", exception.ParamName);
		}
	}

	[TestMethod]
	public void OpenOrRefresh_OffsetsBeyondDocumentEnd_AreClampedToTheDocument()
	{
		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CompletionTestHelpers.CreateHostedController(text: "sample");

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					// The document has six characters; the replacement range is clamped instead of reaching
					// past the document end.
					Assert.IsTrue(hosted.Completion.OpenOrRefresh([new StubCompletionData("sample")], TextRange.FromBounds(3, 99)));

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					Assert.AreEqual(3, completionWindow.StartOffset);
					Assert.AreEqual(6, completionWindow.EndOffset);
				}
			}
		}
	}

	[TestMethod]
	public void OpenOrRefresh_RangeBeyondTheDocument_IsAcceptedAsAClampedEmptyRange()
	{
		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CompletionTestHelpers.CreateHostedController(text: "sample");

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					Assert.IsTrue(hosted.Completion.OpenOrRefresh([new StubCompletionData("sample")], TextRange.FromBounds(60, 100)));

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					Assert.AreEqual(6, completionWindow.StartOffset);
					Assert.AreEqual(6, completionWindow.EndOffset);
				}
			}
		}
	}

	[TestMethod]
	public void CompletionItemAdapter_NullItem_ThrowsArgumentNullException()
	{
		ArgumentNullException exception = Assert.ThrowsExactly<ArgumentNullException>(() => new CompletionItemAdapter(null!));
		Assert.AreEqual("item", exception.ParamName);
	}

	[TestMethod]
	public void Constructor_NullTextArea_ThrowsArgumentNullException()
	{
		ArgumentNullException exception = Assert.ThrowsExactly<ArgumentNullException>(() =>
			new CompletionPresenter(textArea: null!, presentation: CompletionTestHelpers.CreatePresentation())
		);

		Assert.AreEqual("textArea", exception.ParamName);
	}

	[TestMethod]
	public void Constructor_WithoutPresentation_UsesTheDefaultPresentation()
	{
		TextEditor editor = CompletionTestHelpers.CreateEditor();

		using var completion = new CompletionPresenter(editor.TextArea);

		Assert.AreEqual(CompletionPresentationOptions.Default.WindowTheme, completion.WindowPresenter.Theme);
	}

	private static CompletionTestHelpers.CompletionSession CreateController(CompletionPresentationOptions? presentation = null)
	{
		TextEditor editor = CompletionTestHelpers.CreateEditor();
		return CompletionTestHelpers.CreateController(editor, presentation: presentation);
	}
}

#if AVALONIAEDIT
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;
using static Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests.CompletionTestHelpers;
using static Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests.CompletionWindowTestHelpers;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
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
/// Covers the completion window's sizing and host-configuration hooks; the initial-selection policy lives
/// in the <c>CompletionPresenterWindowTests.Selection.cs</c> partial, and each binding's <c>CompletionPresenterWindowTests.Host.cs</c> partial holds
/// the host-composition branches.
/// </summary>
[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed partial class CompletionPresenterWindowTests
{
	[TestMethod]
	public void WindowTheme_AssignedAfterConstruction_ChromesTheNextWindow()
	{
		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					var theme = new CompletionWindowTheme
					{
						Background = Brushes.Navy,
						Foreground = Brushes.Yellow,
						BorderBrush = Brushes.Red,
					};

					// A theme switch assigns the theme in place, so the presenter and its session state survive it.
					hosted.Completion.WindowTheme = theme;

					Assert.IsTrue(hosted.Completion.OpenOrRefresh([CreateItem("sample")], TextRange.FromBounds(0, 5)));

					CompletionWindow window =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					Assert.AreSame(theme, hosted.Completion.WindowTheme);
					Assert.AreSame(Brushes.Navy, GetFrameElement(window).Background);
					Assert.AreSame(Brushes.Red, GetFrameElement(window).BorderBrush);
				}
			}
		}
	}

	[TestMethod]
	public void WindowTheme_NullValue_ThrowsArgumentNullException()
	{
		TextEditor editor = CreateEditor();

		using CompletionSession session = CreateController(editor);

		Assert.ThrowsExactly<ArgumentNullException>(() => session.Completion.WindowTheme = null!);
	}

	[TestMethod]
	public void OpenOrRefresh_SizesWindowToContentWithinConfiguredCap()
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

					CompletionWindow smallWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					CompletionWindowOptions defaultOptions = CompletionWindowOptions.Default;

					// The completion window sizes to its content: a single item stays below the content floor and
					// the effective floor is the content minimum plus the window chrome.
					AssertSizedToContent(smallWindow, defaultOptions);

					Assert.AreEqual(defaultOptions.MinContentWidth + defaultOptions.HorizontalChrome, GetFrameElement(smallWindow).Width);

					ICompletionData[] tallItemSet = [.. Enumerable.Range(0, 60).Select(index => CreateItem($"pawn{index}"))];

					// The replacement start moves, so the presenter opens a replacement window instead of
					// refreshing the open one in place.
					Assert.IsTrue(hosted.Completion.OpenOrRefresh(tallItemSet, TextRange.FromBounds(1, 5)));

					CompletionWindow tallWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The replacement completion window was not tracked.");

					Assert.AreNotSame(smallWindow, tallWindow);

					// A tall item set grows towards the configured cap instead of exceeding it.
					AssertGrowsTowardsCap(tallWindow, smallWindow, defaultOptions);
				}
			}
		}
	}

	[TestMethod]
	public void OpenOrRefresh_ThrowingConfigureWindow_ClosesCreatedWindowAndKeepsStateClosed()
	{
		(TextEditor editor, HostWindow hostWindow) = TestHost.ShowHostedEditor("sample");

		using (hostWindow)
		{
			using CompletionTestHelpers.CompletionSession session = CompletionTestHelpers.CreateController(
				editor,
				hooks: new CompletionPresenterHooks { ConfigureWindow = _ => throw new InvalidOperationException("Window configuration failed.") }
			);

			Assert.ThrowsExactly<InvalidOperationException>(() =>
				session.Completion.OpenOrRefresh([CreateItem("sample")], TextRange.FromBounds(0, 6))
			);

			// The created window must not be stranded or tracked after the hook failure.
			Assert.IsNull(session.Completion.WindowPresenter.ActiveWindow);
			Assert.IsFalse(session.Completion.WindowPresenter.IsWindowOpen);
			Assert.IsFalse(session.Controller.CurrentPresentation.IsListVisible);
		}
	}

#if !AVALONIAEDIT
	[TestMethod]
	public void OpenOrRefresh_ThrowingConfigureTooltip_ClosesCreatedWindowAndKeepsStateClosed()
	{
		(TextEditor editor, HostWindow hostWindow) = TestHost.ShowHostedEditor("sample");

		using (hostWindow)
		{
			using CompletionTestHelpers.CompletionSession session = CompletionTestHelpers.CreateController(
				editor,
				hooks: new CompletionPresenterHooks { ConfigureTooltip = _ => throw new InvalidOperationException("Tooltip configuration failed.") }
			);

			// The tooltip hook runs inside the same rollback scope as the window hook.
			Assert.ThrowsExactly<InvalidOperationException>(() =>
				session.Completion.OpenOrRefresh([CreateItem("sample")], TextRange.FromBounds(0, 6))
			);

			Assert.IsNull(session.Completion.WindowPresenter.ActiveWindow);
			Assert.IsFalse(session.Completion.WindowPresenter.IsWindowOpen);
			Assert.IsFalse(session.Controller.CurrentPresentation.IsListVisible);
		}
	}
#endif

	[TestMethod]
	public void OpenOrRefresh_ConfigureWindowHook_RunsAfterSizingSoItCanPinTheWidth()
	{
		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CompletionTestHelpers.CreateHostedController(
				hooks: new CompletionPresenterHooks { ConfigureWindow = window => GetFrameElement(window).Width = 333.0 }
			);

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					Assert.IsTrue(hosted.Completion.OpenOrRefresh([CreateItem("sample")], TextRange.FromBounds(0, 5)));

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					// The hook runs after the presenter sized the window, so the pinned width survives.
					Assert.AreEqual(333.0, GetFrameElement(completionWindow).Width);
				}
			}
		}
	}

	[TestMethod]
	public void OpenOrRefresh_ConfigureWindowHook_CanConfigureTheWindow()
	{
		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CompletionTestHelpers.CreateHostedController(
				hooks: new CompletionPresenterHooks { ConfigureWindow = window => GetFrameElement(window).FontSize = 42.0 }
			);

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					Assert.IsTrue(hosted.Completion.OpenOrRefresh([CreateItem("sample")], TextRange.FromBounds(0, 5)));

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					// The successful configuration path leaves the host's customizations in place.
					Assert.AreEqual(42.0, GetFrameElement(completionWindow).FontSize);
				}
			}
		}
	}

	[TestMethod]
	public void OpenOrRefresh_CustomMeasureItemWidth_ControlsWindowWidth()
	{
		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CompletionTestHelpers.CreateHostedController(hooks: new CompletionPresenterHooks { MeasureItemWidth = _ => 1000.0 });

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					Assert.IsTrue(hosted.Completion.OpenOrRefresh([CreateItem("sample")], TextRange.FromBounds(0, 5)));

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					CompletionWindowOptions options = CompletionWindowOptions.Default;

					// The custom measurement exceeds the default floor but is still clamped by the maximum width.
					Assert.AreEqual((double)options.MaxWidth, GetFrameElement(completionWindow).Width);
				}
			}
		}
	}

	[TestMethod]
	public void OpenOrRefresh_CustomDisplayInfo_IsUsedForWidthMeasurement()
	{
		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CompletionTestHelpers.CreateHostedController(
				hooks: new CompletionPresenterHooks { GetDisplayInfo = _ => ("sample", "a very long detail that widens the window considerably") },

				presentation: CompletionTestHelpers.CreatePresentation(
					CompletionWindowOptions.Default with
					{
						MinContentWidth = 100.0,
						ItemIconWidth = 0.0,
						ItemDetailSpacing = 0.0,
					}
				)
			);

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					Assert.IsTrue(hosted.Completion.OpenOrRefresh([CreateItem("sample")], TextRange.FromBounds(0, 5)));

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					// The long detail widens the window beyond the 100-pixel content floor, so the measured detail
					// demonstrably drives the width instead of the floor.
					Assert.IsTrue(GetFrameElement(completionWindow).Width > CompletionWindowOptions.Default.HorizontalChrome + 100.0);
				}
			}
		}
	}
}

#if AVALONIAEDIT
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.IntelliSense.Completion;
using static Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests.CompletionTestHost;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;
#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.IntelliSense.Completion;
using static Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests.CompletionTestHost;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;
#endif

/// <summary>
/// Covers the completion window's sizing and host-configuration hooks; the initial-selection policy and
/// the host-composition branches live in the <c>TextCompletionControllerWindowTests.Selection.cs</c> and
/// <c>TextCompletionControllerWindowTests.Host.cs</c> partials.
/// </summary>
[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed partial class TextCompletionControllerWindowTests
{
	[TestMethod]
	public void OpenOrRefresh_SizesWindowToContentWithinConfiguredCap()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted = CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample")], 0, 5));

				CompletionWindow smallWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

#if AVALONIAEDIT
				// The completion window sizes to its content: a single item stays below the content floor and
				// the effective floor is the content minimum plus the window chrome. Avalonia divergence: an
				// the editor completion window is a borderless popup whose chrome and height cap live on its
				// completion list, and the headless host does not measure the popup, so the reference's
				// measured-height assertions (ActualHeight) have no deterministic counterpart; the mirror pins
				// the width floor and the height cap instead.
#else
				smallWindow.UpdateLayout();

				// The window is content-sized: a single item stays below the cap...
				Assert.IsTrue(smallWindow.ActualHeight > 0.0, "The completion window must be measured for this test.");
				Assert.IsTrue(smallWindow.ActualHeight < smallWindow.MaxHeight, "A single item must stay below the cap.");

				// ...and the effective floor is the content minimum plus the window chrome.
#endif
				TextCompletionControllerOptions defaultOptions = TextCompletionControllerOptions.Default;

				Assert.AreEqual(
					defaultOptions.WindowMinContentWidth + defaultOptions.WindowHorizontalChrome,
#if AVALONIAEDIT
					smallWindow.CompletionList.Width);
				Assert.AreEqual(defaultOptions.WindowMaxHeight, smallWindow.CompletionList.MaxHeight);
#else
					smallWindow.Width);
#endif

				ICompletionData[] tallItemSet = [.. Enumerable.Range(0, 60).Select(index => CreateItem($"pawn{index}"))];

				// The replacement start moves, so the controller opens a replacement window instead of
				// refreshing the open one in place.
				Assert.IsTrue(hosted.Controller.OpenOrRefresh(tallItemSet, 1, 5));

				CompletionWindow tallWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The replacement completion window was not tracked.");

				Assert.AreNotSame(smallWindow, tallWindow);

#if !AVALONIAEDIT
				tallWindow.UpdateLayout();

#endif
				// A tall item set grows towards the configured cap instead of exceeding it.
#if AVALONIAEDIT
				Assert.AreEqual(defaultOptions.WindowMaxHeight, tallWindow.CompletionList.MaxHeight);
#else
				Assert.AreEqual((double)defaultOptions.WindowMaxHeight, tallWindow.MaxHeight);
				Assert.IsTrue(tallWindow.ActualHeight <= tallWindow.MaxHeight, "A tall window must not exceed the cap.");
				Assert.IsTrue(
					tallWindow.ActualHeight > smallWindow.ActualHeight,
					"The premise: both windows were measured, so the taller item set grows the window.");
#endif
			}
		}
	}

	[TestMethod]
	public void OpenOrRefresh_ThrowingConfigureWindow_ClosesCreatedWindowAndKeepsStateClosed()
	{
		(TextEditor editor, HostWindow hostWindow) = TestHost.ShowHostedEditor("sample");

		using (hostWindow)
		{
			using var controller = CompletionTestHost.CreateController(
				editor,
				hooks: new TextCompletionControllerHooks
				{
					ConfigureWindow = _ => throw new InvalidOperationException("Window configuration failed.")
				});

			Assert.ThrowsExactly<InvalidOperationException>(
				() => controller.OpenOrRefresh([CreateItem("sample")], 0, 6));

			// The created window must not be stranded or tracked after the hook failure.
			Assert.IsNull(controller.WindowCoordinator.ActiveWindow);
			Assert.IsFalse(controller.WindowCoordinator.IsWindowOpen);
			Assert.IsFalse(controller.CurrentPresentation.IsListVisible);
		}
	}

	[TestMethod]
	public void OpenOrRefresh_ThrowingConfigureTooltip_ClosesCreatedWindowAndKeepsStateClosed()
	{
		(TextEditor editor, HostWindow hostWindow) = TestHost.ShowHostedEditor("sample");

		using (hostWindow)
		{
			using var controller = CompletionTestHost.CreateController(
				editor,
				hooks: new TextCompletionControllerHooks
				{
					ConfigureTooltip = _ => throw new InvalidOperationException("Tooltip configuration failed.")
				});

#if AVALONIAEDIT
			// The tooltip hook runs inside the same rollback scope as the window hook. Avalonia divergence:
			// the editor exposes no tooltip member, so the controller never resolves a tooltip and the
			// tooltip hook is never invoked; a throwing hook therefore cannot fail the open. The closest
			// behavioral assertion is that the open succeeds and the window stays tracked instead of being
			// rolled back by a hook that never ran.
			Assert.IsTrue(controller.OpenOrRefresh([CreateItem("sample")], 0, 6));
#else
			// The tooltip hook runs inside the same rollback scope as the window hook.
			Assert.ThrowsExactly<InvalidOperationException>(
				() => controller.OpenOrRefresh([CreateItem("sample")], 0, 6));
#endif

#if AVALONIAEDIT
			Assert.IsNotNull(controller.WindowCoordinator.ActiveWindow);
			Assert.IsTrue(controller.WindowCoordinator.IsWindowOpen);
			Assert.IsTrue(controller.CurrentPresentation.IsListVisible);
#else
			Assert.IsNull(controller.WindowCoordinator.ActiveWindow);
			Assert.IsFalse(controller.WindowCoordinator.IsWindowOpen);
			Assert.IsFalse(controller.CurrentPresentation.IsListVisible);
#endif
		}
	}

	[TestMethod]
	public void OpenOrRefresh_ConfigureWindowHook_RunsAfterSizingSoItCanPinTheWidth()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CompletionTestHost.CreateHostedController(hooks: new TextCompletionControllerHooks
			{
#if AVALONIAEDIT
				// An the editor completion window's chrome lives on its completion list, so the hook pins the
				// list's width; the reference pinned the window width, which the popup does not carry.
				ConfigureWindow = window => window.CompletionList.Width = 333.0
#else
				ConfigureWindow = window => window.Width = 333.0
#endif
			});

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample")], 0, 5));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				// The hook runs after the controller sized the window, so the pinned width survives.
#if AVALONIAEDIT
				Assert.AreEqual(333.0, completionWindow.CompletionList.Width);
#else
				Assert.AreEqual(333.0, completionWindow.Width);
#endif
			}
		}
	}

	[TestMethod]
	public void OpenOrRefresh_ConfigureWindowHook_CanConfigureTheWindow()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CompletionTestHost.CreateHostedController(hooks: new TextCompletionControllerHooks
			{
#if AVALONIAEDIT
				// The font lives on the completion list that renders the items; a popup carries no font.
				ConfigureWindow = window => window.CompletionList.FontSize = 42.0
#else
				ConfigureWindow = window => window.FontSize = 42.0
#endif
			});

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample")], 0, 5));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				// The successful configuration path leaves the host's customizations in place.
#if AVALONIAEDIT
				Assert.AreEqual(42.0, completionWindow.CompletionList.FontSize);
#else
				Assert.AreEqual(42.0, completionWindow.FontSize);
#endif
			}
		}
	}

	[TestMethod]
	public void OpenOrRefresh_CustomMeasureItemWidth_ControlsWindowWidth()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CompletionTestHost.CreateHostedController(hooks: new TextCompletionControllerHooks
			{
				MeasureItemWidth = _ => 1000.0
			});

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample")], 0, 5));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				TextCompletionControllerOptions options = TextCompletionControllerOptions.Default;

				// The custom measurement exceeds the default floor but is still clamped by the maximum width.
#if AVALONIAEDIT
				Assert.AreEqual((double)options.WindowMaxWidth, completionWindow.CompletionList.Width);
#else
				Assert.AreEqual((double)options.WindowMaxWidth, completionWindow.Width);
#endif
			}
		}
	}

	[TestMethod]
	public void OpenOrRefresh_CustomDisplayInfo_IsUsedForWidthMeasurement()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CompletionTestHost.CreateHostedController(
				hooks: new TextCompletionControllerHooks
				{
					GetDisplayInfo = _ => ("sample", "a very long detail that widens the window considerably")
				},
				options: TextCompletionControllerOptions.Default with
				{
					WindowMinContentWidth = 100.0,
					ItemIconWidth = 0.0,
					ItemDetailSpacing = 0.0
				});

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample")], 0, 5));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				// The long detail widens the window beyond the 100-pixel content floor, so the measured detail
				// demonstrably drives the width instead of the floor.
				Assert.IsTrue(
#if AVALONIAEDIT
					completionWindow.CompletionList.Width > TextCompletionControllerOptions.Default.WindowHorizontalChrome + 100.0);
#else
					completionWindow.Width > TextCompletionControllerOptions.Default.WindowHorizontalChrome + 100.0);
#endif
			}
		}
	}
}

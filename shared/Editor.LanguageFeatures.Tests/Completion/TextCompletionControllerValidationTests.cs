#if AVALONIAEDIT
using Avalonia;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;
#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using System.Windows;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;
#endif

/// <summary>
/// Verifies the argument validation of the completion controller options and of the completion skins.
/// </summary>
[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed class TextCompletionControllerValidationTests
{
	[TestMethod]
	[DataRow("WindowMaxHeight", -1.0)]
	[DataRow("WindowMinContentWidth", -1.0)]
	[DataRow("WindowMaxWidth", -1.0)]
	[DataRow("WindowHorizontalChrome", -0.5)]
	[DataRow("ItemIconWidth", -0.5)]
	[DataRow("ItemDetailSpacing", -0.25)]
	public void Options_NegativeValue_IsRejectedAtAssignment(string propertyName, double value)
	{
		// The options validate themselves, so the offending assignment fails before a controller exists.
		var exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AssignNumericOption(propertyName, value));

		Assert.AreEqual(propertyName, exception.ParamName);
	}

	[TestMethod]
	public void Options_NonFiniteValue_IsRejectedAtAssignment()
	{
		var minWidthException = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
			() => TextCompletionControllerOptions.Default with { WindowMinContentWidth = double.NaN });
		Assert.AreEqual("WindowMinContentWidth", minWidthException.ParamName);

		var maxHeightException = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
			() => TextCompletionControllerOptions.Default with { WindowMaxHeight = double.PositiveInfinity });
		Assert.AreEqual("WindowMaxHeight", maxHeightException.ParamName);

		var chromeException = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
			() => TextCompletionControllerOptions.Default with { WindowHorizontalChrome = double.NaN });
		Assert.AreEqual("WindowHorizontalChrome", chromeException.ParamName);
	}

	[TestMethod]
	public void Options_NegativeDelay_IsRejectedAtAssignment()
	{
		var requestException = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
			() => TextCompletionControllerOptions.Default with { RequestDebounceDelay = TimeSpan.FromMilliseconds(-1.0) });
		Assert.AreEqual("RequestDebounceDelay", requestException.ParamName);

		var tooltipException = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
			() => TextCompletionControllerOptions.Default with { TooltipResolveDelay = TimeSpan.FromMilliseconds(-1.0) });
		Assert.AreEqual("TooltipResolveDelay", tooltipException.ParamName);
	}

	[TestMethod]
	public void Constructor_WindowMaxWidthBelowMinimumContentWidthPlusChrome_ThrowsArgumentOutOfRangeException()
	{
		// The relationship between the content-space floor and the window-space maximum can only be checked
		// when the controller is created; a maximum that cannot hold the floor plus the chrome is rejected.
		var options = TextCompletionControllerOptions.Default with { WindowMinContentWidth = 500, WindowMaxWidth = 400 };
		var exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
			() => CreateController(options));

		Assert.AreEqual("options", exception.ParamName);
	}

	[TestMethod]
	public void Constructor_WindowMaxWidthBelowChromeInclusiveFloor_ThrowsEvenWhenLargerThanMinContentWidth()
	{
		// 920 would already have been too small for a 900 content floor plus the 52-pixel chrome; the
		// cross-space relationship is what makes the record impossible, not the raw ordering of the values.
		var options = TextCompletionControllerOptions.Default with { WindowMinContentWidth = 900, WindowMaxWidth = 920 };
		var exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
			() => CreateController(options));

		Assert.AreEqual("options", exception.ParamName);
	}

	[TestMethod]
	public void TooltipSkin_NonFiniteOffset_IsRejectedAtAssignment()
	{
		// The tooltip skin validates itself, so the failure site is the with-expression instead of the
		// controller constructor, and the exception names the offending property rather than the argument
		// the skin happened to travel in.
		var exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
			() => CompletionTooltipSkin.Default with { HorizontalOffset = double.PositiveInfinity });

		Assert.AreEqual("HorizontalOffset", exception.ParamName);
	}

	[TestMethod]
	public void TooltipSkin_NegativeThickness_IsRejectedAtAssignment()
	{
		var borderException = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
			() => CompletionTooltipSkin.Default with { BorderThickness = new Thickness(-1.0) });
		Assert.AreEqual("BorderThickness", borderException.ParamName);

		var paddingException = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
			() => CompletionTooltipSkin.Default with { Padding = new Thickness(double.NaN) });
		Assert.AreEqual("Padding", paddingException.ParamName);
	}

	[TestMethod]
	public void Constructor_OffEditorThread_ThrowsInvalidOperationException()
	{
#if !AVALONIAEDIT
		TextEditor editor = CompletionTestHost.CreateEditor();
		Exception? captured = null;

#endif
		// The controller verifies the editor thread up front, so constructing it elsewhere fails fast with
#if AVALONIAEDIT
		// the dispatcher's threading error instead of misbehaving later. Avalonia divergence: the headless
		// Avalonia dispatcher does not enforce thread affinity (its CheckAccess reports access from any
		// thread), so the guard - which a real Avalonia dispatcher enforces - cannot be exercised from the
		// headless suite. Reporting this inconclusive keeps the reference intent visible without a false
		// assertion.
		Assert.Inconclusive(
			"The headless Avalonia dispatcher does not enforce thread affinity, so the controller's off-thread "
			+ "construction guard cannot be exercised from the headless test host.");
#else
		// the dispatcher's threading error instead of misbehaving later.
		Task.Run(() =>
		{
			try
			{
				_ = CompletionTestHost.CreateController(editor);
			}
			catch (Exception exception)
			{
				captured = exception;
			}
		}).GetAwaiter().GetResult();

		Assert.IsInstanceOfType<InvalidOperationException>(captured);
#endif
	}

	[TestMethod]
	public void OpenOrRefresh_NullItems_ThrowsArgumentNullException()
	{
		TextCompletionController controller = CreateController();

		using (controller)
		{
			var exception = Assert.ThrowsExactly<ArgumentNullException>(
				() => controller.OpenOrRefresh(null!, 0, 5));

			Assert.AreEqual("items", exception.ParamName);
		}
	}

	[TestMethod]
	public void OpenOrRefresh_NegativeStartOffset_ThrowsArgumentOutOfRangeException()
	{
		TextCompletionController controller = CreateController();

		using (controller)
		{
			var exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
				() => controller.OpenOrRefresh([new TestCompletionData("sample")], -1, 5));

			Assert.AreEqual("startOffset", exception.ParamName);
		}
	}

	[TestMethod]
	public void OpenOrRefresh_EndOffsetBeforeStartOffset_ThrowsArgumentOutOfRangeException()
	{
		TextCompletionController controller = CreateController();

		using (controller)
		{
			var exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
				() => controller.OpenOrRefresh([new TestCompletionData("sample")], 4, 2));

			Assert.AreEqual("endOffset", exception.ParamName);
			Assert.IsFalse(controller.CurrentPresentation.IsListVisible);
		}
	}

	[TestMethod]
	public void OpenOrRefresh_OffsetsBeyondDocumentEnd_AreClampedToTheDocument()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CompletionTestHost.CreateHostedController(text: "sample");

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				// The document has six characters; the replacement range is clamped instead of reaching
				// past the document end.
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([new TestCompletionData("sample")], 3, 99));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				Assert.AreEqual(3, completionWindow.StartOffset);
				Assert.AreEqual(6, completionWindow.EndOffset);
			}
		}
	}

	[TestMethod]
	public void Defaults_PinDocumentedValues()
	{
		TextCompletionControllerOptions options = TextCompletionControllerOptions.Default;

		// The documented defaults are pinned so the README, the XML docs, and the option values cannot drift apart.
		Assert.AreEqual(TimeSpan.FromMilliseconds(120.0), options.RequestDebounceDelay);
		Assert.AreEqual(TimeSpan.FromMilliseconds(120.0), options.TooltipResolveDelay);
		Assert.AreEqual(420.0, options.WindowMinContentWidth);
		Assert.AreEqual(920.0, options.WindowMaxWidth);
		Assert.AreEqual(300.0, options.WindowMaxHeight);
		Assert.AreEqual(52.0, options.WindowHorizontalChrome);
		Assert.AreEqual(24.0, options.ItemIconWidth);
		Assert.AreEqual(12.0, options.ItemDetailSpacing);
		Assert.IsTrue(options.NonActivatingWindow);
		Assert.IsTrue(options.CloseWhenEmpty);

		// The tooltip chrome that used to live on the options is the default tooltip skin.
		Assert.AreEqual(10.0, CompletionTooltipSkin.Default.HorizontalOffset);
	}

	[TestMethod]
	public void OpenOrRefresh_OutOfDocumentOffsetsInReverseOrder_ThrowEvenWhenClampingWouldEqualizeThem()
	{
		TextCompletionController controller = CreateController();

		using (controller)
		{
			// Both raw offsets clamp to the six-character document, but the raw range is malformed, so it
			// must be rejected instead of being accepted as an empty clamped range.
			var exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
				() => controller.OpenOrRefresh([new TestCompletionData("sample")], 100, 60));

			Assert.AreEqual("endOffset", exception.ParamName);
		}
	}

	[TestMethod]
	public void OpenOrRefresh_OutOfDocumentOffsetsInOrder_AreAcceptedAsAClampedEmptyRange()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CompletionTestHost.CreateHostedController(text: "sample");

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([new TestCompletionData("sample")], 60, 100));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				Assert.AreEqual(6, completionWindow.StartOffset);
				Assert.AreEqual(6, completionWindow.EndOffset);
			}
		}
	}

	[TestMethod]
	public void TextCompletionItemCompletionData_NullItem_ThrowsArgumentNullException()
	{
		var exception = Assert.ThrowsExactly<ArgumentNullException>(() => new TextCompletionItemCompletionData(null!));

		Assert.AreEqual("item", exception.ParamName);
	}

	[TestMethod]
	public void Constructor_NullTextArea_ThrowsArgumentNullException()
	{
		var exception = Assert.ThrowsExactly<ArgumentNullException>(
			() => new TextCompletionController(null!, CompletionTestHost.CreateSkin()));

		Assert.AreEqual("textArea", exception.ParamName);
	}

	[TestMethod]
	public void Constructor_NullSkin_ThrowsArgumentNullException()
	{
		TextEditor editor = CompletionTestHost.CreateEditor();

		var exception = Assert.ThrowsExactly<ArgumentNullException>(
			() => new TextCompletionController(editor.TextArea, null!));

		Assert.AreEqual("skin", exception.ParamName);
	}

	[TestMethod]
	public async Task RequestAsync_NullCallback_ThrowsArgumentNullException()
	{
		TextCompletionController controller = CreateController();

		using (controller)
		{
			var exception = await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => controller.RequestAsync(null!));

			Assert.AreEqual("requestAsync", exception.ParamName);
		}
	}

	private static TextCompletionControllerOptions AssignNumericOption(string propertyName, double value) => propertyName switch
	{
		"WindowMinContentWidth" => TextCompletionControllerOptions.Default with { WindowMinContentWidth = value },
		"WindowMaxWidth" => TextCompletionControllerOptions.Default with { WindowMaxWidth = value },
		"WindowMaxHeight" => TextCompletionControllerOptions.Default with { WindowMaxHeight = value },
		"WindowHorizontalChrome" => TextCompletionControllerOptions.Default with { WindowHorizontalChrome = value },
		"ItemIconWidth" => TextCompletionControllerOptions.Default with { ItemIconWidth = value },
		"ItemDetailSpacing" => TextCompletionControllerOptions.Default with { ItemDetailSpacing = value },
		_ => throw new ArgumentOutOfRangeException(nameof(propertyName), propertyName, "No numeric option has this name.")
	};

	private static TextCompletionController CreateController(
		TextCompletionControllerOptions? options = null,
		TextCompletionControllerHooks? hooks = null)
	{
		TextEditor editor = CompletionTestHost.CreateEditor();

		return CompletionTestHost.CreateController(editor, options, hooks);
	}
}

#if AVALONIAEDIT
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Editing;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Editing;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed partial class CompletionWindowSizingTests
{
	[TestMethod]
	public void MeasureRequiredWidth_NoItems_ReturnsMinimumContentWidth()
	{
		(TextArea textArea, HostWindow hostWindow) = CreateHostedTextArea();

		using (hostWindow)
		{
			double width = CompletionWindowSizing.MeasureRequiredWidth(
				textArea,
				options: CompletionWindowOptions.Default,
				getDisplayInfo: null,
				measureItemWidth: null,
				items: []
			);

			Assert.AreEqual(CompletionWindowOptions.Default.MinContentWidth, width);
		}
	}

	[TestMethod]
	public void MeasureRequiredWidth_LongerItemText_IncreasesMeasuredWidth()
	{
		(TextArea textArea, HostWindow hostWindow) = CreateHostedTextArea();

		using (hostWindow)
		{
			CompletionWindowOptions options = CompletionWindowOptions.Default with
			{
				MinContentWidth = 1,
				ItemIconWidth = 0.0,
				ItemDetailSpacing = 0.0,
			};

			double shortWidth = CompletionWindowSizing.MeasureRequiredWidth(
				textArea,
				options,
				getDisplayInfo: null,
				measureItemWidth: null,
				items: [new StubCompletionData("a")]
			);

			double longWidth = CompletionWindowSizing.MeasureRequiredWidth(
				textArea,
				options,
				getDisplayInfo: null,
				measureItemWidth: null,
				items: [new StubCompletionData("a very long item name")]
			);

			Assert.IsGreaterThan(shortWidth, longWidth);
		}
	}

	[TestMethod]
	public void MeasureRequiredWidth_FilterTextShorterThanTheLabel_MeasuresTheDisplayedLabel()
	{
		(TextArea textArea, HostWindow hostWindow) = CreateHostedTextArea();

		using (hostWindow)
		{
			CompletionWindowOptions options = CompletionWindowOptions.Default with
			{
				MinContentWidth = 1,
				ItemIconWidth = 0.0,
				ItemDetailSpacing = 0.0,
			};

			var filteredItem = new CompletionItemAdapter(new CompletionItem("a very long item name") { FilterText = "a" });

			double filteredWidth = CompletionWindowSizing.MeasureRequiredWidth(
				textArea,
				options,
				getDisplayInfo: null,
				measureItemWidth: null,
				items: [filteredItem]
			);

			double plainWidth = CompletionWindowSizing.MeasureRequiredWidth(
				textArea,
				options,
				getDisplayInfo: null,
				measureItemWidth: null,
				items: [new StubCompletionData("a very long item name")]
			);

			// The window is sized for the displayed label even when the item filters under a shorter text.
			Assert.AreEqual(plainWidth, filteredWidth);
		}
	}

	[TestMethod]
	public void MeasureRequiredWidth_TextBlockContent_MeasuresTheRenderedText()
	{
		(TextArea textArea, HostWindow hostWindow) = CreateHostedTextArea();

		using (hostWindow)
		{
			CompletionWindowOptions options = CompletionWindowOptions.Default with
			{
				MinContentWidth = 1,
				ItemIconWidth = 0.0,
				ItemDetailSpacing = 0.0,
			};

			// A deprecated item renders a struck-through text block; the measurement must read the text out
			// of the block instead of falling back to the item's filter key.
			var deprecatedItem = new CompletionItemAdapter(new CompletionItem("a very long item name") { Tags = [CompletionItemTag.Deprecated] });

			double blockWidth = CompletionWindowSizing.MeasureRequiredWidth(
				textArea,
				options,
				getDisplayInfo: null,
				measureItemWidth: null,
				items: [deprecatedItem]
			);

			double plainWidth = CompletionWindowSizing.MeasureRequiredWidth(
				textArea,
				options,
				getDisplayInfo: null,
				measureItemWidth: null,
				items: [new StubCompletionData("a very long item name")]
			);

			Assert.AreEqual(plainWidth, blockWidth);
		}
	}

	[TestMethod]
	public void MeasureRequiredWidth_WidestItemAtTheEnd_IsMeasured()
	{
		(TextArea textArea, HostWindow hostWindow) = CreateHostedTextArea();

		using (hostWindow)
		{
			CompletionWindowOptions options = CompletionWindowOptions.Default with
			{
				MinContentWidth = 1,
				ItemIconWidth = 0.0,
				ItemDetailSpacing = 0.0,
			};

			ICompletionData[] items =
			[
				new StubCompletionData("a"),
				new StubCompletionData("another"),
				new StubCompletionData("a very long item name"),
			];

			double measuredWidth = CompletionWindowSizing.MeasureRequiredWidth(
				textArea,
				options,
				getDisplayInfo: null,
				measureItemWidth: null,
				items
			);

			double longItemWidth = CompletionWindowSizing.MeasureRequiredWidth(
				textArea,
				options,
				getDisplayInfo: null,
				measureItemWidth: null,
				items: [new StubCompletionData("a very long item name")]
			);

			// Every item is measured, so an item at the end of the list sizes the window like the same item alone.
			Assert.AreEqual(longItemWidth, measuredWidth);
		}
	}

	[TestMethod]
	public void MeasureRequiredWidth_CustomMeasurement_StopsMeasuringOnceTheCapIsReached()
	{
		(TextArea textArea, HostWindow hostWindow) = CreateHostedTextArea();

		using (hostWindow)
		{
			CompletionWindowOptions options = CompletionWindowOptions.Default with { MaxWidth = 300.0, HorizontalChrome = 50.0 };

			int measuredItems = 0;

			double measuredWidth = CompletionWindowSizing.MeasureRequiredWidth(
				textArea,
				options,
				getDisplayInfo: null,

				measureItemWidth: _ =>
				{
					measuredItems++;
					return 1000.0;
				},

				items: [new StubCompletionData("first"), new StubCompletionData("second"), new StubCompletionData("third")]
			);

			// Once the measured width reaches the window's content cap (300 minus the 50 chrome), no further
			// item can widen the window, so the remaining items are not measured.
			Assert.AreEqual(1, measuredItems);
			Assert.AreEqual(1000.0, measuredWidth);
		}
	}

	[TestMethod]
	public void MeasureRequiredWidth_ItemDetail_IncreasesMeasuredWidthBySpacingAndDetail()
	{
		(TextArea textArea, HostWindow hostWindow) = CreateHostedTextArea();

		using (hostWindow)
		{
			CompletionWindowOptions options = CompletionWindowOptions.Default with
			{
				MinContentWidth = 1,
				ItemIconWidth = 0.0,
				ItemDetailSpacing = 10.0,
			};

			ICompletionData[] items = [new StubCompletionData("item")];

			double withoutDetail = CompletionWindowSizing.MeasureRequiredWidth(
				textArea,
				options,
				getDisplayInfo: _ => ("item", null),
				measureItemWidth: null,
				items
			);

			double withDetail = CompletionWindowSizing.MeasureRequiredWidth(
				textArea,
				options,
				getDisplayInfo: _ => ("item", "detail"),
				measureItemWidth: null,
				items
			);

			Assert.IsGreaterThan(withoutDetail + 10.0, withDetail);
		}
	}

	[TestMethod]
	public void MeasureRequiredWidth_CustomMeasureItemWidth_ReplacesDefaultMeasurement()
	{
		(TextArea textArea, HostWindow hostWindow) = CreateHostedTextArea();

		using (hostWindow)
		{
			CompletionWindowOptions options = CompletionWindowOptions.Default with { MinContentWidth = 1 };

			double width = CompletionWindowSizing.MeasureRequiredWidth(
				textArea,
				options,
				getDisplayInfo: _ => ("item", null),
				measureItemWidth: _ => 1234.0,
				items: [new StubCompletionData("item")]
			);

			// A host measurement replaces the default icon + text + detail measurement entirely.
			Assert.AreEqual(1234.0, width);
		}
	}

	[TestMethod]
	public void MeasureRequiredWidth_NonFiniteCustomMeasurement_ThrowsInvalidOperationException()
	{
		(TextArea textArea, HostWindow hostWindow) = CreateHostedTextArea();

		using (hostWindow)
		{
			CompletionWindowOptions options = CompletionWindowOptions.Default with { MinContentWidth = 1 };

			// A non-finite measurement would flow into the window width, where the toolkit reads NaN as
			// auto-sizing and silently defeats the configured maximum width.
			InvalidOperationException exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
				CompletionWindowSizing.MeasureRequiredWidth(
					textArea,
					options,
					getDisplayInfo: null,
					measureItemWidth: _ => double.NaN,
					items: [new StubCompletionData("item")]
				)
			);

			Assert.Contains("MeasureItemWidth", exception.Message);
		}
	}

	[TestMethod]
	public void MeasureRequiredWidth_NegativeCustomMeasurement_DoesNotLowerTheContentFloor()
	{
		(TextArea textArea, HostWindow hostWindow) = CreateHostedTextArea();

		using (hostWindow)
		{
			CompletionWindowOptions options = CompletionWindowOptions.Default with { MinContentWidth = 400.0 };

			double width = CompletionWindowSizing.MeasureRequiredWidth(
				textArea,
				options,
				getDisplayInfo: null,
				measureItemWidth: _ => -50.0,
				items: [new StubCompletionData("item")]
			);

			// A negative width cannot widen or lower the window, so the content floor stays in effect.
			Assert.AreEqual(400.0, width);
		}
	}

	[TestMethod]
	public void MeasureRequiredWidth_ItemWithoutImage_DoesNotReserveTheIconColumn()
	{
		(TextArea textArea, HostWindow hostWindow) = CreateHostedTextArea();

		using (hostWindow)
		{
			CompletionWindowOptions withIconColumn = CompletionWindowOptions.Default with
			{
				MinContentWidth = 1,
				ItemIconWidth = 100.0,
				ItemDetailSpacing = 0.0,
			};

			double imageless = CompletionWindowSizing.MeasureRequiredWidth(
				textArea,
				options: withIconColumn,
				getDisplayInfo: null,
				measureItemWidth: null,
				items: [new StubCompletionData("item")]
			);

			double baseline = CompletionWindowSizing.MeasureRequiredWidth(
				textArea,

				options: withIconColumn with
				{
					ItemIconWidth = 0.0,
				},

				getDisplayInfo: null,
				measureItemWidth: null,
				items: [new StubCompletionData("item")]
			);

			// The icon column exists only for items that supply an image, so an imageless item measures exactly
			// as wide as it does with the column disabled.
			Assert.AreEqual(baseline, imageless);
		}
	}

	[TestMethod]
	public void MeasureRequiredWidth_ItemWithImage_AddsTheReservedIconColumn()
	{
		(TextArea textArea, HostWindow hostWindow) = CreateHostedTextArea();

		using (hostWindow)
		{
			CompletionWindowOptions options = CompletionWindowOptions.Default with
			{
				MinContentWidth = 1,
				ItemIconWidth = 100.0,
				ItemDetailSpacing = 0.0,
			};

			double imageless = CompletionWindowSizing.MeasureRequiredWidth(
				textArea,

				options: options with
				{
					ItemIconWidth = 0.0,
				},

				getDisplayInfo: null,
				measureItemWidth: null,
				items: [new StubCompletionData("item")]
			);

			double withImage = CompletionWindowSizing.MeasureRequiredWidth(
				textArea,
				options,
				getDisplayInfo: null,
				measureItemWidth: null,
				items: [new StubCompletionData("item", image: CompletionWindowTestHelpers.CreateIconImage())]
			);

			Assert.AreEqual(imageless + 100.0, withImage);
		}
	}

	[TestMethod]
	public void MeasureRequiredWidth_WhitespaceOnlyDetail_AddsNoDetailSpacing()
	{
		(TextArea textArea, HostWindow hostWindow) = CreateHostedTextArea();

		using (hostWindow)
		{
			CompletionWindowOptions options = CompletionWindowOptions.Default with
			{
				MinContentWidth = 1,
				ItemIconWidth = 0.0,
				ItemDetailSpacing = 10.0,
			};

			ICompletionData[] items = [new StubCompletionData("item")];

			double withoutDetail = CompletionWindowSizing.MeasureRequiredWidth(
				textArea,
				options,
				getDisplayInfo: _ => ("item", null),
				measureItemWidth: null,
				items
			);

			double whitespaceDetail = CompletionWindowSizing.MeasureRequiredWidth(
				textArea,
				options,
				getDisplayInfo: _ => ("item", "   "),
				measureItemWidth: null,
				items
			);

			// A whitespace-only detail is treated as no detail, so the spacing gap is not added.
			Assert.AreEqual(withoutDetail, whitespaceDetail);
		}
	}

	[TestMethod]
	public void MeasureRequiredWidth_DefaultMeasurement_StopsMeasuringOnceTheCapIsReached()
	{
		(TextArea textArea, HostWindow hostWindow) = CreateHostedTextArea();

		using (hostWindow)
		{
			CompletionWindowOptions options = CompletionWindowOptions.Default with
			{
				MinContentWidth = 1,
				MaxWidth = 60.0,
				HorizontalChrome = 0.0,
				ItemIconWidth = 0.0,
				ItemDetailSpacing = 0.0,
			};

			var first = new StubCompletionData("a very long item name that exceeds the cap");
			var second = new StubCompletionData("an even longer item name that is wider still and also exceeds the cap");

			double firstAlone = CompletionWindowSizing.MeasureRequiredWidth(
				textArea,
				options,
				getDisplayInfo: null,
				measureItemWidth: null,
				items: [first]
			);

			double both = CompletionWindowSizing.MeasureRequiredWidth(
				textArea,
				options,
				getDisplayInfo: null,
				measureItemWidth: null,
				items: [first, second]
			);

			// The first item already exceeds the content cap (60 minus the zero chrome), so the default
			// measurement stops before it measures the wider second item; the window can never grow past the cap.
			Assert.IsGreaterThan(60.0, firstAlone, "The first item must exceed the content cap for the early exit to be observable.");
			Assert.AreEqual(firstAlone, both);
		}
	}

	private static (TextArea TextArea, HostWindow HostWindow) CreateHostedTextArea()
	{
		// The sizing math depends on the font, so the design font is pinned before the window is shown to
		// keep measured widths deterministic across machines and DPI settings.
		(TextEditor editor, HostWindow hostWindow) = TestHost.ShowHostedEditor(configureEditor: static editor => TestHost.PinDesignFontSize(editor));
		return (editor.TextArea, hostWindow);
	}
}

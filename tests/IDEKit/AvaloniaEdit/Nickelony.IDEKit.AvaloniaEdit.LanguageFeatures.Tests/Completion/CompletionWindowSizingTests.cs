using Avalonia;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Editing;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.IntelliSense.Completion;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

[AvaloniaTestClass]
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
				TextCompletionControllerOptions.Default,
				getDisplayInfo: null,
				measureItemWidth: null,
				[]);

			Assert.AreEqual(420.0, width);
		}
	}

	[TestMethod]
	public void MeasureRequiredWidth_LongerItemText_IncreasesMeasuredWidth()
	{
		(TextArea textArea, HostWindow hostWindow) = CreateHostedTextArea();

		using (hostWindow)
		{
			var options = TextCompletionControllerOptions.Default with
			{
				WindowMinContentWidth = 1,
				ItemIconWidth = 0.0,
				ItemDetailSpacing = 0.0
			};

			double shortWidth = CompletionWindowSizing.MeasureRequiredWidth(
				textArea,
				options,
				getDisplayInfo: null,
				measureItemWidth: null,
				[new TestCompletionData("a")]);

			double longWidth = CompletionWindowSizing.MeasureRequiredWidth(
				textArea,
				options,
				getDisplayInfo: null,
				measureItemWidth: null,
				[new TestCompletionData("a very long item name")]);

			Assert.IsTrue(longWidth > shortWidth);
		}
	}

	[TestMethod]
	public void MeasureRequiredWidth_FilterTextShorterThanTheLabel_MeasuresTheDisplayedLabel()
	{
		(TextArea textArea, HostWindow hostWindow) = CreateHostedTextArea();

		using (hostWindow)
		{
			var options = TextCompletionControllerOptions.Default with
			{
				WindowMinContentWidth = 1,
				ItemIconWidth = 0.0,
				ItemDetailSpacing = 0.0
			};

			var filteredItem = new TextCompletionItemCompletionData(new TextCompletionItem("a very long item name")
			{
				FilterText = "a"
			});

			double filteredWidth = CompletionWindowSizing.MeasureRequiredWidth(
				textArea,
				options,
				getDisplayInfo: null,
				measureItemWidth: null,
				[filteredItem]);

			double plainWidth = CompletionWindowSizing.MeasureRequiredWidth(
				textArea,
				options,
				getDisplayInfo: null,
				measureItemWidth: null,
				[new TestCompletionData("a very long item name")]);

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
			var options = TextCompletionControllerOptions.Default with
			{
				WindowMinContentWidth = 1,
				ItemIconWidth = 0.0,
				ItemDetailSpacing = 0.0
			};

			// A deprecated item renders a struck-through text block; the measurement must read the text out
			// of the block instead of falling back to the item's filter key.
			var deprecatedItem = new TextCompletionItemCompletionData(new TextCompletionItem("a very long item name")
			{
				Tags = [TextCompletionTag.Deprecated]
			});

			double blockWidth = CompletionWindowSizing.MeasureRequiredWidth(
				textArea,
				options,
				getDisplayInfo: null,
				measureItemWidth: null,
				[deprecatedItem]);

			double plainWidth = CompletionWindowSizing.MeasureRequiredWidth(
				textArea,
				options,
				getDisplayInfo: null,
				measureItemWidth: null,
				[new TestCompletionData("a very long item name")]);

			Assert.AreEqual(plainWidth, blockWidth);
		}
	}

	[TestMethod]
	public void MeasureRequiredWidth_WidestItemAtTheEnd_IsMeasured()
	{
		(TextArea textArea, HostWindow hostWindow) = CreateHostedTextArea();

		using (hostWindow)
		{
			var options = TextCompletionControllerOptions.Default with
			{
				WindowMinContentWidth = 1,
				ItemIconWidth = 0.0,
				ItemDetailSpacing = 0.0
			};

			ICompletionData[] items =
			[
				new TestCompletionData("a"),
				new TestCompletionData("another"),
				new TestCompletionData("a very long item name")
			];

			double measuredWidth = CompletionWindowSizing.MeasureRequiredWidth(
				textArea,
				options,
				getDisplayInfo: null,
				measureItemWidth: null,
				items);

			double longItemWidth = CompletionWindowSizing.MeasureRequiredWidth(
				textArea,
				options,
				getDisplayInfo: null,
				measureItemWidth: null,
				[new TestCompletionData("a very long item name")]);

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
			var options = TextCompletionControllerOptions.Default with
			{
				WindowMaxWidth = 300.0,
				WindowHorizontalChrome = 50.0
			};

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
				[new TestCompletionData("first"), new TestCompletionData("second"), new TestCompletionData("third")]);

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
			var options = TextCompletionControllerOptions.Default with
			{
				WindowMinContentWidth = 1,
				ItemIconWidth = 0.0,
				ItemDetailSpacing = 10.0
			};

			ICompletionData[] items = [new TestCompletionData("item")];

			double withoutDetail = CompletionWindowSizing.MeasureRequiredWidth(
				textArea,
				options,
				_ => ("item", null),
				measureItemWidth: null,
				items);

			double withDetail = CompletionWindowSizing.MeasureRequiredWidth(
				textArea,
				options,
				_ => ("item", "detail"),
				measureItemWidth: null,
				items);

			Assert.IsTrue(withDetail > withoutDetail + 10.0);
		}
	}

	[TestMethod]
	public void MeasureRequiredWidth_CustomMeasureItemWidth_ReplacesDefaultMeasurement()
	{
		(TextArea textArea, HostWindow hostWindow) = CreateHostedTextArea();

		using (hostWindow)
		{
			var options = TextCompletionControllerOptions.Default with
			{
				WindowMinContentWidth = 1
			};

			double width = CompletionWindowSizing.MeasureRequiredWidth(
				textArea,
				options,
				getDisplayInfo: _ => ("item", null),
				measureItemWidth: _ => 1234.0,
				[new TestCompletionData("item")]);

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
			var options = TextCompletionControllerOptions.Default with
			{
				WindowMinContentWidth = 1
			};

			// A non-finite measurement would flow into the window width, where Avalonia reads NaN as
			// auto-sizing and silently defeats the configured maximum width.
			var exception = Assert.ThrowsExactly<InvalidOperationException>(() => CompletionWindowSizing.MeasureRequiredWidth(
				textArea,
				options,
				getDisplayInfo: null,
				measureItemWidth: _ => double.NaN,
				[new TestCompletionData("item")]));

			StringAssert.Contains(exception.Message, "MeasureItemWidth");
		}
	}

	[TestMethod]
	public void MeasureRequiredWidth_NegativeCustomMeasurement_DoesNotLowerTheContentFloor()
	{
		(TextArea textArea, HostWindow hostWindow) = CreateHostedTextArea();

		using (hostWindow)
		{
			var options = TextCompletionControllerOptions.Default with
			{
				WindowMinContentWidth = 400.0
			};

			double width = CompletionWindowSizing.MeasureRequiredWidth(
				textArea,
				options,
				getDisplayInfo: null,
				measureItemWidth: _ => -50.0,
				[new TestCompletionData("item")]);

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
			var withIconColumn = TextCompletionControllerOptions.Default with
			{
				WindowMinContentWidth = 1,
				ItemIconWidth = 100.0,
				ItemDetailSpacing = 0.0
			};

			double imageless = CompletionWindowSizing.MeasureRequiredWidth(
				textArea,
				withIconColumn,
				getDisplayInfo: null,
				measureItemWidth: null,
				[new TestCompletionData("item")]);

			double baseline = CompletionWindowSizing.MeasureRequiredWidth(
				textArea,
				withIconColumn with { ItemIconWidth = 0.0 },
				getDisplayInfo: null,
				measureItemWidth: null,
				[new TestCompletionData("item")]);

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
			var options = TextCompletionControllerOptions.Default with
			{
				WindowMinContentWidth = 1,
				ItemIconWidth = 100.0,
				ItemDetailSpacing = 0.0
			};

			double imageless = CompletionWindowSizing.MeasureRequiredWidth(
				textArea,
				options with { ItemIconWidth = 0.0 },
				getDisplayInfo: null,
				measureItemWidth: null,
				[new TestCompletionData("item")]);

			double withImage = CompletionWindowSizing.MeasureRequiredWidth(
				textArea,
				options,
				getDisplayInfo: null,
				measureItemWidth: null,
				[new TestCompletionData("item", image: new TestImage())]);

			Assert.AreEqual(imageless + 100.0, withImage);
		}
	}

	[TestMethod]
	public void MeasureRequiredWidth_WhitespaceOnlyDetail_AddsNoDetailSpacing()
	{
		(TextArea textArea, HostWindow hostWindow) = CreateHostedTextArea();

		using (hostWindow)
		{
			var options = TextCompletionControllerOptions.Default with
			{
				WindowMinContentWidth = 1,
				ItemIconWidth = 0.0,
				ItemDetailSpacing = 10.0
			};

			ICompletionData[] items = [new TestCompletionData("item")];

			double withoutDetail = CompletionWindowSizing.MeasureRequiredWidth(
				textArea,
				options,
				_ => ("item", null),
				measureItemWidth: null,
				items);

			double whitespaceDetail = CompletionWindowSizing.MeasureRequiredWidth(
				textArea,
				options,
				_ => ("item", "   "),
				measureItemWidth: null,
				items);

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
			var options = TextCompletionControllerOptions.Default with
			{
				WindowMinContentWidth = 1,
				WindowMaxWidth = 60.0,
				WindowHorizontalChrome = 0.0,
				ItemIconWidth = 0.0,
				ItemDetailSpacing = 0.0
			};

			var first = new TestCompletionData("a very long item name that exceeds the cap");
			var second = new TestCompletionData("an even longer item name that is wider still and also exceeds the cap");

			double firstAlone = CompletionWindowSizing.MeasureRequiredWidth(
				textArea,
				options,
				getDisplayInfo: null,
				measureItemWidth: null,
				[first]);

			double both = CompletionWindowSizing.MeasureRequiredWidth(
				textArea,
				options,
				getDisplayInfo: null,
				measureItemWidth: null,
				[first, second]);

			// The first item already exceeds the content cap (60 minus the zero chrome), so the default
			// measurement stops before it measures the wider second item; the window can never grow past the cap.
			Assert.IsTrue(firstAlone > 60.0, "The first item must exceed the content cap for the early exit to be observable.");
			Assert.AreEqual(firstAlone, both);
		}
	}

	private static (TextArea TextArea, HostWindow HostWindow) CreateHostedTextArea()
	{
		// The sizing math depends on the font, so the design font is pinned before the window is shown to
		// keep measured widths deterministic across machines and DPI settings.
		(TextEditor editor, HostWindow hostWindow) =
			AvaloniaTestHost.ShowHostedEditor(configureEditor: static editor => AvaloniaTestHost.PinDesignFontSize(editor));

		return (editor.TextArea, hostWindow);
	}

	/// <summary>
	/// A minimal 1x1 image double for the sizing tests. The mirror measures an item's icon column from the
	/// image's presence, and the headless drawing backend cannot create a real bitmap, so this lightweight
	/// <see cref="IImage"/> stands in for a rendered icon.
	/// </summary>
	private sealed class TestImage : IImage
	{
		/// <inheritdoc/>
		public Size Size => new(1.0, 1.0);

		/// <inheritdoc/>
		public void Draw(DrawingContext context, Rect sourceRect, Rect destRect)
		{
		}
	}
}

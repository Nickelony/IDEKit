#if AVALONIAEDIT
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.Rendering;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.Tests;
#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Rendering;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.Tests;
#endif

/// <summary>
/// The shared contract every <see cref="LineStatusMarginBase"/> margin satisfies: it reserves its authored
/// width, rejects an invalid width, honors a width change, scales that width with the text view font size,
/// paints its marker for a marked line and nothing otherwise, and follows the text view connection.
/// </summary>
/// <remarks>
/// A concrete margin derives from this base and supplies the authored width, the marker strip width, the
/// marker color, and a factory. The marker-shape specifics (brush and geometry defaults, click behavior,
/// custom-brush replacement) stay in each margin's own suite, so this base covers only the scenarios whose
/// shape is identical across margins.
/// </remarks>
public abstract class LineStatusMarginContractTests
{
	/// <summary>Gets the authored default width in design DIPs.</summary>
	protected abstract double DefaultMarginWidth { get; }

	/// <summary>Gets the marker strip width the bitmap scans cover.</summary>
	protected abstract double StripWidth { get; }

	/// <summary>Gets the marker color the bitmap scans look for.</summary>
	protected abstract Color MarkerColor { get; }

	/// <summary>Gets the human-readable marker description used in failure messages.</summary>
	protected abstract string MarkerDescription { get; }

	/// <summary>
	/// Creates the margin under test over a fixed line-status source.
	/// </summary>
	/// <param name="document">The document the source reports against.</param>
	/// <param name="markedLines">The marked line numbers the source reports.</param>
	/// <returns>The created margin.</returns>
	protected abstract LineStatusMarginBase CreateMargin(TextDocument document, IReadOnlyList<int> markedLines);

	[TestMethod]
	public void MeasureOverride_ReservesTheAuthoredDefaultWidth()
	{
		LineStatusMarginBase margin = CreateMargin(new TextDocument("one\r\ntwo"), []);

		TestHost.PinDesignFontSize(margin);
		margin.Measure(new Size(100.0, 100.0));

		Assert.AreEqual(DefaultMarginWidth, margin.DesiredSize.Width);
	}

	[TestMethod]
	public void MarginWidth_InvalidValues_AreRejectedWithoutChangingMeasure()
	{
		LineStatusMarginBase margin = CreateMargin(new TextDocument("one\r\ntwo"), []);

		Assert.ThrowsExactly<ArgumentException>(() => margin.MarginWidth = -4.0);
		Assert.ThrowsExactly<ArgumentException>(() => margin.MarginWidth = double.NaN);
		Assert.ThrowsExactly<ArgumentException>(() => margin.MarginWidth = double.PositiveInfinity);
		Assert.AreEqual(DefaultMarginWidth, margin.MarginWidth);

		TestHost.PinDesignFontSize(margin);
		margin.Measure(new Size(100.0, 100.0));
		Assert.AreEqual(DefaultMarginWidth, margin.DesiredSize.Width);
	}

	[TestMethod]
	public void MarginWidth_Change_IsHonoredByMeasureOverride()
	{
		LineStatusMarginBase margin = CreateMargin(new TextDocument("one\r\ntwo"), []);
		double widened = DefaultMarginWidth + 8.0;

		margin.MarginWidth = widened;

		TestHost.PinDesignFontSize(margin);
		margin.Measure(new Size(100.0, 100.0));

		Assert.AreEqual(widened, margin.DesiredSize.Width);
	}

	[TestMethod]
	public void MarginWidth_Change_InvalidatesMeasure()
	{
		LineStatusMarginBase margin = CreateMargin(new TextDocument("one\r\ntwo"), []);

		margin.Measure(new Size(100.0, 100.0));
		Assert.IsTrue(margin.IsMeasureValid);

		margin.MarginWidth = DefaultMarginWidth + 8.0;

		Assert.IsFalse(margin.IsMeasureValid);
	}

	[TestMethod]
	public void DesiredWidth_ScalesWithTextViewFontSize()
	{
		TextDocument document = CreateDocument(20);
		LineStatusMarginBase margin = CreateMargin(document, []);
		var editor = new TextEditor { Document = document, FontSize = 24.0 };

		using var host = new HostedMarginScope(editor, margin);

		editor.UpdateLayout();

		// The gutter is authored for the 12-DIP design font and scales with the font size.
		Assert.AreEqual(DefaultMarginWidth * 24.0 / 12.0, margin.DesiredSize.Width, 0.5);

		editor.FontSize = 8.0;
		editor.UpdateLayout();

		Assert.AreEqual(DefaultMarginWidth * 8.0 / 12.0, margin.DesiredSize.Width, 0.5);
	}

	[TestMethod]
	public void OnRender_DrawsMarkerForMarkedLine()
	{
		TextDocument document = CreateDocument(20);
		LineStatusMarginBase margin = CreateMargin(document, [1]);
		var editor = new TextEditor { Document = document, FontSize = 12.0 };

		using var host = new HostedMarginScope(editor, margin);

#if AVALONIAEDIT
		Bitmap bitmap = TestBitmapRendering.RenderToBitmap(editor);
#else
		BitmapSource bitmap = TestBitmapRendering.RenderToBitmap(editor);
#endif

		Assert.IsTrue(
			TestBitmapRendering.HasColorInLeftStrip(bitmap, (int)StripWidth, MarkerColor),
			$"Expected a {MarkerDescription} in the margin strip.");
	}

	[TestMethod]
	public void OnRender_DrawsNothingWithoutMarkedLines()
	{
		TextDocument document = CreateDocument(20);
		LineStatusMarginBase margin = CreateMargin(document, []);
		var editor = new TextEditor { Document = document, FontSize = 12.0 };

		using var host = new HostedMarginScope(editor, margin);

#if AVALONIAEDIT
		Bitmap bitmap = TestBitmapRendering.RenderToBitmap(editor);
#else
		BitmapSource bitmap = TestBitmapRendering.RenderToBitmap(editor);
#endif

		Assert.IsFalse(
			TestBitmapRendering.HasColorInLeftStrip(bitmap, (int)StripWidth, MarkerColor),
			$"Expected no {MarkerDescription} without marked lines.");
	}

	[TestMethod]
	public void SourceSubscription_FollowsTextViewConnection()
	{
		var document = new TextDocument("one\r\ntwo");
		LineStatusMarginBase margin = CreateMargin(document, []);
		var editor = new TextEditor { Document = document };

		editor.TextArea.LeftMargins.Add(margin);

		Assert.IsTrue(margin.IsSourceSubscribed);

		editor.TextArea.LeftMargins.Remove(margin);

		Assert.IsFalse(margin.IsSourceSubscribed);
	}

	private static TextDocument CreateDocument(int lineCount)
		=> new(string.Join("\r\n", Enumerable.Repeat("line", lineCount)));
}

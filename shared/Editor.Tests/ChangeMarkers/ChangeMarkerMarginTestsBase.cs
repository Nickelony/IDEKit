#if AVALONIAEDIT
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Nickelony.IDEKit.AvaloniaEdit.ChangeMarkers;
using Nickelony.IDEKit.AvaloniaEdit.Rendering;
using Nickelony.IDEKit.Core.LineStatus;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.Tests;
#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.ChangeMarkers;
using Nickelony.IDEKit.AvalonEdit.Rendering;
using Nickelony.IDEKit.Core.LineStatus;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.Tests;
#endif

/// <summary>
/// The <see cref="ChangeMarkerMargin"/> scenarios whose shape is the same across the editor bindings: the
/// brush-assignment guard, the wrapped-line bar, the source-subscription lifecycle, the background-change
/// marshaling, the disposal and custom-brush render cases, the font-scaled marker bounds, and the
/// authored-width fallback for an unarranged margin.
/// </summary>
/// <remarks>
/// A binding supplies the engine-specific operations through the hooks below: reading the margin's arranged
/// width, and capturing the bounds of a marker drawn into a throwaway drawing. The property-registration
/// scenarios (the effective default width and the brush default and affects flags) stay in each binding's
/// own suite, because WPF reads <c>FrameworkPropertyMetadata</c> flags where Avalonia pins the same contract
/// behaviorally.
/// </remarks>
public abstract class ChangeMarkerMarginTestsBase : LineStatusMarginContractTests
{
	protected override double DefaultMarginWidth => 4.0;

	protected override double StripWidth => 4.0;

	protected override Color MarkerColor => Color.FromRgb(0x1E, 0x90, 0xFF);

	protected override string MarkerDescription => "change marker";

	protected override LineStatusMarginBase CreateMargin(TextDocument document, IReadOnlyList<int> markedLines)
		=> new ChangeMarkerMargin(new FixedLineStatusSource(() => document, markedLines));

	/// <summary>
	/// Gets the margin's arranged width after the host laid it out.
	/// </summary>
	/// <param name="margin">The margin to read.</param>
	/// <returns>The arranged width.</returns>
	protected abstract double GetArrangedWidth(LineStatusMarginBase margin);

	/// <summary>
	/// Draws the margin's marker into a throwaway drawing and returns the drawn bounds.
	/// </summary>
	/// <param name="margin">The margin whose marker is drawn.</param>
	/// <param name="visualLine">The visual line to draw the marker for.</param>
	/// <returns>The bounds of the recorded drawing.</returns>
	protected abstract Rect CaptureMarkerBounds(LineStatusMarginBase margin, VisualLine visualLine);

	[TestMethod]
	public void MarkerBrush_NullAssignments_AreRejected()
	{
		ChangeMarkerMargin margin = CreateMargin();

		Assert.ThrowsExactly<ArgumentNullException>(() => margin.MarkerBrush = null!);

		// XAML and SetValue assignments bypass the CLR property setter and hit the styled-property validation callback.
		Assert.ThrowsExactly<ArgumentException>(() => margin.SetValue(ChangeMarkerMargin.MarkerBrushProperty, null!));
	}

	[TestMethod]
	public void OnRender_WrappedMarkedLine_BarSpansTheWholeWrappedLine()
	{
		// A single document line long enough to wrap several times at the hosted width.
		var document = new TextDocument(string.Join(" ", Enumerable.Repeat("word", 300)));
		var tracker = new UnsavedChangesTracker(() => document);
		var margin = new ChangeMarkerMargin(tracker);
		var editor = new TextEditor { Document = document, FontSize = 12.0, WordWrap = true };

		using var host = new HostedMarginScope(editor, margin);

		IReadOnlyList<VisualLine> visualLines = editor.TextArea.TextView.VisualLines;

		// The premise: word wrap keeps the document line as one visual line made of several
		// text lines; the empty baseline marks the single line.
		Assert.HasCount(1, visualLines);
		Assert.IsGreaterThan(1, visualLines[0].TextLines.Count);

		var bitmap = TestBitmapRendering.RenderToBitmap(editor);

		bool found = TestBitmapRendering.TryGetColorRowBounds(
			bitmap,
			(int)Math.Ceiling(GetArrangedWidth(margin)),
			Color.FromRgb(0x1E, 0x90, 0xFF),
			out int minRow,
			out int maxRow);

		Assert.IsTrue(found, "Expected a blue change marker in the margin strip.");

		// The bar covers the full wrapped height instead of a single text line's height.
		Assert.IsGreaterThan(editor.TextArea.TextView.DefaultLineHeight, maxRow - minRow + 1);

		int segments = TestBitmapRendering.CountColorRowSegments(
			bitmap,
			(int)Math.Ceiling(GetArrangedWidth(margin)),
			Color.FromRgb(0x1E, 0x90, 0xFF));

		Assert.AreEqual(1, segments, "Expected one continuous change bar for the wrapped line.");
	}

	[TestMethod]
	public void IsSourceSubscribed_NonNotifyingSource_IsFalse()
	{
		var document = new TextDocument("one\r\ntwo");
		var margin = new ChangeMarkerMargin(new EmptyMarkerSource());
		var editor = new TextEditor { Document = document };

		editor.TextArea.LeftMargins.Add(margin);

		Assert.IsFalse(margin.IsSourceSubscribed);
	}

	[TestMethod]
	public void NotifyingSource_BackgroundChange_IsMarshaledToMarginDispatcher()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var tracker = new UnsavedChangesTracker(() => document);
		var margin = new ChangeMarkerMargin(tracker);
		var editor = new TextEditor { Document = document, FontSize = 12.0 };

		// The baseline matches the document, so nothing is marked yet.
		tracker.SetBaseline(document.Text);

		using var host = new HostedMarginScope(editor, margin);

		// Changing the baseline on a background thread notifies the margin, which must
		// marshal its repaint instead of touching the visual from the wrong thread.
		Task.Run(() => tracker.SetBaseline("one\r\nTWO\r\nthree")).GetAwaiter().GetResult();

		var bitmap = TestBitmapRendering.PumpAndRender(editor);

		Assert.IsTrue(
			TestBitmapRendering.HasColorInLeftStrip(bitmap, 4, Color.FromRgb(0x1E, 0x90, 0xFF)),
			"Expected the margin to mark the line that changed on the background thread.");
	}

	[TestMethod]
	public void NotifyingSource_ChangeAfterTextViewDetached_PumpIsSafeAndMarginStaysDetached()
	{
		var document = new TextDocument("one\r\ntwo");
		var tracker = new UnsavedChangesTracker(() => document);
		var margin = new ChangeMarkerMargin(tracker);
		var editor = new TextEditor { Document = document };

		editor.TextArea.LeftMargins.Add(margin);

		Assert.IsTrue(margin.IsSourceSubscribed);

		// Queue a repaint while the margin is still connected to the text view...
		tracker.SetBaseline("one\r\nTWO");

		// ...then detach it before the queued callback runs. The queued invalidation observes the
		// detached text view: pumping it must neither throw nor re-subscribe the margin to the source.
		editor.TextArea.LeftMargins.Remove(margin);
		TestHost.PumpDispatcher(editor.Dispatcher, DispatcherPriority.Render);

		Assert.IsFalse(margin.IsSourceSubscribed);
	}

	[TestMethod]
	public void OnRender_AfterTrackerDisposal_DrawsNoMarker()
	{
		var document = new TextDocument(string.Join("\r\n", Enumerable.Repeat("line", 20)));
		var tracker = new UnsavedChangesTracker(() => document);
		var margin = new ChangeMarkerMargin(tracker);
		var editor = new TextEditor { Document = document, FontSize = 12.0 };

		using var host = new HostedMarginScope(editor, margin);

		// The margin reads its source during rendering, so disposing the tracker while the margin is
		// still attached must not turn the next repaint into a dispatcher exception.
		tracker.Dispose();
		margin.InvalidateVisual();

		var bitmap = TestBitmapRendering.PumpAndRender(editor);

		Assert.IsFalse(
			TestBitmapRendering.HasColorInLeftStrip(bitmap, 4, Color.FromRgb(0x1E, 0x90, 0xFF)),
			"Expected no marker after the tracker was disposed.");
	}

	[TestMethod]
	public void OnRender_MarkerBounds_ScaleWithTextViewFontSize()
	{
		(int MinColumn, int MaxColumn) RenderMarkerBounds(double fontSize)
		{
			var document = new TextDocument(string.Join("\r\n", Enumerable.Repeat("line", 20)));
			var margin = new ChangeMarkerMargin(new FixedLineStatusSource(() => document, [1], notifiesChanges: false));
			var editor = new TextEditor { Document = document, FontSize = fontSize };

			using var host = new HostedMarginScope(editor, margin);

			// The premise: the hosted margin is arranged to a positive width, so the scan covers a real strip
			// and the scaled-bounds comparison below is not vacuous.
			Assert.IsGreaterThan(0.0, GetArrangedWidth(margin), "The margin must be arranged for this test.");

			var bitmap = TestBitmapRendering.RenderToBitmap(editor);

			bool found = TestBitmapRendering.TryGetColorColumnBounds(
				bitmap,
				(int)Math.Ceiling(GetArrangedWidth(margin)),
				Color.FromRgb(0x1E, 0x90, 0xFF),
				out int minColumn,
				out int maxColumn);

			Assert.IsTrue(found, "Expected a blue change marker in the margin.");
			return (minColumn, maxColumn);
		}

		(int MinColumn, int MaxColumn) smallMarker = RenderMarkerBounds(8.0);
		(int MinColumn, int MaxColumn) largeMarker = RenderMarkerBounds(24.0);

		int smallMarkerWidth = smallMarker.MaxColumn - smallMarker.MinColumn;
		int largeMarkerWidth = largeMarker.MaxColumn - largeMarker.MinColumn;

		Assert.IsGreaterThan(0, smallMarkerWidth);
		Assert.IsGreaterThan(smallMarkerWidth, largeMarkerWidth);
	}

	[TestMethod]
	public void OnRender_CustomMarkerBrush_IsUsed()
	{
		var document = new TextDocument(string.Join("\r\n", Enumerable.Repeat("line", 20)));

		var margin = new ChangeMarkerMargin(new FixedLineStatusSource(() => document, [1], notifiesChanges: false))
		{
			MarkerBrush = new SolidColorBrush(Colors.Red)
		};

		var editor = new TextEditor { Document = document, FontSize = 12.0 };

		using var host = new HostedMarginScope(editor, margin);

		var bitmap = TestBitmapRendering.RenderToBitmap(editor);

		Assert.IsTrue(
			TestBitmapRendering.HasColorInLeftStrip(bitmap, 4, Colors.Red),
			"Expected the custom red marker brush to be used.");

		Assert.IsFalse(
			TestBitmapRendering.HasColorInLeftStrip(bitmap, 4, Color.FromRgb(0x1E, 0x90, 0xFF)),
			"Expected the default blue marker to be replaced.");
	}

	[TestMethod]
	public void DrawMarker_UnarrangedMargin_DrawsTheMarkerAtTheAuthoredWidth()
	{
		var document = new TextDocument(string.Join("\r\n", Enumerable.Repeat("line", 20)));
		var margin = new ChangeMarkerMargin(new FixedLineStatusSource(() => document, [1]));

		TestHost.PinDesignFontSize(margin);

		// The unarranged margin has no actual width, so the authored fallback width must be used.
		Assert.AreEqual(0.0, GetArrangedWidth(margin));

		var editor = new TextEditor { Document = document, FontSize = 12.0 };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		VisualLine firstLine = editor.TextArea.TextView.VisualLines[0];

		Rect bounds = CaptureMarkerBounds(margin, firstLine);

		// The default 4-DIP bar spans the authored width when the margin is not arranged.
		Assert.AreEqual(0.0, bounds.X, 0.001);
		Assert.AreEqual(4.0, bounds.Width, 0.001);
	}

	protected static ChangeMarkerMargin CreateMargin()
		=> new(new EmptyMarkerSource());

	// A source without the IChangeNotificationSource interface, so the margin exercises the
	// no-subscription branch; FixedLineStatusSource implements the interface even when it never
	// raises, so it cannot stand in for this double.
	private sealed class EmptyMarkerSource : ILineStatusSource
	{
		public IReadOnlyList<int> GetMarkedLineNumbers() => [];
	}
}

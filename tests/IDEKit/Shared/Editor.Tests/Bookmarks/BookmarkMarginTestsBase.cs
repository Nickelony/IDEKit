#if AVALONIAEDIT
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Nickelony.IDEKit.AvaloniaEdit.Bookmarks;
using Nickelony.IDEKit.AvaloniaEdit.LineStatus;
using Nickelony.IDEKit.Core.Bookmarks;

namespace Nickelony.IDEKit.AvaloniaEdit.Tests;

#else
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.Bookmarks;
using Nickelony.IDEKit.AvalonEdit.LineStatus;
using Nickelony.IDEKit.Core.Bookmarks;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

#endif

/// <summary>The shared <see cref="BookmarkMargin"/> scenarios that run identically across the editor bindings.</summary>
/// <remarks>
/// A binding supplies the engine-specific operations through the hooks below: reading the margin's arranged width and capturing
/// the bounds of a marker drawn into a throwaway drawing. The property-registration scenarios (the default width and the brush and geometry defaults)
/// and the whole-surface hit-test scenario stay in each binding's own suite, because the two engines expose their metadata and hit-test APIs
/// in different shapes.
/// </remarks>
public abstract class BookmarkMarginTestsBase : LineStatusMarginContractTestsBase
{
	protected override double DefaultMarginWidth => 16.0;

	protected override double StripWidth => 16.0;

	protected override Color MarkerColor => Color.FromRgb(0xE6, 0xA2, 0x3C);

	protected override string MarkerDescription => "bookmark icon";

	protected override LineStatusMarginBase CreateMargin(TextDocument document, IReadOnlyList<int> markedLines) =>
		new BookmarkMargin(new StubLineStatusSource(() => document, markedLines));

	/// <summary>Returns the margin's arranged width after the host laid it out.</summary>
	/// <param name="margin">The margin to read.</param>
	/// <returns>The arranged width.</returns>
	protected abstract double GetArrangedWidth(LineStatusMarginBase margin);

	/// <summary>Draws the margin's marker into a throwaway drawing and returns the drawn bounds.</summary>
	/// <param name="margin">The margin whose marker is drawn.</param>
	/// <param name="visualLine">The visual line to draw the marker for.</param>
	/// <returns>The bounds of the recorded drawing.</returns>
	protected abstract Rect CaptureMarkerBounds(LineStatusMarginBase margin, VisualLine visualLine);

	[TestMethod]
	public void IconBrushAndGeometry_NullAssignments_AreRejected()
	{
		BookmarkMargin margin = CreateMargin(new TextDocument("one\r\ntwo"));

		Assert.ThrowsExactly<ArgumentNullException>(() => margin.IconBrush = null!);
		Assert.ThrowsExactly<ArgumentNullException>(() => margin.IconGeometry = null!);

		// XAML and SetValue assignments bypass the CLR property setter and hit the styled-property validation callback.
		Assert.ThrowsExactly<ArgumentException>(() => margin.SetValue(BookmarkMargin.IconBrushProperty, null!));
		Assert.ThrowsExactly<ArgumentException>(() => margin.SetValue(BookmarkMargin.IconGeometryProperty, null!));
	}

	[TestMethod]
	public void TryHandleClickAt_ClickOnMarginRow_TogglesBookmarkForThatLine()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var tracker = new BookmarkTracker(() => document);
		var margin = new BookmarkMargin(tracker);
		var editor = new TextEditor { Document = document };

		using var host = new HostedMarginTestHost(editor, margin);

		VisualLine secondLine = editor.TextArea.TextView.VisualLines.First(line => line.FirstDocumentLine.LineNumber == 2);

		double clickY = secondLine.GetTextLineVisualYPosition(secondLine.TextLines[0], VisualYPosition.TextTop) + 2.0;

		bool toggled = margin.TryHandleClickAt(new Point(4.0, clickY));

		Assert.IsTrue(toggled);
		Assert.HasCount(1, tracker.GetMarkedLineNumbers());
		Assert.AreEqual(2, tracker.GetMarkedLineNumbers()[0]);

		// A second click on the same row removes the bookmark again.
		bool toggledOff = margin.TryHandleClickAt(new Point(4.0, clickY));

		Assert.IsTrue(toggledOff);
		Assert.IsEmpty(tracker.GetMarkedLineNumbers());
	}

	[TestMethod]
	public void TryHandleClickAt_ClickOutsideVisualLines_TogglesNothing()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var tracker = new BookmarkTracker(() => document);
		var margin = new BookmarkMargin(tracker);
		var editor = new TextEditor { Document = document };

		using var host = new HostedMarginTestHost(editor, margin);

		bool toggled = margin.TryHandleClickAt(new Point(4.0, 100000.0));

		Assert.IsFalse(toggled);
		Assert.IsEmpty(tracker.GetMarkedLineNumbers());
	}

	[TestMethod]
	public void OnRender_IconBounds_ScaleWithTextViewFontSize()
	{
		(int MinColumn, int MaxColumn) RenderSingleIconBounds(double fontSize)
		{
			var document = new TextDocument(string.Join("\r\n", Enumerable.Repeat("line", 20)));
			var tracker = new BookmarkTracker(() => document);
			var margin = new BookmarkMargin(tracker);
			var editor = new TextEditor { Document = document, FontSize = fontSize };

			tracker.ToggleBookmark(document.GetLineByNumber(1).Offset);

			using var host = new HostedMarginTestHost(editor, margin);

			// The premise: the hosted margin is arranged to a positive width, so the scan covers a real strip
			// and the scaled-bounds comparison below is not vacuous.
			Assert.IsGreaterThan(0.0, GetArrangedWidth(margin), "The margin must be arranged for this test.");

			var bitmap = TestBitmapRendering.RenderToBitmap(editor);

			bool found = TestBitmapRendering.TryGetColorColumnBounds(
				bitmap,
				stripWidth: (int)Math.Ceiling(GetArrangedWidth(margin)),
				target: Color.FromRgb(0xE6, 0xA2, 0x3C),
				out int minColumn,
				out int maxColumn
			);

			Assert.IsTrue(found, "Expected an amber bookmark icon in the margin.");
			return (minColumn, maxColumn);
		}

		(int MinColumn, int MaxColumn) smallIcon = RenderSingleIconBounds(8.0);
		(int MinColumn, int MaxColumn) largeIcon = RenderSingleIconBounds(24.0);

		int smallIconWidth = smallIcon.MaxColumn - smallIcon.MinColumn;
		int largeIconWidth = largeIcon.MaxColumn - largeIcon.MinColumn;

		Assert.IsGreaterThan(0, smallIconWidth);
		Assert.IsGreaterThan(0, largeIconWidth);

		Assert.IsLessThan(10, smallIconWidth, "Expected a small-font icon narrower than the authored 10-DIP icon.");
		Assert.IsGreaterThan(10, largeIconWidth, "Expected a large-font icon wider than the authored 10-DIP icon.");

		Assert.IsGreaterThan(smallIconWidth, largeIconWidth);
	}

	[TestMethod]
	public void OnRender_SmallFont_AdjacentBookmarkIconsDoNotOverlap()
	{
		var document = new TextDocument(string.Join("\r\n", Enumerable.Repeat("line", 20)));
		var tracker = new BookmarkTracker(() => document);
		var margin = new BookmarkMargin(tracker);
		var editor = new TextEditor { Document = document, FontSize = 5.0 };

		tracker.ToggleBookmark(document.GetLineByNumber(1).Offset);
		tracker.ToggleBookmark(document.GetLineByNumber(2).Offset);

		using var host = new HostedMarginTestHost(editor, margin);

		double lineHeight = editor.TextArea.TextView.DefaultLineHeight;

		// The premise: at this font size a line is shorter than the authored 9-DIP-tall
		// icon (with margin for the engine's own line-height ratio), so an unscaled icon would
		// overlap the neighboring line's icon.
		Assert.IsLessThan(9.0, lineHeight, "The test font must produce lines shorter than the authored icon.");

		var bitmap = TestBitmapRendering.RenderToBitmap(editor);

		int segments = TestBitmapRendering.CountColorRowSegments(
			bitmap,
			(int)Math.Ceiling(GetArrangedWidth(margin)),
			Color.FromRgb(0xE6, 0xA2, 0x3C)
		);

		Assert.AreEqual(2, segments, "Expected one separated icon segment per bookmarked line.");
	}

	[TestMethod]
	public void OnRender_WrappedMarkedLine_DrawsASingleIcon()
	{
		// A single document line long enough to wrap several times at the hosted width.
		var document = new TextDocument(string.Join(" ", Enumerable.Repeat("word", 300)));
		var tracker = new BookmarkTracker(() => document);
		var margin = new BookmarkMargin(tracker);

		var editor = new TextEditor
		{
			Document = document,
			FontSize = 12.0,
			WordWrap = true,
		};

		tracker.ToggleBookmark(document.GetLineByNumber(1).Offset);

		using var host = new HostedMarginTestHost(editor, margin);

		IReadOnlyList<VisualLine> visualLines = editor.TextArea.TextView.VisualLines;

		// The premise: word wrap keeps the document line as one visual line made of several
		// text lines, so the icon must not repeat per wrapped segment.
		Assert.HasCount(1, visualLines);
		Assert.IsGreaterThan(1, visualLines[0].TextLines.Count);

		var bitmap = TestBitmapRendering.RenderToBitmap(editor);

		int segments = TestBitmapRendering.CountColorRowSegments(
			bitmap,
			(int)Math.Ceiling(GetArrangedWidth(margin)),
			Color.FromRgb(0xE6, 0xA2, 0x3C)
		);

		// The icon is drawn once and centered on the wrapped line instead of repeating per segment.
		Assert.AreEqual(1, segments, "Expected a single bookmark icon for the wrapped line.");
	}

	[TestMethod]
	public void SourceChange_AfterDispatcherPump_RepaintsTheMarker()
	{
		var document = new TextDocument(string.Join("\r\n", Enumerable.Repeat("line", 20)));
		var tracker = new BookmarkTracker(() => document);
		var margin = new BookmarkMargin(tracker);
		var editor = new TextEditor { Document = document, FontSize = 12.0 };

		using var host = new HostedMarginTestHost(editor, margin);

		// The bookmark is added while the margin is connected, so only the source notification
		// can invalidate the margin; the queued invalidation must run on the margin's dispatcher.
		tracker.ToggleBookmark(document.GetLineByNumber(1).Offset);

		var bitmap = TestBitmapRendering.PumpAndRender(editor);

		Assert.IsTrue(
			TestBitmapRendering.HasColorInLeftStrip(bitmap, 16, Color.FromRgb(0xE6, 0xA2, 0x3C)),
			"Expected the notified margin to repaint the new bookmark icon."
		);
	}

	[TestMethod]
	public void CustomBookmarkSource_DrivesRendering()
	{
		var document = new TextDocument(string.Join("\r\n", Enumerable.Repeat("line", 20)));
		var source = new StubLineStatusSource(() => document, [1]);
		var margin = new BookmarkMargin(source);
		var editor = new TextEditor { Document = document, FontSize = 12.0 };

		using var host = new HostedMarginTestHost(editor, margin);

		var bitmap = TestBitmapRendering.RenderToBitmap(editor);

		Assert.IsTrue(
			TestBitmapRendering.HasColorInLeftStrip(bitmap, 16, Color.FromRgb(0xE6, 0xA2, 0x3C)),
			"Expected an amber bookmark icon in the margin strip."
		);
	}

	[TestMethod]
	public void TryHandleClickAt_CustomSource_TogglesThroughSource()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var source = new StubLineStatusSource(() => document, []);
		var margin = new BookmarkMargin(source);
		var editor = new TextEditor { Document = document };

		using var host = new HostedMarginTestHost(editor, margin);

		VisualLine secondLine = editor.TextArea.TextView.VisualLines.First(line => line.FirstDocumentLine.LineNumber == 2);

		double clickY = secondLine.GetTextLineVisualYPosition(secondLine.TextLines[0], VisualYPosition.TextTop) + 2.0;

		bool toggled = margin.TryHandleClickAt(new Point(4.0, clickY));

		Assert.IsTrue(toggled);
		Assert.AreEqual(1, source.ToggleCalls);
		Assert.AreEqual(2, source.ToggledLineNumber);
	}

	[TestMethod]
	public void OnRender_CustomIconBrushAndGeometry_AreUsed()
	{
		var document = new TextDocument(string.Join("\r\n", Enumerable.Repeat("line", 20)));
		var tracker = new BookmarkTracker(() => document);

		var margin = new BookmarkMargin(tracker)
		{
			IconBrush = new SolidColorBrush(Colors.Red),
			IconGeometry = new RectangleGeometry(new Rect(x: 0.0, y: 0.0, width: 10.0, height: 9.0)),
		};

		var editor = new TextEditor { Document = document, FontSize = 12.0 };

		tracker.ToggleBookmark(document.GetLineByNumber(1).Offset);

		using var host = new HostedMarginTestHost(editor, margin);

		var bitmap = TestBitmapRendering.RenderToBitmap(editor);

		Assert.IsTrue(
			TestBitmapRendering.HasColorInLeftStrip(bitmap, 16, Colors.Red),
			"Expected the custom red brush to paint the custom icon geometry."
		);

		Assert.IsFalse(
			TestBitmapRendering.HasColorInLeftStrip(bitmap, 16, Color.FromRgb(0xE6, 0xA2, 0x3C)),
			"Expected the default amber icon to be replaced."
		);
	}

	[TestMethod]
	public void TryHandleClickAt_UnattachedMargin_DoesNotToggle()
	{
		var document = new TextDocument("one\r\ntwo");
		var tracker = new BookmarkTracker(() => document);
		var margin = new BookmarkMargin(tracker);

		// An unattached margin resolves no line, so the click is not handled and nothing is toggled.
		bool toggled = margin.TryHandleClickAt(new Point(4.0, 4.0));

		Assert.IsFalse(toggled);
		Assert.IsEmpty(tracker.GetMarkedLineNumbers());
	}

	[TestMethod]
	public void TryHandleClickAt_WithoutValidVisualLines_DoesNotToggle()
	{
		var document = new TextDocument("one\r\ntwo");
		var tracker = new BookmarkTracker(() => document);
		var margin = new BookmarkMargin(tracker);
		var editor = new TextEditor { Document = document };

		// The editor is never hosted or laid out, so the text view has no valid visual lines yet.
		editor.TextArea.LeftMargins.Add(margin);

		bool toggled = margin.TryHandleClickAt(new Point(4.0, 4.0));

		Assert.IsFalse(toggled);
		Assert.IsEmpty(tracker.GetMarkedLineNumbers());
	}

	[TestMethod]
	public void DrawMarker_UnarrangedMargin_CentersTheIconInTheAuthoredWidth()
	{
		var document = new TextDocument(string.Join("\r\n", Enumerable.Repeat("line", 20)));
		var margin = new BookmarkMargin(new BookmarkTracker(() => document));

		TestHost.PinDesignFontSize(margin);

		// The unarranged margin has no actual width, so the authored fallback width must be used.
		Assert.AreEqual(0.0, GetArrangedWidth(margin));

		var editor = new TextEditor { Document = document, FontSize = 12.0 };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		VisualLine firstLine = editor.TextArea.TextView.VisualLines[0];

		Rect bounds = CaptureMarkerBounds(margin, firstLine);

		// The 10-DIP icon is centered in the 16-DIP authored width: (16 - 10) / 2 = 3.
		Assert.AreEqual(3.0, bounds.X, 0.001);
		Assert.AreEqual(10.0, bounds.Width, 0.001);
	}

	[TestMethod]
	public void OnBookmarkToggleRequested_Override_ControlsTheToggleAndTheClickHandling()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var tracker = new BookmarkTracker(() => document);
		var margin = new HookMargin(tracker);
		var editor = new TextEditor { Document = document };

		using var host = new HostedMarginTestHost(editor, margin);

		VisualLine secondLine = editor.TextArea.TextView.VisualLines.First(line => line.FirstDocumentLine.LineNumber == 2);

		double clickY = secondLine.GetTextLineVisualYPosition(secondLine.TextLines[0], VisualYPosition.TextTop) + 2.0;

		// The override vetoes the toggle: the click resolves to line 2 but stays unhandled, and nothing
		// is bookmarked.
		margin.HookResult = false;

		bool vetoed = margin.TryHandleClickAt(new Point(4.0, clickY));

		Assert.AreEqual(secondLine.FirstDocumentLine.Offset, margin.LastRequestedOffset);
		Assert.IsFalse(vetoed);
		Assert.IsEmpty(tracker.GetMarkedLineNumbers());

		// The passthrough to the default implementation toggles through the source and reports the
		// request as handled.
		margin.HookResult = true;

		Assert.IsTrue(margin.TryHandleClickAt(new Point(4.0, clickY)));
		Assert.AreEqual(secondLine.FirstDocumentLine.Offset, margin.LastRequestedOffset);
		Assert.HasCount(1, tracker.GetMarkedLineNumbers());
		Assert.AreEqual(2, tracker.GetMarkedLineNumbers()[0]);
	}

	protected static BookmarkMargin CreateMargin(TextDocument document) => new(new BookmarkTracker(() => document));

	/// <summary>
	/// A bookmark margin whose toggle hook records the requested line offset and can veto the toggle without calling the default implementation.
	/// </summary>
	private sealed class HookMargin : BookmarkMargin
	{
		/// <summary>Initializes a new instance of the <see cref="HookMargin"/> class.</summary>
		/// <param name="bookmarkSource">The source that queries and toggles bookmarks.</param>
		public HookMargin(IBookmarkSource bookmarkSource)
			: base(bookmarkSource) { }

		/// <summary>Indicates whether the override delegates to the default toggle.</summary>
		public bool HookResult { get; set; } = true;

		/// <summary>The line offset of the last toggle request.</summary>
		public int? LastRequestedOffset { get; private set; }

		/// <inheritdoc/>
		protected override bool OnBookmarkToggleRequested(int documentOffset)
		{
			LastRequestedOffset = documentOffset;
			return HookResult && base.OnBookmarkToggleRequested(documentOffset);
		}
	}
}

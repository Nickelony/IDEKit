#if AVALONIAEDIT
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Nickelony.IDEKit.AvaloniaEdit.Rendering;
using Nickelony.IDEKit.Core.Notifications;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.Tests;
#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.Rendering;
using Nickelony.IDEKit.Core.Notifications;
using System.Windows;
using System.Windows.Media;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.Tests;
#endif

/// <summary>
/// The <see cref="LineStatusMarginBase"/> scenarios whose shape is the same across the editor bindings:
/// the reserved-width measure, the forward-walk render cases, the source-subscription lifecycle, the
/// coalesced visual invalidation, and the arrange invalidation on a view change.
/// </summary>
/// <remarks>
/// A binding supplies the engine-specific operations through the hooks below: running the margin's draw
/// pass, reading the arranged width, pumping a render-priority dispatcher frame, and the
/// arrange-invalidation assertion (Avalonia's <c>InvalidateVisual</c> does not invalidate the arrange
/// pass, so the mirror reports that assertion inconclusive instead).
/// </remarks>
public abstract class LineStatusMarginBaseTestsBase
{
	/// <summary>
	/// Runs the margin's draw pass so a render scenario can observe the margin's recorded draws.
	/// </summary>
	/// <param name="editor">The editor that hosts the margin.</param>
	/// <param name="margin">The margin whose draw pass is run.</param>
	protected abstract void RunDrawPass(TextEditor editor, LineStatusMarginBase margin);

	/// <summary>
	/// Gets the margin's arranged width after the host laid it out.
	/// </summary>
	/// <param name="margin">The margin to read.</param>
	/// <returns>The arranged width.</returns>
	protected abstract double GetArrangedWidth(LineStatusMarginBase margin);

	/// <summary>
	/// Pumps the dispatcher so the margin's queued render-priority invalidation runs.
	/// </summary>
	protected abstract void PumpRenderFrame();

	/// <summary>
	/// Asserts that the margin invalidated its arrange when the given view change ran.
	/// </summary>
	/// <param name="arrangeValidInHandler">Whether the margin was still arrange-valid inside the handler.</param>
	/// <param name="message">The failure message.</param>
	protected virtual void AssertArrangeInvalidated(bool arrangeValidInHandler, string message)
		=> Assert.IsFalse(arrangeValidInHandler, message);

	[TestMethod]
	public void MeasureOverride_ZeroMarginWidth_ReservesNoWidth()
	{
		var margin = new RecordingMargin { MarginWidth = 0.0 };

		TestHost.PinDesignFontSize(margin);
		margin.Measure(new Size(100.0, 100.0));

		Assert.AreEqual(0.0, margin.DesiredSize.Width);
	}

	[TestMethod]
	public void MeasureOverride_ScalesReservedWidthWithFontSize()
	{
		var margin = new RecordingMargin { MarginWidth = 10.0 };

		TestHost.PinDesignFontSize(margin, 24.0);
		margin.Measure(new Size(100.0, 100.0));

		// The reserved width is authored for the 12-DIP design font.
		Assert.AreEqual(20.0, margin.DesiredSize.Width);
	}

	[TestMethod]
	public void OnRender_DrawsMarkerForMarkedLine()
	{
		var document = CreateDocument(40);
		var margin = new RecordingMargin();
		var editor = new TextEditor { Document = document, FontSize = 12.0 };

		using var host = new HostedMarginScope(editor, margin);

		margin.SetMarkedLines([3]);
		margin.InvalidateVisual();

		RunDrawPass(editor, margin);

		Assert.HasCount(1, margin.Draws);
		Assert.AreEqual(3, margin.Draws[0].LineNumber);
		Assert.IsGreaterThan(0.0, margin.Draws[0].VisualTop);
	}

	[TestMethod]
	public void OnRender_MarkedLinesBeforeViewport_AreSkippedByTheForwardWalk()
	{
		var document = CreateDocument(400);
		var margin = new RecordingMargin();
		var editor = new TextEditor { Document = document, FontSize = 12.0 };

		using var host = new HostedMarginScope(editor, margin);

		editor.ScrollToLine(299);
		editor.UpdateLayout();

		IReadOnlyList<VisualLine> visualLines = editor.TextArea.TextView.VisualLines;

		Assert.IsNotEmpty(visualLines);

		int firstVisibleLineNumber = visualLines[0].FirstDocumentLine.LineNumber;

		// The premise: the viewport starts below the document start, so the marked line above
		// it must be skipped by the forward-only walk while the first visible line is drawn.
		Assert.IsGreaterThan(1, firstVisibleLineNumber, "The scroll position must hide the first document line.");

		margin.SetMarkedLines([firstVisibleLineNumber - 1, firstVisibleLineNumber]);
		margin.InvalidateVisual();

		RunDrawPass(editor, margin);

		Assert.HasCount(1, margin.Draws);
		Assert.AreEqual(firstVisibleLineNumber, margin.Draws[0].LineNumber);
	}

	[TestMethod]
	public void OnRender_MarkedLinesBelowViewport_DrawNothing()
	{
		var document = CreateDocument(400);
		var margin = new RecordingMargin();
		var editor = new TextEditor { Document = document, FontSize = 12.0 };

		using var host = new HostedMarginScope(editor, margin);

		// Line 390 is far below the visible range.
		margin.SetMarkedLines([390]);
		margin.InvalidateVisual();

		RunDrawPass(editor, margin);

		Assert.IsEmpty(margin.Draws);
	}

	[TestMethod]
	public void OnRender_WrappedMarkedLine_DrawsOnceForTheWholeLine()
	{
		// A single document line long enough to wrap several times at the hosted width.
		string wrappedText = string.Join(" ", Enumerable.Repeat("word", 300));
		var document = new TextDocument(wrappedText);
		var margin = new RecordingMargin();
		var editor = new TextEditor { Document = document, FontSize = 12.0, WordWrap = true };

		using var host = new HostedMarginScope(editor, margin);

		IReadOnlyList<VisualLine> visualLines = editor.TextArea.TextView.VisualLines;

		// The premise: word wrap keeps the document line as one visual line made of several
		// text lines, so the marker must not be duplicated per wrapped segment.
		Assert.HasCount(1, visualLines);
		Assert.IsGreaterThan(1, visualLines[0].TextLines.Count);

		VisualLine wrappedLine = visualLines[0];
		double visualTop = wrappedLine.VisualTop - editor.TextArea.TextView.VerticalOffset;

		margin.SetMarkedLines([1]);
		margin.InvalidateVisual();

		RunDrawPass(editor, margin);

		// One draw for the whole wrapped line, anchored at its line box top; the marker
		// covers the remaining segments through VisualLine.Height.
		Assert.HasCount(1, margin.Draws);
		Assert.AreEqual(1, margin.Draws[0].LineNumber);
		Assert.AreEqual(visualTop, margin.Draws[0].VisualTop, 0.5);
		Assert.IsGreaterThan(editor.TextArea.TextView.DefaultLineHeight, wrappedLine.Height);
	}

	[TestMethod]
	public void OnTextViewChanged_MarginAttachedToSecondEditor_RendersForTheNewEditor()
	{
		var margin = new RecordingMargin();
		var firstDocument = CreateDocument(40);
		var firstEditor = new TextEditor { Document = firstDocument, FontSize = 12.0 };

		using (HostWindow firstHost = TestHost.ShowEditorWithMargin(firstEditor, margin))
		{
			Assert.IsNotEmpty(firstEditor.TextArea.TextView.VisualLines);

			margin.SetMarkedLines([3]);
			margin.InvalidateVisual();

			RunDrawPass(firstEditor, margin);

			Assert.HasCount(1, margin.Draws);
		}

		// Move the same margin to a second editor after it was connected once before.
		firstEditor.TextArea.LeftMargins.Remove(margin);

		var secondDocument = CreateDocument(40);
		var secondEditor = new TextEditor { Document = secondDocument, FontSize = 12.0 };

		using HostWindow secondHost = TestHost.ShowEditorWithMargin(secondEditor, margin);

		Assert.IsNotEmpty(secondEditor.TextArea.TextView.VisualLines);

		margin.Draws.Clear();
		margin.SetMarkedLines([7]);
		margin.InvalidateVisual();

		RunDrawPass(secondEditor, margin);

		// The margin repaints for the new editor and uses the new document's lines.
		Assert.HasCount(1, margin.Draws);
		Assert.AreEqual(7, margin.Draws[0].LineNumber);
	}

	[TestMethod]
	public void SetNotifyingSource_SecondCall_ThrowsInvalidOperationException()
	{
		var margin = new DoubleAssignMargin();
		var firstSource = new FixedLineStatusSource(() => new TextDocument(), []);
		var secondSource = new FixedLineStatusSource(() => new TextDocument(), []);

		Assert.ThrowsExactly<InvalidOperationException>(() => margin.AssignTwoSources(firstSource, secondSource));
	}

	[TestMethod]
	public void SetNotifyingSource_AfterConnecting_SubscribesImmediately()
	{
		var document = CreateDocument(40);
		var margin = new LateAssignMargin();
		var source = new FixedLineStatusSource(() => document, []);
		var editor = new TextEditor { Document = document, FontSize = 12.0 };

		using var host = new HostedMarginScope(editor, margin);

		// The margin is connected before the handler is registered, so the registration must subscribe
		// right away instead of silently never subscribing.
		margin.AssignSource(source);

		Assert.IsTrue(margin.IsSubscribed);
	}

	[TestMethod]
	public void SetNotifyingSource_WhenTheMarginLeavesTheView_Unsubscribes()
	{
		var document = CreateDocument(40);
		var margin = new LateAssignMargin();
		var source = new FixedLineStatusSource(() => document, []);
		var editor = new TextEditor { Document = document, FontSize = 12.0 };

		using var host = new HostedMarginScope(editor, margin);

		margin.AssignSource(source);

		Assert.IsTrue(margin.IsSubscribed, "The premise: the margin subscribes while it is connected.");

		// Leaving the view detaches the source subscription instead of leaving it live on a detached margin.
		editor.TextArea.LeftMargins.Remove(margin);

		Assert.IsFalse(margin.IsSubscribed);
	}

	[TestMethod]
	public void RejectNullValue_RejectsOnlyNull()
	{
		Assert.IsFalse(ExposedMargin.RejectNull(null));
		Assert.IsTrue(ExposedMargin.RejectNull(new object()));
	}

	[TestMethod]
	public void GetEffectiveMarginWidth_BeforeArrange_FallsBackToTheScaledReservedWidth()
	{
		var margin = new ExposedMargin { MarginWidth = 10.0 };

		TestHost.PinDesignFontSize(margin);

		// The margin has no arranged width yet, so the reserved width scaled by the font is used.
		Assert.AreEqual(10.0, margin.EffectiveMarginWidth);
	}

	[TestMethod]
	public void GetEffectiveMarginWidth_AfterArrange_UsesTheArrangedWidth()
	{
		var document = CreateDocument(40);
		var margin = new ExposedMargin { MarginWidth = 10.0 };
		var editor = new TextEditor { Document = document, FontSize = 12.0 };

		using var host = new HostedMarginScope(editor, margin);

		editor.UpdateLayout();

		// Once the margin has an arranged width, that width wins over the reserved width.
		Assert.IsGreaterThan(0.0, GetArrangedWidth(margin));
		Assert.AreEqual(GetArrangedWidth(margin), margin.EffectiveMarginWidth);
	}

	[TestMethod]
	public void QueueVisualInvalidation_CoalescesWhileQueuedAndReleasesTheGateWhenItRuns()
	{
		var margin = new RecordingMargin();

		margin.QueueInvalidation();
		margin.QueueInvalidation();

		// Notifications that arrive while an invalidation is queued coalesce into that one invalidation.
		Assert.IsTrue(margin.IsInvalidationQueued);

		PumpRenderFrame();

		// The gate is released when the queued action runs, so a later notification queues again.
		Assert.IsFalse(margin.IsInvalidationQueued);

		margin.QueueInvalidation();

		Assert.IsTrue(margin.IsInvalidationQueued);
	}

	[TestMethod]
	public void VisualLinesChanged_InvalidatesTheMarginArrange()
	{
		var document = CreateDocument(40);
		var margin = new RecordingMargin();
		var editor = new TextEditor { Document = document, FontSize = 12.0 };

		using var host = new HostedMarginScope(editor, margin);

		editor.UpdateLayout();

		Assert.IsTrue(margin.IsArrangeValid, "The premise: the margin is arrange-valid before the change.");

		// The margin subscribed to the view's visual lines when it connected; this subscription is added
		// afterwards, so it runs after the margin's own handler and observes the state that handler produced.
		int raisedCount = 0;
		bool arrangeValidInHandler = true;

		editor.TextArea.TextView.VisualLinesChanged += (_, _) =>
		{
			raisedCount++;
			arrangeValidInHandler = margin.IsArrangeValid;
		};

		// The edit forces the view to regenerate its visual lines on the next layout pass.
		document.Insert(0, "added\r\n");
		editor.UpdateLayout();

		Assert.IsGreaterThan(0, raisedCount, "The premise: the edit must raise VisualLinesChanged.");
		AssertArrangeInvalidated(arrangeValidInHandler, "The margin must invalidate its arrange when the visual lines change.");
	}

	[TestMethod]
	public void ScrollOffsetChanged_InvalidatesTheMarginArrange()
	{
		var document = CreateDocument(400);
		var margin = new RecordingMargin();
		var editor = new TextEditor { Document = document, FontSize = 12.0 };

		using var host = new HostedMarginScope(editor, margin);

		editor.UpdateLayout();

		Assert.IsTrue(margin.IsArrangeValid, "The premise: the margin is arrange-valid before the scroll.");

		int raisedCount = 0;
		bool arrangeValidInHandler = true;

		editor.TextArea.TextView.ScrollOffsetChanged += (_, _) =>
		{
			raisedCount++;
			arrangeValidInHandler = margin.IsArrangeValid;
		};

		// The scroll viewer applies the requested offset during the next layout pass, so the offset
		// change (and its notification) arrives from the layout pass rather than from the scroll call.
		editor.ScrollToLine(300);
		editor.UpdateLayout();

		Assert.IsGreaterThan(0, raisedCount, "The premise: the scroll must raise ScrollOffsetChanged.");
		AssertArrangeInvalidated(arrangeValidInHandler, "The margin must invalidate its arrange when the scroll offset changes.");
	}

	private static TextDocument CreateDocument(int lineCount)
		=> new(string.Join("\r\n", Enumerable.Repeat("line", lineCount)));

	private sealed class ExposedMargin : LineStatusMarginBase
	{
		public static bool RejectNull(object? value) => RejectNullValue(value);

		public double EffectiveMarginWidth => GetEffectiveMarginWidth();

		protected override IReadOnlyList<int> GetMarkedLineNumbers() => [];

		protected internal override void DrawMarker(DrawingContext drawingContext, VisualLine visualLine, double visualTop)
		{ }
	}

	private sealed class LateAssignMargin : LineStatusMarginBase
	{
		public bool IsSubscribed => IsSourceSubscribed;

		public void AssignSource(IChangeNotificationSource source)
			=> SetNotifyingSource(source);

		protected override IReadOnlyList<int> GetMarkedLineNumbers() => [];

		protected internal override void DrawMarker(DrawingContext drawingContext, VisualLine visualLine, double visualTop)
		{ }
	}

	private sealed class DoubleAssignMargin : LineStatusMarginBase
	{
		public void AssignTwoSources(IChangeNotificationSource first, IChangeNotificationSource second)
		{
			SetNotifyingSource(first);
			SetNotifyingSource(second);
		}

		protected override IReadOnlyList<int> GetMarkedLineNumbers() => [];

		protected internal override void DrawMarker(DrawingContext drawingContext, VisualLine visualLine, double visualTop)
		{ }
	}

	private sealed class RecordingMargin : LineStatusMarginBase
	{
		private IReadOnlyList<int> _markedLines = [];

		public List<(int LineNumber, double VisualTop)> Draws { get; } = [];

		public void SetMarkedLines(IReadOnlyList<int> lineNumbers)
			=> _markedLines = [.. lineNumbers];

		public void QueueInvalidation()
			=> QueueVisualInvalidation();

		protected override IReadOnlyList<int> GetMarkedLineNumbers()
			=> _markedLines;

		protected internal override void DrawMarker(DrawingContext drawingContext, VisualLine visualLine, double visualTop)
			=> Draws.Add((visualLine.FirstDocumentLine.LineNumber, visualTop));
	}
}

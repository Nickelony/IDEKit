#if AVALONIAEDIT
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Nickelony.IDEKit.AvaloniaEdit.Diagnostics;
using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.Core.Text;
using Pen = Avalonia.Media.IPen;

namespace Nickelony.IDEKit.AvaloniaEdit.Tests;

#else
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.Diagnostics;
using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.Core.Text;
using Pen = System.Windows.Media.Pen;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

#endif

// The cached underline geometry: each scenario moves one input of the cache key and leaves the others as they were.
public abstract partial class DiagnosticsRendererTestsBase
{
	private const int LineLength = 400;
	private const int LineStride = LineLength + 2;

	[TestMethod]
	public void Draw_UnchangedView_BuildsTheGeometryOnce()
	{
		(TextEditor editor, DiagnosticsRenderer renderer, HostWindow hostWindow) = ShowEditorWithUnderlines();
		using HostWindow _ = hostWindow;

		Rect first = RenderGeometryBounds(renderer, editor);

		// Moving the caret redraws the layer without laying the view out again.
		editor.TextArea.Caret.Offset = 25;

		Rect second = RenderGeometryBounds(renderer, editor);
		Rect third = RenderGeometryBounds(renderer, editor);

		Assert.AreEqual(1, renderer.GeometryBuildCount);
		Assert.AreEqual(first, second);
		Assert.AreEqual(first, third);
	}

	[TestMethod]
	public void Draw_AfterTheViewLaysOutAgain_BuildsTheGeometryAgain()
	{
		(TextEditor editor, DiagnosticsRenderer renderer, HostWindow hostWindow) = ShowEditorWithUnderlines();
		using HostWindow _ = hostWindow;

		TextView textView = editor.TextArea.TextView;
		Rect before = RenderGeometryBounds(renderer, editor);

		ReadOnlyCollection<VisualLine> visualLinesBefore = textView.VisualLines;
		Vector scrollBefore = textView.ScrollOffset;
		ITextSourceVersion? versionBefore = editor.Document.Version;

		editor.FontSize = 30.0;
		textView.Redraw();
		textView.EnsureVisualLines();

		// Only the visual-line collection differs from the first pass.
		Assert.AreNotSame(visualLinesBefore, textView.VisualLines);
		Assert.AreEqual(scrollBefore, textView.ScrollOffset);
		Assert.AreSame(versionBefore, editor.Document.Version);

		Rect after = RenderGeometryBounds(renderer, editor);

		Assert.AreEqual(2, renderer.GeometryBuildCount);
		Assert.IsGreaterThan(before.Bottom, after.Bottom, "The larger font must move the underline down to the taller line box.");
	}

	[TestMethod]
	public void Draw_AfterTheViewIsResizedWithWordWrap_BuildsTheGeometryAgain()
	{
		var editor = new TextEditor
		{
			Document = new TextDocument(new string('x', LineLength)),
			FontSize = 12.0,
			WordWrap = true,
			Width = 900.0,
		};

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		TextView textView = editor.TextArea.TextView;

		DiagnosticsRenderer renderer = CreateRenderer(textView, new DiagnosticSegment(TextRange.FromBounds(0, LineLength), DiagnosticSeverity.Error));

		Rect wide = RenderGeometryBounds(renderer, editor);

		editor.Width = 300.0;
		editor.UpdateLayout();

		// The narrower view wraps the line into more rows, so the underline covers a taller area.
		Rect narrow = RenderGeometryBounds(renderer, editor);

		Assert.AreEqual(2, renderer.GeometryBuildCount);
		Assert.IsGreaterThan(wide.Height, narrow.Height);
	}

	[TestMethod]
	public void Draw_AfterAHorizontalScrollBeforeTheNextLayout_BuildsTheGeometryAgain()
	{
		(TextEditor editor, DiagnosticsRenderer renderer, HostWindow hostWindow) = ShowEditorWithUnderlines();
		using HostWindow _ = hostWindow;

		TextView textView = editor.TextArea.TextView;
		Rect before = RenderGeometryBounds(renderer, editor);

		ReadOnlyCollection<VisualLine> visualLinesBefore = textView.VisualLines;
		ITextSourceVersion? versionBefore = editor.Document.Version;

		ScrollWithoutLayout(textView, 100.0, 0.0);

		// The scroll offset moves before the view lays out again, so only that input differs from the first pass.
		Assert.IsGreaterThan(50.0, textView.ScrollOffset.X, "The view must have scrolled.");
		Assert.IsTrue(textView.VisualLinesValid);
		Assert.AreSame(visualLinesBefore, textView.VisualLines);
		Assert.AreSame(versionBefore, editor.Document.Version);

		Rect after = RenderGeometryBounds(renderer, editor);

		Assert.AreEqual(2, renderer.GeometryBuildCount);

		Assert.AreEqual(
			expected: before.X - textView.ScrollOffset.X,
			actual: after.X,
			delta: 0.5,
			message: "The underline must follow the scrolled text."
		);
	}

	[TestMethod]
	public void Draw_AfterAVerticalScrollBeforeTheNextLayout_BuildsTheGeometryAgain()
	{
		(TextEditor editor, DiagnosticsRenderer renderer, HostWindow hostWindow) = ShowEditorWithUnderlines();
		using HostWindow _ = hostWindow;

		TextView textView = editor.TextArea.TextView;
		Rect before = RenderGeometryBounds(renderer, editor);

		ReadOnlyCollection<VisualLine> visualLinesBefore = textView.VisualLines;
		ITextSourceVersion? versionBefore = editor.Document.Version;

		ScrollWithoutLayout(textView, 0.0, 60.0);

		Assert.IsGreaterThan(30.0, textView.ScrollOffset.Y, "The view must have scrolled.");
		Assert.IsTrue(textView.VisualLinesValid);
		Assert.AreSame(visualLinesBefore, textView.VisualLines);
		Assert.AreSame(versionBefore, editor.Document.Version);

		Rect after = RenderGeometryBounds(renderer, editor);

		Assert.AreEqual(2, renderer.GeometryBuildCount);

		Assert.AreEqual(
			expected: before.Y - textView.ScrollOffset.Y,
			actual: after.Y,
			delta: 0.5,
			message: "The underline must follow the scrolled text."
		);
	}

	[TestMethod]
	public void Draw_AfterADocumentChangeBelowTheView_BuildsTheGeometryAgain()
	{
		(TextEditor editor, DiagnosticsRenderer renderer, HostWindow hostWindow) = ShowEditorWithUnderlines();
		using HostWindow _ = hostWindow;

		TextView textView = editor.TextArea.TextView;
		Rect before = RenderGeometryBounds(renderer, editor);

		ReadOnlyCollection<VisualLine> visualLinesBefore = textView.VisualLines;
		Vector scrollBefore = textView.ScrollOffset;
		ITextSourceVersion? versionBefore = editor.Document.Version;

		editor.Document.Insert(editor.Document.TextLength, "tail");

		// The edit lies past the last visual line, so the view keeps its lines and only the document version differs.
		Assert.AreNotSame(versionBefore, editor.Document.Version);
		Assert.IsTrue(textView.VisualLinesValid);
		Assert.AreSame(visualLinesBefore, textView.VisualLines);
		Assert.AreEqual(scrollBefore, textView.ScrollOffset);

		Rect after = RenderGeometryBounds(renderer, editor);

		Assert.AreEqual(2, renderer.GeometryBuildCount);
		Assert.AreEqual(before, after);
	}

	[TestMethod]
	public void Draw_AfterApplyingASetWithAnotherSeverity_DrawsTheNewSeverity()
	{
		var editor = new TextEditor { Document = new TextDocument(new string('x', 40)), FontSize = 12.0 };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		DiagnosticsRenderer renderer = CreateRenderer(
			editor.TextArea.TextView,
			new DiagnosticSegment(TextRange.FromBounds(0, 40), DiagnosticSeverity.Error)
		);

		GeometryDrawing errorDrawing = RenderSingleDrawing(renderer, editor.TextArea.TextView);

		Assert.AreSame(renderer.ErrorPen, errorDrawing.Pen);

		renderer.ApplySegments([new DiagnosticSegment(TextRange.FromBounds(0, 40), DiagnosticSeverity.Hint)]);

		// The range, the document, the layout and the scroll offset are those of the first pass; only the applied set differs.
		GeometryDrawing hintDrawing = RenderSingleDrawing(renderer, editor.TextArea.TextView);

		Assert.AreEqual(2, renderer.GeometryBuildCount);
		Assert.AreSame(renderer.HintPen, hintDrawing.Pen);
		Assert.IsNotNull(hintDrawing.Geometry);
		Assert.AreEqual(0.0, hintDrawing.Geometry.Bounds.Height, "A hint underline is straight.");
	}

	[TestMethod]
	public void Draw_AfterClearingAndApplyingAgain_BuildsTheGeometryOfTheNewSet()
	{
		var editor = new TextEditor { Document = new TextDocument(new string('x', 40)), FontSize = 12.0 };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		DiagnosticsRenderer renderer = CreateRenderer(
			editor.TextArea.TextView,
			new DiagnosticSegment(TextRange.FromBounds(0, 40), DiagnosticSeverity.Error)
		);

		_ = RenderSingleDrawing(renderer, editor.TextArea.TextView);

		renderer.ClearSegments();

		Assert.IsNull(Render(renderer, editor.TextArea.TextView));

		renderer.ApplySegments([new DiagnosticSegment(TextRange.FromBounds(0, 40), DiagnosticSeverity.Warning)]);

		GeometryDrawing warningDrawing = RenderSingleDrawing(renderer, editor.TextArea.TextView);

		Assert.AreEqual(2, renderer.GeometryBuildCount);
		Assert.AreSame(renderer.WarningPen, warningDrawing.Pen);
	}

	[TestMethod]
	public void Draw_AfterAssigningAPen_StrokesTheCachedGeometryWithTheNewPen()
	{
		(TextEditor editor, DiagnosticsRenderer renderer, HostWindow hostWindow) = ShowEditorWithUnderlines();
		using HostWindow _ = hostWindow;

		TextView textView = editor.TextArea.TextView;
		Rect before = RenderGeometryBounds(renderer, editor);

		Pen magenta = CreateFrozenPen(Colors.Magenta);
		renderer.ErrorPen = magenta;

		DrawingGroup? drawing = Render(renderer, textView);

		Assert.IsNotNull(drawing);
		Assert.AreSame(magenta, ((GeometryDrawing)drawing.Children[0]).Pen);

		// The geometry holds no pen, so the new pen applies without building it again.
		Assert.AreEqual(1, renderer.GeometryBuildCount);
		Assert.AreEqual(before, ((GeometryDrawing)drawing.Children[0]).Geometry!.Bounds);
	}

	private (TextEditor Editor, DiagnosticsRenderer Renderer, HostWindow HostWindow) ShowEditorWithUnderlines()
	{
		string text = string.Join("\r\n", Enumerable.Repeat(new string('x', LineLength), 300));
		var editor = new TextEditor { Document = new TextDocument(text), FontSize = 12.0 };

		HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		// One error underline on each of the first 100 lines, so the visible lines all carry one.
		DiagnosticsRenderer renderer = CreateRenderer(
			editor.TextArea.TextView,
			[.. Enumerable.Range(0, 100).Select(line => new DiagnosticSegment(new TextRange((line * LineStride) + 10, 20), DiagnosticSeverity.Error))]
		);

		return (editor, renderer, hostWindow);
	}

	private static void ScrollWithoutLayout(TextView textView, double horizontalOffset, double verticalOffset)
	{
#if AVALONIAEDIT
		((IScrollable)textView).Offset = new Vector(horizontalOffset, verticalOffset);
#else
		var scrollInfo = (IScrollInfo)textView;
		scrollInfo.SetHorizontalOffset(horizontalOffset);
		scrollInfo.SetVerticalOffset(verticalOffset);
#endif
	}

	private GeometryDrawing RenderSingleDrawing(DiagnosticsRenderer renderer, TextView textView)
	{
		DrawingGroup? drawing = Render(renderer, textView);

		Assert.IsNotNull(drawing, "Expected the renderer to draw an underline.");
		Assert.HasCount(1, drawing.Children);

		return (GeometryDrawing)drawing.Children[0];
	}
}

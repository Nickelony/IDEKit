#if AVALONIAEDIT
using Avalonia.Media;
using AvaloniaEdit;
using Nickelony.IDEKit.AvaloniaEdit.Diagnostics;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Diagnostics;
using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.IntelliSense.Diagnostics;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;
#else
using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.AvalonEdit.Diagnostics;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Diagnostics;
using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.IntelliSense.Diagnostics;
using System.Windows.Media;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;
#endif

[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed class TextDiagnosticSegmentFactoryTests
{
	[TestMethod]
	public void CreateSegments_ProjectsOffsetsAndSeverity()
	{
		var diagnostics = new List<TextDiagnostic>
		{
			new(TextDiagnosticSeverity.Error, "first", 1, 5),
			new(TextDiagnosticSeverity.Hint, "second", 7, 7),
		};

		IReadOnlyList<TextDiagnosticSegment> segments = TextDiagnosticSegmentFactory.CreateSegments(diagnostics);

		Assert.HasCount(2, segments);
		Assert.AreEqual(1, segments[0].StartOffset);
		Assert.AreEqual(5, segments[0].EndOffset);
		Assert.AreEqual(TextDiagnosticSeverity.Error, segments[0].Severity);
		Assert.AreEqual(7, segments[1].StartOffset);
		Assert.AreEqual(7, segments[1].EndOffset);
		Assert.AreEqual(TextDiagnosticSeverity.Hint, segments[1].Severity);
	}

	[TestMethod]
	public void CreateSegmentProvider_ProjectsTheCurrentDiagnosticsOnEachCall()
	{
		IReadOnlyList<TextDiagnostic> current = [new TextDiagnostic(TextDiagnosticSeverity.Warning, "first", 1, 2)];

		Func<IReadOnlyList<TextDiagnosticSegment>> provider = TextDiagnosticSegmentFactory.CreateSegmentProvider(() => current);

		Assert.HasCount(1, provider());

		current = [];

		// The provider projects on each call, so a later render pass sees the updated diagnostics.
		Assert.IsEmpty(provider());
	}

	[TestMethod]
	public void CreateRenderer_DrawsTheProjectedSegments()
	{
		var editor = new TextEditor { Text = "sample" };
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		IReadOnlyList<TextDiagnostic> diagnostics = [new TextDiagnostic(TextDiagnosticSeverity.Error, "first", 1, 2)];

		// The renderer draws for the projected diagnostics and draws nothing when the projection is empty,
		// so the draw exercises the segment pipeline and not just the renderer's construction.
		int drawnWithDiagnostics = CountDrawnFigures(editor, TextDiagnosticSegmentFactory.CreateRenderer(() => diagnostics));
		int drawnWithoutDiagnostics = CountDrawnFigures(editor, TextDiagnosticSegmentFactory.CreateRenderer(() => []));

		Assert.IsTrue(drawnWithDiagnostics > 0, "A projected diagnostic must draw an underline.");
		Assert.AreEqual(0, drawnWithoutDiagnostics);
	}

	[TestMethod]
	public void CreateSegments_NullDiagnostics_ThrowsArgumentNullException()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => TextDiagnosticSegmentFactory.CreateSegments(null!));
	}

	[TestMethod]
	public void CreateSegments_NullEntry_ThrowsArgumentException()
	{
		// The direct projection keeps its fail-fast contract: a null entry is a caller error, not a
		// render-pass tolerance case (which only the provider-backed entry points cover).
		IReadOnlyList<TextDiagnostic> diagnostics = [new TextDiagnostic(TextDiagnosticSeverity.Error, "first", 1, 2), null!];

		Assert.ThrowsExactly<ArgumentException>(() => TextDiagnosticSegmentFactory.CreateSegments(diagnostics));
	}

	[TestMethod]
	public void CreateSegmentProvider_NullProvider_ThrowsArgumentNullException()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => TextDiagnosticSegmentFactory.CreateSegmentProvider(null!));
	}

	[TestMethod]
	public void CreateSegmentProvider_ProviderReturningNull_RendersNoSegments()
	{
		Func<IReadOnlyList<TextDiagnosticSegment>> provider = TextDiagnosticSegmentFactory.CreateSegmentProvider(() => null!);

		// The provider runs during the render pass, so a null result is rendered as nothing instead of
		// crashing the renderer; the direct projection still rejects null (see the factory remarks).
		IReadOnlyList<TextDiagnosticSegment> segments = provider();

		Assert.AreEqual(0, segments.Count);
	}

	[TestMethod]
	public void CreateSegmentProvider_ProviderReturningListWithNullEntry_SkipsNullEntries()
	{
		IReadOnlyList<TextDiagnostic> diagnostics =
			[new TextDiagnostic(TextDiagnosticSeverity.Error, "first", 1, 2), null!, new TextDiagnostic(TextDiagnosticSeverity.Hint, "second", 4, 6)];

		Func<IReadOnlyList<TextDiagnosticSegment>> provider = TextDiagnosticSegmentFactory.CreateSegmentProvider(() => diagnostics);

		// A provider that yields a list containing null entries must not throw inside the render pass; the
		// non-null entries still project so a partially-bad provider degrades gracefully.
		IReadOnlyList<TextDiagnosticSegment> segments = provider();

		Assert.HasCount(2, segments);
		Assert.AreEqual(1, segments[0].StartOffset);
		Assert.AreEqual(4, segments[1].StartOffset);
	}

#if AVALONIAEDIT
	private static int CountDrawnFigures(TextEditor editor, DiagnosticsRenderer renderer)
	{
		editor.TextArea.TextView.EnsureVisualLines();

		// Avalonia has no DrawingVisual/RenderOpen; a DrawingGroup records the drawn content, and an empty
		// Children collection means nothing was drawn - the same shape the base mirror renderer tests use.
		var drawing = new DrawingGroup();

		using (DrawingContext drawingContext = drawing.Open())
			renderer.Draw(editor.TextArea.TextView, drawingContext);

		return drawing.Children.Count;
	}
#else
	private static int CountDrawnFigures(TextEditor editor, DiagnosticsRenderer renderer)
	{
		editor.TextArea.TextView.EnsureVisualLines();

		var visual = new DrawingVisual();

		using (DrawingContext drawingContext = visual.RenderOpen())
		{
			renderer.Draw(editor.TextArea.TextView, drawingContext);
		}

		return visual.Drawing is DrawingGroup group ? group.Children.Count : 0;
	}
#endif
}

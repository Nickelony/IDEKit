#if AVALONIAEDIT
using Avalonia.Media;
using AvaloniaEdit.Rendering;
#else
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Rendering;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.LineStatus;

#else

namespace Nickelony.IDEKit.AvalonEdit.LineStatus;

#endif

/// <summary>Walks the marked visual lines of a line-status margin.</summary>
internal static class MarkedLineWalker
{
	/// <summary>Draws the marker of each marked visual line in the render pass.</summary>
	internal static void DrawMarkers(
		DrawingContext context,
		TextView textView,
		IReadOnlyList<int>? markedLineNumbers,
		Action<DrawingContext, VisualLine, double> drawMarker
	)
	{
		// The GetMarkedLineNumbers contract requires a non-null result. A null result draws nothing, because the read runs in the render
		// pass and a null there would throw. An exception the source throws still propagates.
		if (markedLineNumbers is null || markedLineNumbers.Count == 0)
			return;

		// A source that invalidated the view during its read would make the VisualLines access below
		// throw, so the validity is re-checked after the read.
		if (!textView.VisualLinesValid)
			return;

		int markedLineIndex = 0;

		foreach (VisualLine line in textView.VisualLines)
		{
			int lineNumber = line.FirstDocumentLine.LineNumber;

			// Marked lines are ascending, so the index only moves forward as the visual lines advance.
			// A word-wrapped document line stays a single visual line with several text lines, so it is
			// drawn once; drawMarker receives the line box's top and subclasses cover the full wrapped
			// height through VisualLine.Height.
			while (markedLineIndex < markedLineNumbers.Count && markedLineNumbers[markedLineIndex] < lineNumber)
				markedLineIndex++;

			if (markedLineIndex >= markedLineNumbers.Count)
				break;

			if (markedLineNumbers[markedLineIndex] != lineNumber)
				continue;

			double visualTop = line.VisualTop - textView.VerticalOffset;

			drawMarker(context, line, visualTop);
		}
	}
}

using System.Collections.ObjectModel;
#if AVALONIAEDIT
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
#else
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.Diagnostics;

#else

namespace Nickelony.IDEKit.AvalonEdit.Diagnostics;

#endif

/// <summary>The state of a text view and of the applied segments that a cached underline geometry was built for.</summary>
/// <remarks>
/// Every component moves or changes a rectangle that the text view reports for a segment. The visual-line collection is replaced when the view
/// lays out again, which covers text, font, wrapping, size, and flow changes. The scroll offset shifts every rectangle and changes before the view
/// lays out again. The document version moves the anchored segments, and the segment generation covers a new applied set and its severities.
/// </remarks>
internal readonly struct UnderlineCacheKey
{
	private readonly ReadOnlyCollection<VisualLine> _visualLines;

	private readonly double _scrollOffsetX;
	private readonly double _scrollOffsetY;

	private readonly ITextSourceVersion? _documentVersion;
	private readonly int _segmentGeneration;

	internal UnderlineCacheKey(
		ReadOnlyCollection<VisualLine> visualLines,
		double scrollOffsetX,
		double scrollOffsetY,
		ITextSourceVersion? documentVersion,
		int segmentGeneration
	)
	{
		_visualLines = visualLines;
		_scrollOffsetX = scrollOffsetX;
		_scrollOffsetY = scrollOffsetY;
		_documentVersion = documentVersion;
		_segmentGeneration = segmentGeneration;
	}

	/// <summary>Determines whether a geometry built for this key is valid for the other key.</summary>
	/// <param name="other">The key of the pass that asks for the geometry.</param>
	/// <returns><see langword="true"/> when every component is the same instance or value; otherwise, <see langword="false"/>.</returns>
	internal bool Matches(UnderlineCacheKey other)
	{
		return ReferenceEquals(_visualLines, other._visualLines)
			&& _scrollOffsetX.Equals(other._scrollOffsetX)
			&& _scrollOffsetY.Equals(other._scrollOffsetY)
			&& ReferenceEquals(_documentVersion, other._documentVersion)
			&& _segmentGeneration == other._segmentGeneration;
	}
}

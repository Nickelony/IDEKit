using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Nickelony.IDEKit.AvaloniaEdit.LineStatus;
using Nickelony.IDEKit.AvaloniaEdit.Media;

namespace Nickelony.IDEKit.AvaloniaEdit.Bookmarks;

/// <include file="../../../../../shared/docs/BookmarkMargin.xml" path="doc/members/member[@name='BookmarkMargin.Class']/*"/>
public partial class BookmarkMargin : LineStatusIconMarginBase
{
	private static readonly Geometry s_sampleIconGeometry = CreateIconGeometry();

	private static readonly ImmutableSolidColorBrush s_sampleIconBrush = BrushAndPenFactory.CreateBrush(
		Color.FromRgb(SampleIconRed, SampleIconGreen, SampleIconBlue)
	);

	static BookmarkMargin()
	{
		IconBrushProperty.OverrideDefaultValue<BookmarkMargin>(s_sampleIconBrush);
		IconGeometryProperty.OverrideDefaultValue<BookmarkMargin>(s_sampleIconGeometry);
	}

	/// <include file="../../../../../shared/docs/BookmarkMargin.xml" path="doc/members/member[@name='SampleIconBrush']/*"/>
	public static IBrush SampleIconBrush => s_sampleIconBrush;

	/// <include file="../../../../../shared/docs/BookmarkMargin.xml" path="doc/members/member[@name='SampleIconGeometry']/*"/>
	/// <remarks>
	/// Avalonia cannot freeze a geometry, so the member returns a copy and the shared default cannot be mutated in place.
	/// </remarks>
	public static Geometry SampleIconGeometry => s_sampleIconGeometry.Clone();

	private static StreamGeometry CreateIconGeometry()
	{
		var geometry = new StreamGeometry();

		using (StreamGeometryContext context = geometry.Open())
		{
			Point[] outline = GetIconOutline();

			context.BeginFigure(outline[0], true);

			foreach (Point vertex in outline.AsSpan(1))
				context.LineTo(vertex, true);

			context.EndFigure(true);
		}

		return geometry;
	}
}

using System.Windows;
using System.Windows.Media;
using Nickelony.IDEKit.AvalonEdit.LineStatus;
using Nickelony.IDEKit.AvalonEdit.Media;

namespace Nickelony.IDEKit.AvalonEdit.Bookmarks;

/// <include file="../../../../../shared/docs/BookmarkMargin.xml" path="doc/members/member[@name='BookmarkMargin.Class']/*"/>
public partial class BookmarkMargin : LineStatusIconMarginBase
{
	private static readonly SolidColorBrush s_sampleIconBrush = BrushAndPenFactory.CreateBrush(
		Color.FromRgb(SampleIconRed, SampleIconGreen, SampleIconBlue)
	);

	static BookmarkMargin()
	{
		IconBrushProperty.OverrideMetadata(
			typeof(BookmarkMargin),
			new FrameworkPropertyMetadata(s_sampleIconBrush, FrameworkPropertyMetadataOptions.AffectsRender)
		);

		IconGeometryProperty.OverrideMetadata(
			typeof(BookmarkMargin),
			new FrameworkPropertyMetadata(SampleIconGeometry, FrameworkPropertyMetadataOptions.AffectsRender)
		);
	}

	/// <include file="../../../../../shared/docs/BookmarkMargin.xml" path="doc/members/member[@name='SampleIconBrush']/*"/>
	public static Brush SampleIconBrush => s_sampleIconBrush;

	/// <include file="../../../../../shared/docs/BookmarkMargin.xml" path="doc/members/member[@name='SampleIconGeometry']/*"/>
	public static Geometry SampleIconGeometry { get; } = CreateIconGeometry();

	private static StreamGeometry CreateIconGeometry()
	{
		var geometry = new StreamGeometry();

		using (StreamGeometryContext context = geometry.Open())
		{
			Point[] outline = GetIconOutline();

			context.BeginFigure(outline[0], true, true);

			foreach (Point vertex in outline.AsSpan(1))
				context.LineTo(vertex, true, false);
		}

		geometry.Freeze();
		return geometry;
	}
}

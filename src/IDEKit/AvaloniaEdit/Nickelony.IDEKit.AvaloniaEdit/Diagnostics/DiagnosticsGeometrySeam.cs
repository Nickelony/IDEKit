using Avalonia;
using Avalonia.Media;

namespace Nickelony.IDEKit.AvaloniaEdit.Diagnostics;

/// <summary>Gives the diagnostics renderer its Avalonia geometry calls.</summary>
/// <remarks>
/// The stream-geometry context is the spot where the engines disagree on shape: Avalonia takes the closed flag on the figure call, the stroked flag
/// on the line call, and needs an explicit end call. The bindings declare the same members, so the shared renderer reads one set of names.
/// </remarks>
internal static class DiagnosticsGeometrySeam
{
	/// <include file="../../../../../shared/docs/DiagnosticsGeometrySeam.xml" path="doc/members/member[@name='Freeze']/*"/>
	/// <remarks>
	/// Does nothing, because a geometry is immutable once its context is closed.
	/// </remarks>
	internal static void Freeze(StreamGeometry geometry) => _ = geometry;

	/// <include file="../../../../../shared/docs/DiagnosticsGeometrySeam.xml" path="doc/members/member[@name='BeginFigure']/*"/>
	internal static void BeginFigure(StreamGeometryContext context, Point start) => context.BeginFigure(start, false);

	/// <include file="../../../../../shared/docs/DiagnosticsGeometrySeam.xml" path="doc/members/member[@name='LineTo']/*"/>
	internal static void LineTo(StreamGeometryContext context, Point point) => context.LineTo(point, true);

	/// <include file="../../../../../shared/docs/DiagnosticsGeometrySeam.xml" path="doc/members/member[@name='EndFigure']/*"/>
	internal static void EndFigure(StreamGeometryContext context) => context.EndFigure(false);
}

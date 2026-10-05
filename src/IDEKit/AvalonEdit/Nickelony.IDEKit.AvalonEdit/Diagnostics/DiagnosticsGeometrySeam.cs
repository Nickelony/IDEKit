using System.Windows;
using System.Windows.Media;

namespace Nickelony.IDEKit.AvalonEdit.Diagnostics;

/// <summary>Gives the diagnostics renderer its WPF geometry calls.</summary>
/// <remarks>
/// The renderer is shared binding source, and the stream-geometry context is the spot where the engines disagree on shape: WPF takes the closed
/// and smooth-join flags on the figure and line calls, completes the figure when the context is disposed, and freezes a finished geometry.
/// This type is the WPF half of that seam; the bindings declare the same members, so the shared renderer reads one set of names.
/// </remarks>
internal static class DiagnosticsGeometrySeam
{
	/// <include file="../../../../../shared/docs/DiagnosticsGeometrySeam.xml" path="doc/members/member[@name='Freeze']/*"/>
	internal static void Freeze(StreamGeometry geometry) => geometry.Freeze();

	/// <include file="../../../../../shared/docs/DiagnosticsGeometrySeam.xml" path="doc/members/member[@name='BeginFigure']/*"/>
	internal static void BeginFigure(StreamGeometryContext context, Point start) => context.BeginFigure(start, false, false);

	/// <include file="../../../../../shared/docs/DiagnosticsGeometrySeam.xml" path="doc/members/member[@name='LineTo']/*"/>
	internal static void LineTo(StreamGeometryContext context, Point point) => context.LineTo(point, true, false);

	/// <include file="../../../../../shared/docs/DiagnosticsGeometrySeam.xml" path="doc/members/member[@name='EndFigure']/*"/>
	/// <remarks>
	/// Does nothing, because the figure completes when the context is disposed.
	/// </remarks>
	internal static void EndFigure(StreamGeometryContext context) => _ = context;
}

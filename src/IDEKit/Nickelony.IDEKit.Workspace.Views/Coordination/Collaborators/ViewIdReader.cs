using System.Runtime.CompilerServices;

namespace Nickelony.IDEKit.Workspace.Views.Coordination;

/// <summary>Reads a view's id for diagnostics without letting a throwing accessor escape.</summary>
internal static class ViewIdReader
{
	/// <summary>Reads a view id for a report or a teardown loop.</summary>
	/// <param name="view">The view to read.</param>
	/// <returns>The view id, or a marker with a per-instance discriminator when the view cannot report a usable id.</returns>
	/// <remarks>
	/// A view that cannot report a usable id is reported with the documented marker plus a per-instance discriminator, and the remaining views
	/// are still released. The discriminator keeps two such views from collapsing into one issue when the issue list is normalized by view id.
	/// </remarks>
	internal static string Read(IWorkspaceDocumentView view)
	{
		try
		{
			string viewId = view.ViewId;

			if (!string.IsNullOrWhiteSpace(viewId))
				return viewId;
		}
		catch
		{
			// Fall through to the unidentified marker.
		}

		return $"(unidentified view {RuntimeHelpers.GetHashCode(view):X8})";
	}
}

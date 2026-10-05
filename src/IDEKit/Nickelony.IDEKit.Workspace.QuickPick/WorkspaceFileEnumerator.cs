namespace Nickelony.IDEKit.Workspace.QuickPick;

/// <summary>Enumerates the files below a folder of a workspace root.</summary>
/// <param name="directory">The folder to enumerate, which is the root or a folder below it.</param>
/// <param name="root">The workspace root that the filter and the relative paths are measured from.</param>
/// <param name="filter">Decides which files and folders stay out of the list.</param>
/// <param name="cancellationToken">A token that stops the enumeration.</param>
/// <remarks>
/// This is the seam that substitutes the file system for the tracker of the file list, so a test controls the thread and the pace of the walk.
/// The enumeration is lazy and runs on the thread that consumes it.
/// </remarks>
internal delegate IEnumerable<string> WorkspaceFileEnumerator(
	string directory,
	string root,
	WorkspaceFileFilter filter,
	CancellationToken cancellationToken
);

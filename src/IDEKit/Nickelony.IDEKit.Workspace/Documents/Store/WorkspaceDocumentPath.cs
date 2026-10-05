using Nickelony.IDEKit.Core.Paths;

namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>Rebases workspace document ids from one directory onto another.</summary>
internal static class WorkspaceDocumentPath
{
	// Returns the part of a document id below a directory id; the directory id itself yields an empty
	// relative path. LocalPath decides whether the id is below the directory, and the slice then drops the directory and its separator instead
	// of using Path.GetRelativePath, which compares ordinally on Unix.
	internal static string GetRelativePathUnderDirectory(string documentId, string directoryId, LocalPathComparisonPolicy pathComparison)
	{
		if (string.Equals(documentId, directoryId, pathComparison.Comparison))
			return string.Empty;

		if (!LocalPath.IsDirectoryOrDescendant(documentId, directoryId, pathComparison))
			throw new InvalidOperationException($"The document id '{documentId}' is not a descendant of '{directoryId}'.");

		return documentId[(Path.EndsInDirectorySeparator(directoryId) ? directoryId.Length : directoryId.Length + 1)..];
	}

	internal static string RebasePath(
		string documentId,
		string sourceDirectoryId,
		string destinationDirectoryId,
		LocalPathComparisonPolicy pathComparison
	) => Path.Combine(destinationDirectoryId, GetRelativePathUnderDirectory(documentId, sourceDirectoryId, pathComparison));
}

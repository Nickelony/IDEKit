using Nickelony.IDEKit.Core.Pathing;

namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>
/// Normalizes and rebases workspace document paths.
/// </summary>
/// <remarks>
/// <para>
/// The normalized full path is the store's document id. A trailing directory separator is trimmed so
/// one location cannot have two spellings, and identity comparison is selected separately through
/// <see cref="LocalPathComparisonPolicy"/>.
/// </para>
/// <para>
/// Identity is spelling-based: the platform's path aliases are not resolved, so a Windows short
/// (8.3) name, a trailing dot or space, or a differently Unicode-normalized spelling of the same
/// path is tracked as a second document. A host that resolves aliases canonicalizes the path before
/// it reaches the store.
/// </para>
/// </remarks>
internal static class WorkspaceDocumentPath
{
	public static string GetDirectoryPrefix(string directoryId)
		=> directoryId.EndsWith(Path.DirectorySeparatorChar)
			? directoryId
			: directoryId + Path.DirectorySeparatorChar;

	// Returns true when the document id is the directory itself or a descendant of it. The directory
	// id has no trailing separator while the descendant test uses the prefixed id, so both spellings of
	// the directory are matched with the caller's comparison.
	public static bool IsDirectoryOrDescendant(
		string documentId,
		string directoryId,
		string directoryPrefix,
		StringComparison comparison)
		=> string.Equals(documentId, directoryId, comparison)
			|| documentId.StartsWith(directoryPrefix, comparison);

	// Returns the part of a document id below a directory id; the directory id itself yields an empty
	// relative path. The validated prefix is sliced with the configured comparison instead of using
	// Path.GetRelativePath, which compares ordinally on Unix; a case-divergent descendant id on a
	// case-insensitive volume would otherwise produce a '..'-laden part and a non-normalized
	// destination id. Callers only pass ids collected as the directory itself or a descendant of it
	// under the same comparison, so anything else is a defect instead of input to recover from.
	public static string GetRelativePathUnderDirectory(
		string documentId,
		string directoryId,
		StringComparison comparison)
	{
		if (string.Equals(documentId, directoryId, comparison))
			return string.Empty;

		string prefix = GetDirectoryPrefix(directoryId);
		if (!documentId.StartsWith(prefix, comparison))
		{
			throw new InvalidOperationException(
				$"The document id '{documentId}' is not a descendant of '{directoryId}'.");
		}

		return documentId[prefix.Length..];
	}

	public static string RebasePath(
		string documentId,
		string sourceDirectoryId,
		string destinationDirectoryId,
		StringComparison comparison)
		=> Path.Combine(
			destinationDirectoryId,
			GetRelativePathUnderDirectory(documentId, sourceDirectoryId, comparison));

	/// <summary>
	/// Tries to normalize a path into a document id. Returns <see langword="false"/> for a
	/// <see langword="null"/>, blank, relative, or unnormalizable path; otherwise, the result is the
	/// fully qualified path with trailing directory separators trimmed.
	/// </summary>
	public static bool TryNormalizePath(string? filePath, out string documentId)
	{
		documentId = string.Empty;

		if (string.IsNullOrWhiteSpace(filePath))
			return false;

		// Document ids are identity paths: resolving a relative path against the process current
		// directory would bind document identity to ambient process state that a host or a headless
		// server cannot control. A relative path is invalid input.
		if (!Path.IsPathFullyQualified(filePath))
			return false;

		try
		{
			// Canonicalize trailing separators so one location has one identity: Path.GetFullPath keeps
			// a trailing separator, which would otherwise make "dir" and "dir\" two identities for the
			// same location. Root paths, including volume roots and UNC share roots, are returned
			// unchanged by the trim.
			documentId = Path.TrimEndingDirectorySeparator(Path.GetFullPath(filePath));
			return true;
		}
		catch (ArgumentException)
		{
			return false;
		}
		catch (IOException)
		{
			return false;
		}
		catch (NotSupportedException)
		{
			return false;
		}
	}
}

namespace Nickelony.IDEKit.Core.Bookmarks;

/// <summary>Persists ordered line numbers to a sidecar file next to a document.</summary>
/// <remarks>
/// Entries are one-based line numbers, one per line, written as UTF-8 without a byte-order mark and byte-identical
/// on every platform; reads honor a byte-order mark. A save replaces the sidecar through a unique temporary file in the same directory, so a failed
/// write cannot truncate the previous contents; the replace is crash-safe rather than power-loss durable. No cross-process coordination is provided
/// and all I/O is synchronous and blocking.
/// </remarks>
internal static class SidecarLineFile
{
	/// <summary>Writes the one-based line numbers to the sidecar of a document, or deletes the sidecar when the set is empty.</summary>
	/// <param name="filePath">The path of the document the sidecar belongs to.</param>
	/// <param name="lineNumbers">The one-based line numbers to persist.</param>
	/// <param name="sidecarExtension">The sidecar file extension, with or without a leading dot (for example <c>".bkmrk"</c>).</param>
	/// <returns>
	/// Either:
	/// <list type="bullet">
	/// <item><see langword="true"/> when the sidecar file was written, or when an empty set deleted it or found none to delete;</item>
	/// <item>
	/// <see langword="false"/> when the file path is blank or unusable, or the file operation fails with an I/O, authorization, or path error.
	/// </item>
	/// </list>
	/// </returns>
	/// <exception cref="ArgumentException"><paramref name="sidecarExtension"/> does not form a usable file extension.</exception>
	/// <remarks>
	/// An empty set deletes the sidecar (deleting a missing one is not an error), while a non-empty set with no number
	/// at or above one is a caller bug: the save fails and the existing sidecar is left untouched. Duplicates are written once, ascending,
	/// so the content depends on the set, not the sequence. A path the file system rejects, or one ending in a directory separator,
	/// is a failed operation; the extension is validated before the path.
	/// </remarks>
	internal static bool Save(string filePath, IEnumerable<int> lineNumbers, string sidecarExtension) =>
		SaveWithFileSystem(filePath, lineNumbers, sidecarExtension, fileSystem: SidecarFileSystem.Default);

	/// <summary>
	/// Saves the supplied line numbers through an injected file-operations implementation, so the failure mapping can be exercised
	/// without a platform-specific file-system failure.
	/// </summary>
	internal static bool SaveWithFileSystem(string filePath, IEnumerable<int> lineNumbers, string sidecarExtension, ISidecarFileSystem fileSystem)
	{
		ValidateExtension(sidecarExtension);

		if (string.IsNullOrWhiteSpace(filePath) || Path.EndsInDirectorySeparator(filePath))
			return false;

		// The enumeration is read exactly once, so a lazy caller sequence cannot be consumed twice.
		var distinctLines = new SortedSet<int>();
		bool hasInput = false;

		foreach (int lineNumber in lineNumbers)
		{
			hasInput = true;

			if (lineNumber >= 1)
				distinctLines.Add(lineNumber);
		}

		string sidecarPath = GetSidecarPath(filePath, sidecarExtension);

		if (distinctLines.Count > 0)
			return TryReplaceSidecar(sidecarPath, [.. distinctLines], fileSystem);

		// An empty set clears the sidecar, while a non-empty set whose entries are all invalid is a
		// caller bug that must not delete saved lines.
		if (hasInput)
			return false;

		try
		{
			// Deleting a missing sidecar is a no-op, so no existence check is needed; a directory at
			// the sidecar path fails and is reported as a failed operation, matching the write path.
			fileSystem.Delete(sidecarPath);
			return true;
		}
		catch (Exception exception) when (IsRecoverableFileError(exception))
		{
			return false;
		}
	}

	/// <summary>
	/// Replaces the sidecar through a temporary file so a failed write cannot truncate the previous contents; the temporary file is removed
	/// when the replacement fails.
	/// </summary>
	private static bool TryReplaceSidecar(string sidecarPath, IReadOnlyList<int> orderedLines, ISidecarFileSystem fileSystem)
	{
		// A unique temporary name keeps concurrent saves from sharing a scratch file.
		string temporaryPath = sidecarPath + "." + Path.GetRandomFileName() + ".tmp";

		try
		{
			fileSystem.WriteAllText(temporaryPath, SidecarLineSerializer.Serialize(orderedLines));
			fileSystem.Move(temporaryPath, sidecarPath);

			return true;
		}
		catch (Exception exception) when (IsRecoverableFileError(exception))
		{
			DeleteTemporaryFile(temporaryPath, fileSystem);
			return false;
		}
	}

	private static void DeleteTemporaryFile(string temporaryPath, ISidecarFileSystem fileSystem)
	{
		try
		{
			fileSystem.Delete(temporaryPath);
		}
		catch (Exception exception) when (IsRecoverableFileError(exception))
		{
			// Best-effort cleanup: the temporary file is this class's scratch file, and the next save
			// writes a fresh unique name, so a failed delete is intentionally swallowed.
		}
	}

	/// <summary>
	/// Determines whether a file operation failed because of the path, the file system, or missing permissions, which callers report as a failed
	/// operation instead of an exception.
	/// </summary>
	private static bool IsRecoverableFileError(Exception exception) =>
		exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;

	/// <summary>
	/// Attempts to restore the line numbers persisted in the sidecar file for the supplied document path and reports a storage failure separately
	/// from an empty sidecar.
	/// </summary>
	/// <param name="filePath">The path of the document the sidecar belongs to.</param>
	/// <param name="sidecarExtension">The sidecar file extension, with or without a leading dot (for example <c>".bkmrk"</c>).</param>
	/// <param name="lineNumbers">Receives the restored one-based line numbers; empty when none were stored.</param>
	/// <returns>
	/// <see langword="true"/> when the sidecar was read or does not exist as a file; <see langword="false"/> when the file path is blank or unusable,
	/// a directory occupies the sidecar path, or the file operation failed.
	/// </returns>
	/// <exception cref="ArgumentException"><paramref name="sidecarExtension"/> does not form a usable file extension.</exception>
	/// <remarks>
	/// Entries that are not integers and values below one are skipped, and the result is distinct and ascending, so a hand-edited sidecar restores
	/// the shape <see cref="Save"/> writes; parsing accepts an optional sign, surrounding whitespace, and any line terminator.
	/// </remarks>
	internal static bool TryRestore(string filePath, string sidecarExtension, out IReadOnlyList<int> lineNumbers) =>
		TryRestoreWithFileSystem(filePath, sidecarExtension, out lineNumbers, fileSystem: SidecarFileSystem.Default);

	/// <summary>
	/// Restores the persisted line numbers through an injected file-operations implementation, so the failure mapping can be exercised
	/// without a platform-specific file-system failure.
	/// </summary>
	internal static bool TryRestoreWithFileSystem(
		string filePath,
		string sidecarExtension,
		out IReadOnlyList<int> lineNumbers,
		ISidecarFileSystem fileSystem
	)
	{
		ValidateExtension(sidecarExtension);

		lineNumbers = [];

		if (string.IsNullOrWhiteSpace(filePath) || Path.EndsInDirectorySeparator(filePath))
			return false;

		string sidecarPath = GetSidecarPath(filePath, sidecarExtension);

		if (!fileSystem.FileExists(sidecarPath))
			return !fileSystem.DirectoryExists(sidecarPath);

		try
		{
			// The read materializes the file before the parse, so a failed read never hands a half-read
			// file to the serializer; an unreadable sidecar is reported as a failure with no line
			// numbers.
			lineNumbers = SidecarLineSerializer.Deserialize(fileSystem.ReadAllLines(sidecarPath));
		}
		catch (Exception exception) when (IsRecoverableFileError(exception))
		{
			return false;
		}

		return true;
	}

	/// <summary>Builds the sidecar path by appending the normalized extension to the document path.</summary>
	/// <param name="filePath">The path of the document the sidecar belongs to.</param>
	/// <param name="sidecarExtension">The sidecar file extension, with or without a leading dot (for example <c>".bkmrk"</c>).</param>
	/// <exception cref="ArgumentException">
	/// <paramref name="filePath"/> is blank or ends in a directory separator (so it names a directory),
	/// or <paramref name="sidecarExtension"/> does not form a usable file extension.
	/// </exception>
	/// <remarks>
	/// The extension is validated before the path is inspected, so an invalid extension is reported even when the path is also invalid.
	/// </remarks>
	internal static string GetSidecarPath(string filePath, string sidecarExtension)
	{
		string extension = SidecarFileExtension.Normalize(sidecarExtension);

		if (string.IsNullOrWhiteSpace(filePath))
			throw new ArgumentException("The document path must not be blank.", nameof(filePath));

		if (Path.EndsInDirectorySeparator(filePath))
			throw new ArgumentException("The document path must name a file, not a directory (it ends in a directory separator).", nameof(filePath));

		return filePath + (extension[0] == '.' ? extension : "." + extension);
	}

	/// <summary>Validates a sidecar file extension without building a path.</summary>
	/// <param name="sidecarExtension">The extension to validate, with or without a leading dot.</param>
	/// <exception cref="ArgumentException"><paramref name="sidecarExtension"/> is blank or does not form a usable file extension.</exception>
	/// <remarks>
	/// Rejects an unusable extension at a configuration boundary, so the failure surfaces when the configuration is read instead of when a save
	/// or restore builds its sidecar path. Validation is platform-independent: the same extensions are accepted on every operating system.
	/// </remarks>
	internal static void ValidateExtension(string sidecarExtension) => SidecarFileExtension.Normalize(sidecarExtension);
}

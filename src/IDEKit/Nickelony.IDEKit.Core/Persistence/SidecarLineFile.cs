namespace Nickelony.IDEKit.Core.Persistence;

/// <summary>
/// Persists ordered line numbers to a sidecar file next to a document.
/// </summary>
/// <remarks>
/// <para>
/// This is the file kernel for line-marker persistence. Callers own what the line numbers mean
/// (for example bookmarks or breakpoints), and the sidecar extension is a required caller input:
/// entries are saved one per line as one-based line numbers (invariant culture, a single <c>\n</c>
/// line terminator), and an empty set deletes the sidecar instead of writing an empty file.
/// </para>
/// <para>
/// A non-empty set that contains no valid line number is a caller bug: the save fails and the
/// existing sidecar is left untouched.
/// </para>
/// <para>
/// Save reports a blank document path as a failed operation and <see cref="TryRestore"/> as a
/// failure too, while <see cref="GetSidecarPath"/> rejects it with an exception because it cannot
/// build a path. A path that ends in a directory separator names a directory rather than a file, so
/// it is rejected the same way instead of silently placing the sidecar inside that directory.
/// </para>
/// <para>
/// Files are written as UTF-8 without a byte-order mark, and the content is byte-identical on every
/// platform; reads honor a byte-order mark and otherwise assume UTF-8. All I/O is synchronous and
/// blocking, and the methods take no cancellation token.
/// </para>
/// <para>
/// A successful save replaces the sidecar through a unique temporary file in the same directory, so
/// a failed write cannot truncate the previous contents; the temporary file is deleted when the
/// replacement fails. The replace is crash-safe rather than power-loss durable: it does not flush
/// the file to disk before the move, and it does not preserve the previous file's access control
/// list or attributes.
/// </para>
/// <para>
/// No cross-process coordination is provided: two processes that save the same sidecar are
/// last-writer-wins, a reader can observe either the old or the new contents, and a save that a
/// destination another process holds open without sharing rejects is reported as a failed
/// operation.
/// </para>
/// <para>
/// The text format lives in <see cref="SidecarLineSerializer"/> and the extension policy in
/// <see cref="SidecarFileExtension"/>; the file operations are injected
/// (<see cref="ISidecarFileOperations"/>, internal) so the failure mapping is testable on every
/// operating system.
/// </para>
/// </remarks>
public static class SidecarLineFile
{
	/// <summary>
	/// Saves the supplied line numbers to a sidecar file.
	/// </summary>
	/// <remarks>
	/// <para>
	/// An empty set deletes the sidecar instead of writing an empty file; deleting a missing sidecar
	/// is not an error, while a directory at the sidecar path is reported as a failed operation. Line
	/// numbers below 1 are ignored while at least one valid number remains. A non-empty set whose
	/// entries are all below 1 is a caller bug: the save fails and the existing sidecar is left
	/// untouched, so a degenerate set cannot delete saved lines.
	/// </para>
	/// <para>
	/// Duplicate numbers are written once, in ascending order, so the file content depends on the set
	/// of numbers, not on the caller's sequence.
	/// </para>
	/// <para>
	/// A path that the file system rejects is reported as a failed operation instead of throwing.
	/// A path that ends in a directory separator is reported as a failed operation as well: it names
	/// a directory, so a sidecar for it would be misplaced. Only a <see langword="null"/> document
	/// path is rejected before the extension is validated; otherwise, the extension is validated first,
	/// so an invalid extension throws even when the path would fail the save.
	/// </para>
	/// </remarks>
	/// <param name="filePath">The path of the document the sidecar belongs to.</param>
	/// <param name="lineNumbers">The one-based line numbers to persist.</param>
	/// <param name="sidecarExtension">
	/// The sidecar file extension, with or without a leading dot (for example <c>".bkmrk"</c>).
	/// </param>
	/// <returns>
	/// Either:
	/// <list type="bullet">
	/// <item><see langword="true"/> when the sidecar file was written or deleted, or when there was nothing to persist;</item>
	/// <item><see langword="false"/> when the file path is blank or unusable, or the file operation fails with an I/O, authorization, or path error.</item>
	/// </list>
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="filePath"/>, <paramref name="lineNumbers"/>, or <paramref name="sidecarExtension"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="sidecarExtension"/> does not form a usable file extension.</exception>
	public static bool Save(string filePath, IEnumerable<int> lineNumbers, string sidecarExtension)
		=> SaveWithFileOperations(filePath, lineNumbers, sidecarExtension, SidecarFileOperations.Default);

	/// <summary>
	/// Saves the supplied line numbers through an injected file-operations implementation, so the
	/// failure mapping can be exercised without a platform-specific file-system failure.
	/// </summary>
	internal static bool SaveWithFileOperations(
		string filePath,
		IEnumerable<int> lineNumbers,
		string sidecarExtension,
		ISidecarFileOperations fileOperations)
	{
		ArgumentNullException.ThrowIfNull(filePath);
		ArgumentNullException.ThrowIfNull(lineNumbers);
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
			return TryReplaceSidecar(sidecarPath, [.. distinctLines], fileOperations);

		// An empty set clears the sidecar, while a non-empty set whose entries are all invalid is a
		// caller bug that must not delete saved lines.
		if (hasInput)
			return false;

		try
		{
			// Deleting a missing sidecar is a no-op, so no existence check is needed; a directory at
			// the sidecar path fails and is reported as a failed operation, matching the write path.
			fileOperations.Delete(sidecarPath);
			return true;
		}
		catch (Exception exception) when (IsRecoverableFileError(exception))
		{
			return false;
		}
	}

	/// <summary>
	/// Replaces the sidecar through a temporary file so a failed write cannot truncate the previous
	/// contents. The temporary file is removed when the replacement fails.
	/// </summary>
	private static bool TryReplaceSidecar(
		string sidecarPath,
		IReadOnlyList<int> orderedLines,
		ISidecarFileOperations fileOperations)
	{
		// A unique temporary name keeps concurrent saves from sharing a scratch file.
		string temporaryPath = sidecarPath + "." + Path.GetRandomFileName() + ".tmp";

		try
		{
			fileOperations.WriteAllText(temporaryPath, SidecarLineSerializer.Serialize(orderedLines));
			fileOperations.Move(temporaryPath, sidecarPath);

			return true;
		}
		catch (Exception exception) when (IsRecoverableFileError(exception))
		{
			TryDeleteTemporaryFile(temporaryPath, fileOperations);
			return false;
		}
	}

	private static void TryDeleteTemporaryFile(string temporaryPath, ISidecarFileOperations fileOperations)
	{
		try
		{
			fileOperations.Delete(temporaryPath);
		}
		catch (Exception exception) when (IsRecoverableFileError(exception))
		{
			// Best-effort cleanup: the temporary file is the caller's scratch file, and the next save
			// writes a fresh unique name, so a failed delete is intentionally swallowed.
		}
	}

	/// <summary>
	/// Determines whether a file operation failed because of the path, the file system, or missing
	/// permissions, which callers report as a failed operation instead of an exception.
	/// </summary>
	private static bool IsRecoverableFileError(Exception exception)
		=> exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;

	/// <summary>
	/// Attempts to restore the line numbers persisted in the sidecar file for the supplied document
	/// path and reports a storage failure separately from an empty sidecar.
	/// </summary>
	/// <remarks>
	/// Entries that are not integers are skipped, as are values below one. The returned line numbers
	/// are distinct and ordered ascending, so a hand-edited sidecar restores the same shape that
	/// <see cref="Save"/> writes; parsing is invariant-culture and accepts an optional sign and
	/// surrounding whitespace, and any line terminator. A missing sidecar reports success with an
	/// empty list; a blank or unusable file path, a path occupied by a directory, or a file
	/// operation that fails with an I/O, authorization, or path error, reports
	/// <see langword="false"/>, so the caller can tell a storage failure apart from "nothing was
	/// saved".
	/// </remarks>
	/// <param name="filePath">The path of the document the sidecar belongs to.</param>
	/// <param name="sidecarExtension">
	/// The sidecar file extension, with or without a leading dot (for example <c>".bkmrk"</c>).
	/// </param>
	/// <param name="lineNumbers">Receives the restored one-based line numbers; empty when none were stored.</param>
	/// <returns>
	/// <see langword="true"/> when the sidecar was read or does not exist as a file;
	/// <see langword="false"/> when the file path is blank or unusable, a directory occupies the
	/// sidecar path, or the file operation failed.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="filePath"/> or <paramref name="sidecarExtension"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="sidecarExtension"/> does not form a usable file extension.</exception>
	public static bool TryRestore(string filePath, string sidecarExtension, out IReadOnlyList<int> lineNumbers)
		=> TryRestoreWithFileOperations(filePath, sidecarExtension, out lineNumbers, SidecarFileOperations.Default);

	/// <summary>
	/// Restores the persisted line numbers through an injected file-operations implementation, so the
	/// failure mapping can be exercised without a platform-specific file-system failure.
	/// </summary>
	internal static bool TryRestoreWithFileOperations(
		string filePath,
		string sidecarExtension,
		out IReadOnlyList<int> lineNumbers,
		ISidecarFileOperations fileOperations)
	{
		ArgumentNullException.ThrowIfNull(filePath);
		ValidateExtension(sidecarExtension);

		lineNumbers = [];

		if (string.IsNullOrWhiteSpace(filePath) || Path.EndsInDirectorySeparator(filePath))
			return false;

		string sidecarPath = GetSidecarPath(filePath, sidecarExtension);

		if (!fileOperations.FileExists(sidecarPath))
			return !fileOperations.DirectoryExists(sidecarPath);

		try
		{
			// The read materializes the file before the parse, so a failed read never hands a half-read
			// file to the serializer; an unreadable sidecar is reported as a failure with no line
			// numbers.
			lineNumbers = SidecarLineSerializer.Deserialize(fileOperations.ReadAllLines(sidecarPath));
		}
		catch (Exception exception) when (IsRecoverableFileError(exception))
		{
			return false;
		}

		return true;
	}

	/// <summary>
	/// Gets the sidecar file path for the supplied document path.
	/// </summary>
	/// <remarks>
	/// The extension is validated before the path is inspected, matching <see cref="Save"/> and
	/// <see cref="TryRestore"/>: when both arguments are invalid, the extension determines the reported
	/// exception.
	/// </remarks>
	/// <param name="filePath">The path of the document the sidecar belongs to.</param>
	/// <param name="sidecarExtension">
	/// The sidecar file extension, with or without a leading dot (for example <c>".bkmrk"</c>).
	/// </param>
	/// <returns>The sidecar file path.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="filePath"/> or <paramref name="sidecarExtension"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="filePath"/> is blank or ends in a directory separator (so it names a
	/// directory), or <paramref name="sidecarExtension"/> does not form a usable file extension.
	/// </exception>
	public static string GetSidecarPath(string filePath, string sidecarExtension)
	{
		ArgumentNullException.ThrowIfNull(filePath);

		// The extension is validated before the path is inspected, so an invalid extension is the
		// reported failure even when the path is also invalid.
		string extension = SidecarFileExtension.Normalize(sidecarExtension);

		if (string.IsNullOrWhiteSpace(filePath))
			throw new ArgumentException("The document path must not be blank.", nameof(filePath));

		if (Path.EndsInDirectorySeparator(filePath))
			throw new ArgumentException("The document path must name a file, not a directory (it ends in a directory separator).", nameof(filePath));

		return filePath + (extension[0] == '.' ? extension : "." + extension);
	}

	/// <summary>
	/// Validates a sidecar file extension without building a path.
	/// </summary>
	/// <remarks>
	/// Use this to reject an unusable extension at a configuration boundary, so the failure surfaces
	/// when the configuration is read instead of when a save or restore builds its sidecar path. The
	/// rule is platform-independent: the same extensions are accepted on every operating system.
	/// </remarks>
	/// <param name="sidecarExtension">The extension to validate, with or without a leading dot.</param>
	/// <exception cref="ArgumentNullException"><paramref name="sidecarExtension"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="sidecarExtension"/> is blank or does not form a usable file extension.
	/// </exception>
	public static void ValidateExtension(string sidecarExtension)
		=> SidecarFileExtension.Normalize(sidecarExtension);
}

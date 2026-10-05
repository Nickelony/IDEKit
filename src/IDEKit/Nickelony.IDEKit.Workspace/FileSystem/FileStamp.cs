namespace Nickelony.IDEKit.Workspace.FileSystem;

/// <summary>Captures the on-disk state that detects changes to a workspace file.</summary>
/// <param name="Exists">Whether the file exists.</param>
/// <param name="Length">The byte length of the file, or <see langword="null"/> when it does not exist.</param>
/// <param name="LastWriteTimeUtc">The last write time in UTC, or <see langword="null"/> when the file does not exist.</param>
/// <param name="ContentHash">
/// The content hash of the file (SHA-256, uppercase hexadecimal with the default implementation), or <see langword="null"/> when the file
/// does not exist.
/// </param>
/// <remarks>
/// Capturing a stamp with the default file system reads and hashes the whole file, so capture once and pass the value
/// around instead of re-capturing it per check. The comparison is deliberately conservative: a rewritten file usually receives a new last-write time,
/// and a coarse timestamp (FAT/exFAT) or network share that keeps the previous time still differs through the content hash.
/// </remarks>
public readonly record struct FileStamp(bool Exists, long? Length, DateTime? LastWriteTimeUtc, string? ContentHash)
{
	/// <summary>The stamp for a file that does not exist.</summary>
	public static FileStamp Missing => new(Exists: false, Length: null, LastWriteTimeUtc: null, ContentHash: null);

	/// <summary>The stamp for a path that exists as a directory instead of a file.</summary>
	/// <remarks>
	/// A directory is not a missing file: the stamp reports <see cref="Exists"/> as <see langword="false"/> like <see cref="Missing"/>,
	/// and distinguishes itself through <see cref="IsDirectory"/> so a caller rejects the path instead of treating it as a missing file.
	/// </remarks>
	public static FileStamp Directory => new(Exists: false, Length: null, LastWriteTimeUtc: null, ContentHash: null) { IsDirectory = true };

	/// <summary>Whether the path exists as a directory instead of a file.</summary>
	/// <remarks>
	/// It is <see langword="false"/> for a path that does not exist.
	/// <see cref="Directory"/> sets it while <see cref="Exists"/> stays <see langword="false"/>, so that stamp and <see cref="Missing"/> compare
	/// as different values.
	/// </remarks>
	public bool IsDirectory { get; init; }
}

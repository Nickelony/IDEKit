using Nickelony.IDEKit.Workspace.Documents.FileSystem;

namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>
/// Identifies the text encoding used to decode or persist a workspace document.
/// </summary>
public enum TextEncodingKind
{
	/// <summary>UTF-8 encoding.</summary>
	Utf8,

	/// <summary>Little-endian UTF-16 encoding.</summary>
	Utf16LittleEndian,

	/// <summary>Big-endian UTF-16 encoding.</summary>
	Utf16BigEndian,

	/// <summary>
	/// Windows-1252 encoding. A host that selects it must enable the code-pages provider
	/// (<c>Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)</c>); the library does not
	/// register one, because that mutates process-wide state.
	/// </summary>
	Windows1252
}

/// <summary>
/// Identifies the newline convention associated with workspace document content: detected on read,
/// supplied by the caller on open defaults and replacements. The style is metadata; it does not
/// rewrite newline characters.
/// </summary>
public enum TextNewlineStyle
{
	/// <summary>Carriage return followed by line feed.</summary>
	CrLf,

	/// <summary>Line feed.</summary>
	Lf,

	/// <summary>Carriage return.</summary>
	Cr,

	/// <summary>More than one newline style.</summary>
	Mixed,

	/// <summary>No newline characters.</summary>
	None
}

/// <summary>
/// Describes the encoding, byte-order mark, and detected newline format of a workspace file.
/// </summary>
/// <param name="Encoding">The text encoding used to decode or persist the file.</param>
/// <param name="HasBom">Whether the file content starts with a byte-order mark.</param>
/// <param name="NewlineStyle">The newline convention associated with the content.</param>
public readonly record struct TextFileFormat(
	TextEncodingKind Encoding,
	bool HasBom,
	TextNewlineStyle NewlineStyle);

/// <summary>
/// Captures the on-disk state used to detect changes to a workspace file.
/// </summary>
/// <remarks>
/// For an existing file, the stamp includes its byte length, last-write time, and content hash. The
/// default implementation hashes the content with SHA-256 and formats it as uppercase hexadecimal;
/// the replacement-failure classification relies on that format. <see cref="Missing"/> represents a
/// file that does not exist, and <see cref="Directory"/> represents a path that exists as a directory
/// instead of a file. With the default
/// <see cref="LocalWorkspaceFileSystem"/>, capturing a stamp reads and hashes the whole file
/// (SHA-256). Every capture therefore costs time
/// proportional to the file size; capture a stamp once and pass it around instead of re-capturing it
/// per check.
/// The stamp comparison is deliberately conservative: a rewritten file usually receives a new
/// last-write time, so the stamp differs and the change is reported as an external conflict. When a
/// coarse timestamp (FAT/exFAT) or a network share keeps the previous time, the content hash still
/// detects the rewrite.
/// </remarks>
/// <param name="Exists">Whether the file exists.</param>
/// <param name="Length">The byte length of the file, or <see langword="null"/> when it does not exist.</param>
/// <param name="LastWriteTimeUtc">The last write time in UTC, or <see langword="null"/> when the file does not exist.</param>
/// <param name="ContentHash">The content hash of the file (SHA-256, uppercase hexadecimal with the default implementation), or <see langword="null"/> when the file does not exist.</param>
public readonly record struct FileStamp(
	bool Exists,
	long? Length,
	DateTime? LastWriteTimeUtc,
	string? ContentHash)
{
	/// <summary>
	/// Gets the stamp for a file that does not exist.
	/// </summary>
	public static FileStamp Missing => new(false, null, null, null);

	/// <summary>
	/// Gets the stamp for a path that exists as a directory instead of a file.
	/// </summary>
	/// <remarks>
	/// A directory is not a missing file: the stamp reports <see cref="Exists"/> as
	/// <see langword="false"/> like <see cref="Missing"/>, and distinguishes itself through
	/// <see cref="IsDirectory"/> so a caller rejects the path instead of treating it as a missing file.
	/// </remarks>
	public static FileStamp Directory => new(false, null, null, null) { IsDirectory = true };

	/// <summary>
	/// Gets a value indicating whether the path exists as a directory instead of a file.
	/// </summary>
	public bool IsDirectory { get; init; }
}

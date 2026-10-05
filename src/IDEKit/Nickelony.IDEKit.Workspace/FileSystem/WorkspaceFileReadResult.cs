namespace Nickelony.IDEKit.Workspace.FileSystem;

/// <summary>Contains the content or the raw bytes captured while reading a path, together with the metadata of that read.</summary>
/// <remarks>
/// <see cref="ResolveContent"/> is the single way to obtain the text and the format to adopt, so a caller cannot read <see cref="Content"/>
/// and miss <see cref="RawBytes"/>; a missing file has no on-disk format to adopt, so the caller supplies the fallback format for that case.
/// </remarks>
public sealed class WorkspaceFileReadResult
{
	private WorkspaceFileReadResult(
		string content,
		TextFileFormat fileFormat,
		FileStamp onDiskStamp,
		ReadOnlyMemory<byte>? rawBytes,
		bool isDirectory
	)
	{
		Content = content;
		FileFormat = fileFormat;
		OnDiskStamp = onDiskStamp;
		RawBytes = rawBytes;
		IsDirectory = isDirectory;
	}

	/// <summary>The result of a read of a path that does not exist: empty content, a default format, and <see cref="FileStamp.Missing"/>.</summary>
	public static WorkspaceFileReadResult Missing { get; } =
		new(content: string.Empty, fileFormat: default, onDiskStamp: FileStamp.Missing, rawBytes: null, isDirectory: false);

	/// <summary>The result of a read of a path that exists as a directory instead of a file.</summary>
	/// <remarks>
	/// The result carries the missing-file stamp so a caller can reject the path instead of treating it as a missing file,
	/// and sets <see cref="IsDirectory"/> so it can distinguish the two.
	/// </remarks>
	public static WorkspaceFileReadResult Directory { get; } =
		new(content: string.Empty, fileFormat: default, onDiskStamp: FileStamp.Missing, rawBytes: null, isDirectory: true);

	/// <summary>Creates the result of a read that produced raw bytes.</summary>
	/// <remarks>
	/// <see cref="LocalWorkspaceFileSystem"/> uses this state for an existing file so the document store can apply its selected no-BOM encoding.
	/// Callers resolve the text through <see cref="ResolveContent"/>.
	/// </remarks>
	public static WorkspaceFileReadResult FromBytes(FileStamp onDiskStamp, ReadOnlyMemory<byte> rawBytes) =>
		new(content: string.Empty, fileFormat: default, onDiskStamp, rawBytes, isDirectory: false);

	/// <summary>Creates the result of a read that already decoded the file content.</summary>
	/// <exception cref="ArgumentNullException"><paramref name="content"/> is <see langword="null"/>.</exception>
	public static WorkspaceFileReadResult FromContent(string content, TextFileFormat fileFormat, FileStamp onDiskStamp)
	{
		ArgumentNullException.ThrowIfNull(content);
		return new(content, fileFormat, onDiskStamp, rawBytes: null, isDirectory: false);
	}

	/// <summary>The decoded content, or an empty string when the content was not decoded (a raw-bytes, missing, or directory result).</summary>
	/// <remarks>
	/// A caller obtains the text through <see cref="ResolveContent"/> so it does not miss <see cref="RawBytes"/>.
	/// </remarks>
	public string Content { get; }

	/// <summary>The format captured while reading, or the default format when the read produced raw bytes.</summary>
	public TextFileFormat FileFormat { get; }

	/// <summary>The stamp captured while reading; <see cref="FileStamp.Missing"/> when the path does not exist.</summary>
	public FileStamp OnDiskStamp { get; }

	/// <summary>The raw bytes when the implementation provides them; otherwise, <see langword="null"/>.</summary>
	public ReadOnlyMemory<byte>? RawBytes { get; }

	public bool IsDirectory { get; }

	/// <summary>Resolves the text and the format to adopt for this read.</summary>
	/// <param name="fallbackFileFormat">The format to adopt for a decoded read of a missing file.</param>
	/// <param name="noBomEncodings">The candidate encodings, in order of preference, for raw bytes that carry no byte-order mark.</param>
	/// <param name="fileFormat">Receives the format to adopt for the document.</param>
	/// <returns>The text to adopt for the document.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="noBomEncodings"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="noBomEncodings"/> is empty or lists an encoding twice, and the read holds raw bytes.
	/// </exception>
	/// <exception cref="System.Text.DecoderFallbackException">
	/// Raw bytes do not decode, as <see cref="WorkspaceTextCodec.Decode"/> describes.
	/// </exception>
	/// <exception cref="NotSupportedException">
	/// Raw bytes need an encoding that the process cannot resolve, as <see cref="WorkspaceTextCodec.Decode"/> describes.
	/// </exception>
	/// <remarks>
	/// A decoded read returns <see cref="Content"/> and reports <see cref="FileFormat"/>
	/// when the file exists, or <paramref name="fallbackFileFormat"/> when it does not (a missing file has no on-disk format to adopt),
	/// and it ignores <paramref name="noBomEncodings"/>. A raw-bytes read decodes the bytes with <paramref name="noBomEncodings"/> and reports
	/// the format detected during decoding.
	/// </remarks>
	public string ResolveContent(TextFileFormat fallbackFileFormat, IReadOnlyList<TextEncoding> noBomEncodings, out TextFileFormat fileFormat)
	{
		ArgumentNullException.ThrowIfNull(noBomEncodings);

		if (RawBytes is not { } rawBytes)
		{
			fileFormat = OnDiskStamp.Exists ? FileFormat : fallbackFileFormat;
			return Content;
		}

		return WorkspaceTextCodec.Decode(rawBytes.Span, noBomEncodings, out fileFormat);
	}
}

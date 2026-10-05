using System.Buffers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace Nickelony.IDEKit.Workspace.FileSystem;

/// <summary>Reads a local file together with its stamp, and captures the stamp of a path.</summary>
/// <remarks>
/// The stamp of a read is built from the bytes that the read returned, so its length and hash describe
/// the content the caller holds even when a concurrent writer changes the file meanwhile. A directory and a missing file are reported as states
/// of their own, never as an empty file.
/// </remarks>
internal static class FileStampReader
{
	internal static async Task<WorkspaceFileReadResult> ReadAsync(string path, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		// A directory is not a missing file: reporting it as missing would let the store track a
		// directory path as a new empty document whose later writes and deletes fail.
		if (Directory.Exists(path))
			return WorkspaceFileReadResult.Directory;

		if (!File.Exists(path))
			return WorkspaceFileReadResult.Missing;

		byte[] bytes;
		string contentHash;

		try
		{
			(bytes, contentHash) = await ReadBytesAsync(path, cancellationToken).ConfigureAwait(false);
		}
		catch (FileNotFoundException)
		{
			// The file vanished between the existence probe and the open. The requested end state - no
			// file - still holds, so a missing file is reported instead of a load failure.
			return WorkspaceFileReadResult.Missing;
		}
		catch (DirectoryNotFoundException)
		{
			return WorkspaceFileReadResult.Missing;
		}

		FileStamp stamp = CreateStampFromBytes(path, bytes, contentHash, cancellationToken);
		return WorkspaceFileReadResult.FromBytes(stamp, bytes);
	}

	internal static async Task<FileStamp> CaptureAsync(string path, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		// A directory is not a missing file: reporting it as missing would let the store treat a path
		// occupied by a directory as absent and retarget a document onto it. The directory stamp mirrors
		// the directory read result so both capture paths surface the same state distinctly.
		if (Directory.Exists(path))
			return FileStamp.Directory;

		if (!File.Exists(path))
			return FileStamp.Missing;

		return await CaptureProbedFileAsync(path, cancellationToken).ConfigureAwait(false);
	}

	// Captures the stamp of a path that an existence probe found. The file can vanish between the probe and the open; the requested
	// end state - no file - still holds, so a missing stamp is reported instead of a failure.
	internal static async Task<FileStamp> CaptureProbedFileAsync(string path, CancellationToken cancellationToken)
	{
		try
		{
			return await CaptureStampFromFileAsync(path, cancellationToken).ConfigureAwait(false);
		}
		catch (FileNotFoundException)
		{
			return FileStamp.Missing;
		}
		catch (DirectoryNotFoundException)
		{
			return FileStamp.Missing;
		}
	}

	// A cheap metadata probe that only ever proves a mismatch: when the kind, length, or last-write time of the
	// path already differs from the expected stamp, the full stamps cannot be equal. An agreement proves nothing,
	// because the content hash is not part of the probe, so a write still re-checks after staging. Reading metadata
	// costs a few stat calls instead of a full read and hash of the file, and it succeeds while another process
	// holds the file open without read sharing.
	internal static bool ShapeDiffers(string path, FileStamp expectedStamp)
	{
		if (Directory.Exists(path) != expectedStamp.IsDirectory)
			return true;

		if (expectedStamp.IsDirectory)
			return false;

		bool fileExists = File.Exists(path);

		if (fileExists != expectedStamp.Exists)
			return true;

		if (!fileExists)
			return false;

		try
		{
			return new FileInfo(path).Length != expectedStamp.Length || File.GetLastWriteTimeUtc(path) != expectedStamp.LastWriteTimeUtc;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			// The file changed or became unreadable between the existence probe and the metadata
			// read: report a difference so the rejection path captures the observed state.
			return true;
		}
	}

	// Reads the file into a pre-sized buffer instead of growing a MemoryStream and copying it again
	// with ToArray, and hashes the bytes while reading so the content is scanned once; a concurrent
	// writer can make the final length differ from the initial one.
	private static async Task<(byte[] Bytes, string ContentHash)> ReadBytesAsync(string path, CancellationToken cancellationToken)
	{
		FileStream stream = new(
			path,
			mode: FileMode.Open,
			access: FileAccess.Read,
			share: FileShare.ReadWrite | FileShare.Delete,
			bufferSize: 64 * 1024,
			options: FileOptions.Asynchronous | FileOptions.SequentialScan
		);

		await using ConfiguredAsyncDisposable streamDisposal = stream.ConfigureAwait(false);

		using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		long fileLength = stream.Length;

		if (fileLength == 0)
			return ([], Convert.ToHexString(hash.GetHashAndReset()));

		if (fileLength > int.MaxValue)
			throw new IOException($"The file '{path}' is {fileLength} bytes and is too large to read into memory.");

		byte[] bytes = new byte[(int)fileLength];
		int offset = 0;

		while (offset < bytes.Length)
		{
			int read = await stream.ReadAsync(bytes.AsMemory(offset), cancellationToken).ConfigureAwait(false);

			if (read == 0)
				break;

			hash.AppendData(bytes, offset, read);
			offset += read;
		}

		return (offset == bytes.Length ? bytes : bytes[..offset], Convert.ToHexString(hash.GetHashAndReset()));
	}

	// Streams the file once and hashes while reading so the length and hash describe the same byte
	// sequence without buffering the whole file in memory.
	private static async Task<FileStamp> CaptureStampFromFileAsync(string path, CancellationToken cancellationToken)
	{
		FileStream stream = new(
			path,
			mode: FileMode.Open,
			access: FileAccess.Read,
			share: FileShare.ReadWrite | FileShare.Delete,
			bufferSize: 64 * 1024,
			options: FileOptions.Asynchronous | FileOptions.SequentialScan
		);

		await using ConfiguredAsyncDisposable streamDisposal = stream.ConfigureAwait(false);

		using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

		byte[] buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
		long length = 0;

		try
		{
			int read;

			while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
			{
				hash.AppendData(buffer, 0, read);
				length += read;
			}
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(buffer);
		}

		return new FileStamp(
			Exists: true,
			length,
			LastWriteTimeUtc: File.GetLastWriteTimeUtc(path),
			ContentHash: Convert.ToHexString(hash.GetHashAndReset())
		);
	}

	private static FileStamp CreateStampFromBytes(string path, ReadOnlyMemory<byte> bytes, string contentHash, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return new FileStamp(Exists: true, Length: bytes.Length, LastWriteTimeUtc: File.GetLastWriteTimeUtc(path), contentHash);
	}

	internal static async Task<FileStamp?> CaptureOrNullAsync(string path)
	{
		try
		{
			if (!File.Exists(path))
				return FileStamp.Missing;

			return await CaptureStampFromFileAsync(path, CancellationToken.None).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
		{
			return null;
		}
	}
}

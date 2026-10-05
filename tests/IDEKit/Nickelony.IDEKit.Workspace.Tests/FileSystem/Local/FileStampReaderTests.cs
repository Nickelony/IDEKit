using Nickelony.IDEKit.Workspace.FileSystem;
using static Nickelony.IDEKit.Workspace.Tests.FileSystemTestHelpers;

namespace Nickelony.IDEKit.Workspace.Tests;

/// <summary>
/// Covers the stamp reader directly: that a stamp describes the bytes it came with, and the states of a missing path and a directory.
/// </summary>
[TestClass]
public sealed class FileStampReaderTests
{
	public required TestContext TestContext { get; set; }

	[TestMethod]
	public async Task ReadAsync_OfAFile_ReturnsTheBytesWithAStampOfThoseBytes()
	{
		using var temp = new TemporaryDirectory();

		string path = Path.Combine(temp.Path, "file.bin");
		byte[] bytes = [.. Enumerable.Range(0, 200_000).Select(index => (byte)(index % 251))];

		await File.WriteAllBytesAsync(path, bytes, TestContext.CancellationToken);

		WorkspaceFileReadResult result = await FileStampReader.ReadAsync(path, TestContext.CancellationToken);

		Assert.IsTrue(result.OnDiskStamp.Exists);
		Assert.AreEqual(bytes.Length, result.OnDiskStamp.Length);
		Assert.AreEqual(HashOf(bytes), result.OnDiskStamp.ContentHash);
		Assert.AreEqual(File.GetLastWriteTimeUtc(path), result.OnDiskStamp.LastWriteTimeUtc);
		Assert.IsTrue(bytes.AsSpan().SequenceEqual(result.RawBytes!.Value.Span));
	}

	[TestMethod]
	public async Task ReadAsync_OfAnEmptyFile_ReturnsTheStampOfNoBytes()
	{
		using var temp = new TemporaryDirectory();
		string path = Path.Combine(temp.Path, "empty.txt");
		await File.WriteAllBytesAsync(path, [], TestContext.CancellationToken);

		WorkspaceFileReadResult result = await FileStampReader.ReadAsync(path, TestContext.CancellationToken);

		Assert.AreEqual(0, result.OnDiskStamp.Length);
		Assert.AreEqual(HashOf([]), result.OnDiskStamp.ContentHash);
		Assert.HasCount(0, result.RawBytes!.Value);
	}

	[TestMethod]
	public async Task ReadAsync_WhileAWriterAppends_ReturnsAStampOfTheBytesItReturned()
	{
		using var temp = new TemporaryDirectory();
		string path = Path.Combine(temp.Path, "growing.log");
		await File.WriteAllBytesAsync(path, new byte[1024], TestContext.CancellationToken);

		using var stopWriting = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);

		var writer = Task.Run(
			async () =>
			{
				await using FileStream stream = new(
					path,
					mode: FileMode.Append,
					access: FileAccess.Write,
					share: FileShare.ReadWrite | FileShare.Delete
				);

				byte[] chunk = new byte[7];

				while (!stopWriting.IsCancellationRequested)
				{
					await stream.WriteAsync(chunk, CancellationToken.None);
					await stream.FlushAsync(CancellationToken.None);
				}
			},

			TestContext.CancellationToken
		);

		try
		{
			for (int attempt = 0; attempt < 300; attempt++)
			{
				WorkspaceFileReadResult result = await FileStampReader.ReadAsync(path, TestContext.CancellationToken);
				ReadOnlyMemory<byte> bytes = result.RawBytes!.Value;

				Assert.AreEqual(bytes.Length, result.OnDiskStamp.Length, $"attempt {attempt}");
				Assert.AreEqual(HashOf(bytes.Span), result.OnDiskStamp.ContentHash, $"attempt {attempt}");
			}
		}
		finally
		{
			await stopWriting.CancelAsync();
			await writer;
		}
	}

	[TestMethod]
	public async Task CaptureAsync_OfAFile_EqualsTheStampOfAReadOfTheSameBytes()
	{
		using var temp = new TemporaryDirectory();
		string path = Path.Combine(temp.Path, "file.txt");
		await File.WriteAllTextAsync(path, "content", TestContext.CancellationToken);

		FileStamp captured = await FileStampReader.CaptureAsync(path, TestContext.CancellationToken);
		WorkspaceFileReadResult read = await FileStampReader.ReadAsync(path, TestContext.CancellationToken);

		Assert.AreEqual(read.OnDiskStamp, captured);
		Assert.AreEqual(HashOf("content"u8), captured.ContentHash);
	}

	[TestMethod]
	public async Task ReadAsync_OfAMissingPathAndOfADirectory_ReportsEachStateOnItsOwn()
	{
		using var temp = new TemporaryDirectory();

		WorkspaceFileReadResult missing = await FileStampReader.ReadAsync(Path.Combine(temp.Path, "absent.txt"), TestContext.CancellationToken);
		WorkspaceFileReadResult directory = await FileStampReader.ReadAsync(temp.Path, TestContext.CancellationToken);

		Assert.AreEqual(FileStamp.Missing, missing.OnDiskStamp);
		Assert.IsFalse(missing.IsDirectory);
		Assert.IsTrue(directory.IsDirectory);
	}

	[TestMethod]
	public async Task CaptureAsync_OfAMissingPathAndOfADirectory_ReportsEachStateOnItsOwn()
	{
		using var temp = new TemporaryDirectory();

		Assert.AreEqual(FileStamp.Missing, await FileStampReader.CaptureAsync(Path.Combine(temp.Path, "absent.txt"), TestContext.CancellationToken));
		Assert.AreEqual(FileStamp.Directory, await FileStampReader.CaptureAsync(temp.Path, TestContext.CancellationToken));
	}

	[TestMethod]
	public async Task CaptureProbedFileAsync_OfAFileThatVanishedAfterTheProbe_ReportsItMissingInsteadOfFailing()
	{
		using var temp = new TemporaryDirectory();

		// The probe found the path, then the file went away; the open fails with FileNotFoundException.
		Assert.AreEqual(
			FileStamp.Missing,
			await FileStampReader.CaptureProbedFileAsync(Path.Combine(temp.Path, "vanished.txt"), TestContext.CancellationToken)
		);

		// A directory that went away with it fails the open with DirectoryNotFoundException.
		Assert.AreEqual(
			FileStamp.Missing,
			await FileStampReader.CaptureProbedFileAsync(Path.Combine(temp.Path, "vanished", "file.txt"), TestContext.CancellationToken)
		);
	}

	[TestMethod]
	public async Task CaptureProbedFileAsync_OfAnUnreadableFile_StillFails()
	{
		using var temp = new TemporaryDirectory();
		string path = Path.Combine(temp.Path, "locked.txt");
		await File.WriteAllTextAsync(path, "content", TestContext.CancellationToken);

		// Only a vanished file is a state of its own; a file that exists but cannot be opened is a failure the caller must see.
		await using FileStream exclusive = new(path, mode: FileMode.Open, access: FileAccess.ReadWrite, share: FileShare.None);

		await Assert.ThrowsAsync<IOException>(() => FileStampReader.CaptureProbedFileAsync(path, TestContext.CancellationToken));
	}

	[TestMethod]
	public async Task ReadAsync_WithACanceledToken_Throws()
	{
		using var temp = new TemporaryDirectory();
		string path = Path.Combine(temp.Path, "file.txt");
		await File.WriteAllTextAsync(path, "content", TestContext.CancellationToken);

		await Assert.ThrowsAsync<OperationCanceledException>(() => FileStampReader.ReadAsync(path, new CancellationToken(canceled: true)));
	}

	[TestMethod]
	public async Task ShapeDiffers_ProvesAMismatchOfKindLengthOrWriteTimeOnly()
	{
		using var temp = new TemporaryDirectory();
		string path = Path.Combine(temp.Path, "file.txt");
		await File.WriteAllTextAsync(path, "content", TestContext.CancellationToken);
		FileStamp stamp = await FileStampReader.CaptureAsync(path, TestContext.CancellationToken);

		Assert.IsFalse(FileStampReader.ShapeDiffers(path, stamp));
		Assert.IsFalse(FileStampReader.ShapeDiffers(path, stamp with { ContentHash = "another hash" }), "The content hash is not part of the probe.");
		Assert.IsTrue(FileStampReader.ShapeDiffers(path, stamp with { Length = stamp.Length + 1 }));
		Assert.IsTrue(FileStampReader.ShapeDiffers(path, stamp with { LastWriteTimeUtc = stamp.LastWriteTimeUtc!.Value.AddSeconds(1) }));
		Assert.IsTrue(FileStampReader.ShapeDiffers(path, FileStamp.Missing));
		Assert.IsTrue(FileStampReader.ShapeDiffers(path, FileStamp.Directory));
		Assert.IsTrue(FileStampReader.ShapeDiffers(temp.Path, stamp));
		Assert.IsFalse(FileStampReader.ShapeDiffers(temp.Path, FileStamp.Directory));
		Assert.IsFalse(FileStampReader.ShapeDiffers(Path.Combine(temp.Path, "absent.txt"), FileStamp.Missing));
		Assert.IsTrue(FileStampReader.ShapeDiffers(Path.Combine(temp.Path, "absent.txt"), stamp));
	}

	[TestMethod]
	public async Task CaptureOrNullAsync_OfAMissingPathAndOfAFile_ReturnsTheStamp()
	{
		using var temp = new TemporaryDirectory();
		string path = Path.Combine(temp.Path, "file.txt");
		await File.WriteAllTextAsync(path, "content", TestContext.CancellationToken);

		Assert.AreEqual(FileStamp.Missing, await FileStampReader.CaptureOrNullAsync(Path.Combine(temp.Path, "absent.txt")));
		Assert.AreEqual(await FileStampReader.CaptureAsync(path, TestContext.CancellationToken), await FileStampReader.CaptureOrNullAsync(path));
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public async Task CaptureOrNullAsync_OfAFileThatCannotBeRead_ReturnsNull()
	{
		using var temp = new TemporaryDirectory();
		string path = Path.Combine(temp.Path, "locked.txt");
		await File.WriteAllTextAsync(path, "content", TestContext.CancellationToken);

		await using FileStream exclusive = new(path, mode: FileMode.Open, access: FileAccess.ReadWrite, share: FileShare.None);

		Assert.IsNull(await FileStampReader.CaptureOrNullAsync(path));
	}
}

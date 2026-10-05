using System.Text;
using Nickelony.IDEKit.Workspace.FileSystem;
using static Nickelony.IDEKit.Workspace.Tests.FileSystemTestHelpers;

namespace Nickelony.IDEKit.Workspace.Tests;

/// <summary>
/// Covers the atomic writer directly: that a destination never holds a partial file, that a failed write keeps the original, and that the new content
/// is never lost.
/// </summary>
[TestClass]
public sealed class AtomicFileWriterTests
{
	public required TestContext TestContext { get; set; }

	private static async Task<(string Path, FileStamp Stamp)> OriginalAsync(TemporaryDirectory temp, string text, CancellationToken cancellationToken)
	{
		string path = Path.Combine(temp.Path, "destination.txt");
		await File.WriteAllTextAsync(path, text, cancellationToken);

		return (path, await FileStampReader.CaptureAsync(path, cancellationToken));
	}

	[TestMethod]
	public async Task WriteAsync_PublishesOnlyAfterTheCompleteContentIsStaged()
	{
		using var temp = new TemporaryDirectory();
		(string destination, FileStamp expected) = await OriginalAsync(temp, "original", TestContext.CancellationToken);

		byte[] content = Encoding.UTF8.GetBytes(new string('x', 200_000));
		byte[]? stagedAtPublish = null;
		string? destinationAtPublish = null;

		var writer = new AtomicFileWriter(
			Primitives(
				replace: (staged, target) =>
				{
					stagedAtPublish = File.ReadAllBytes(staged);
					destinationAtPublish = File.ReadAllText(target);
					File.Replace(staged, target, null);
				}
			)
		);

		WorkspaceFileReplacementResult result = await writer.WriteAsync(
			destinationPath: destination,
			content,
			expectedStamp: expected,
			cancellationToken: TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceFileReplacementOutcome.Replaced, result.Outcome);
		Assert.IsTrue(content.AsSpan().SequenceEqual(stagedAtPublish), "The temporary file held the complete content when it was published.");
		Assert.AreEqual("original", destinationAtPublish, "The destination held the original until the publish.");
		Assert.IsTrue(content.AsSpan().SequenceEqual(File.ReadAllBytes(destination)));
		Assert.AreEqual(HashOf(content), result.ObservedOnDiskStamp!.Value.ContentHash);
		Assert.IsEmpty(Directory.GetFiles(temp.Path, "*.tmp"));
	}

	[TestMethod]
	public async Task WriteAsync_OfANewFile_PublishesTheCompleteContent()
	{
		using var temp = new TemporaryDirectory();
		string destination = Path.Combine(temp.Path, "created.txt");

		var writer = new AtomicFileWriter(Primitives());

		WorkspaceFileReplacementResult result = await writer.WriteAsync(
			destinationPath: destination,
			content: "created"u8.ToArray(),
			expectedStamp: FileStamp.Missing,
			cancellationToken: TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceFileReplacementOutcome.Replaced, result.Outcome);
		Assert.AreEqual("created", await File.ReadAllTextAsync(destination, TestContext.CancellationToken));
		Assert.IsEmpty(Directory.GetFiles(temp.Path, "*.tmp"));
	}

	[TestMethod]
	public async Task WriteAsync_WhenThePublishFails_KeepsTheOriginalAndRemovesTheTemporaryFile()
	{
		using var temp = new TemporaryDirectory();
		(string destination, FileStamp expected) = await OriginalAsync(temp, "original", TestContext.CancellationToken);

		var writer = new AtomicFileWriter(Primitives(replace: (_, _) => throw new IOException("The disk is full.", GenericIOFailure)));

		WorkspaceFileReplacementResult result = await writer.WriteAsync(
			destinationPath: destination,
			content: "replacement"u8.ToArray(),
			expectedStamp: expected,
			cancellationToken: TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceFileReplacementOutcome.Failed, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.WriteFailed, result.Failure!.Code);
		Assert.AreEqual("original", await File.ReadAllTextAsync(destination, TestContext.CancellationToken));
		Assert.IsEmpty(Directory.GetFiles(temp.Path, "*.tmp"));
	}

	[TestMethod]
	public async Task WriteAsync_WithACanceledToken_ReportsCanceledAndLeavesTheOriginal()
	{
		using var temp = new TemporaryDirectory();
		(string destination, FileStamp expected) = await OriginalAsync(temp, "original", TestContext.CancellationToken);

		var writer = new AtomicFileWriter(Primitives());

		WorkspaceFileReplacementResult result = await writer.WriteAsync(
			destinationPath: destination,
			content: "replacement"u8.ToArray(),
			expectedStamp: expected,
			cancellationToken: new CancellationToken(canceled: true)
		);

		Assert.AreEqual(WorkspaceFileReplacementOutcome.Canceled, result.Outcome);
		Assert.AreEqual("original", await File.ReadAllTextAsync(destination, TestContext.CancellationToken));
		Assert.IsEmpty(Directory.GetFiles(temp.Path, "*.tmp"));
	}

	[TestMethod]
	public async Task WriteAsync_WhenAReplaceLeavesTheDestinationFreeAndTheContentInTheTemporaryFile_MovesTheContentIntoPlace()
	{
		using var temp = new TemporaryDirectory();
		(string destination, FileStamp expected) = await OriginalAsync(temp, "original", TestContext.CancellationToken);

		var writer = new AtomicFileWriter(
			Primitives(
				replace: (_, target) =>
				{
					File.Delete(target);
					throw new IOException("The replacement could not be moved.", UnableToMoveReplacement);
				}
			)
		);

		WorkspaceFileReplacementResult result = await writer.WriteAsync(
			destinationPath: destination,
			content: "replacement"u8.ToArray(),
			expectedStamp: expected,
			cancellationToken: TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceFileReplacementOutcome.Replaced, result.Outcome);
		Assert.AreEqual("replacement", await File.ReadAllTextAsync(destination, TestContext.CancellationToken));
		Assert.IsEmpty(Directory.GetFiles(temp.Path, "*.tmp"));
	}

	[TestMethod]
	public async Task WriteAsync_WhenTheContentCannotBeMovedIntoPlace_KeepsTheTemporaryFileAsTheOnlyCopy()
	{
		using var temp = new TemporaryDirectory();
		(string destination, FileStamp expected) = await OriginalAsync(temp, "original", TestContext.CancellationToken);

		var writer = new AtomicFileWriter(
			Primitives(
				replace: (_, target) =>
				{
					File.Delete(target);
					throw new IOException("The replacement could not be moved.", UnableToMoveReplacement);
				},

				moveFile: (_, _) => throw new IOException("The destination is locked.", GenericIOFailure)
			)
		);

		WorkspaceFileReplacementResult result = await writer.WriteAsync(
			destinationPath: destination,
			content: "replacement"u8.ToArray(),
			expectedStamp: expected,
			cancellationToken: TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceFileReplacementOutcome.ReplacementStateUnknown, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.ReplacementStateUnknown, result.Failure!.Code);
		Assert.IsFalse(File.Exists(destination));

		string kept = Directory.GetFiles(temp.Path, "*.tmp").Single();

		Assert.AreEqual("replacement", await File.ReadAllTextAsync(kept, TestContext.CancellationToken));
		Assert.Contains(kept, result.Failure.Message);
	}

	[TestMethod]
	public async Task WriteAsync_AgainstAChangedDestination_ReportsAConflictWithoutPublishing()
	{
		using var temp = new TemporaryDirectory();
		(string destination, FileStamp expected) = await OriginalAsync(temp, "original", TestContext.CancellationToken);
		await File.WriteAllTextAsync(destination, "changed by someone else", TestContext.CancellationToken);
		int publishes = 0;

		var writer = new AtomicFileWriter(Primitives(replace: (_, _) => publishes++));

		WorkspaceFileReplacementResult result = await writer.WriteAsync(
			destinationPath: destination,
			content: "replacement"u8.ToArray(),
			expectedStamp: expected,
			cancellationToken: TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceFileReplacementOutcome.ExternalFileConflict, result.Outcome);
		Assert.AreEqual(await FileStampReader.CaptureAsync(destination, TestContext.CancellationToken), result.ObservedOnDiskStamp);
		Assert.AreEqual(0, publishes);
		Assert.AreEqual("changed by someone else", await File.ReadAllTextAsync(destination, TestContext.CancellationToken));
		Assert.IsEmpty(Directory.GetFiles(temp.Path, "*.tmp"));
	}
}

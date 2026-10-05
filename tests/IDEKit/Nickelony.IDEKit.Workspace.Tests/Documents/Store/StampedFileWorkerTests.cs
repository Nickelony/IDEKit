using System.Text;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

/// <summary>Covers the store's lock-free file work directly: reads, the stamped write, the stamp resolution and the failure classification.</summary>
[TestClass]
public sealed class StampedFileWorkerTests
{
	private const string DocumentId = "document";

	public required TestContext TestContext { get; set; }

	private sealed class TestHost
	{
		internal TestHost() => Io = new StampedFileWorker(FileSystem, Lifetime);

		internal ScriptedWorkspaceFileSystem FileSystem { get; } = new();

		internal StoreLifetime Lifetime { get; } = new();

		internal StampedFileWorker Io { get; }

		internal Task<(WorkspaceFileReplacementResult Replacement, FileStamp? ResolvedStamp, WorkspaceOperationFailure? StampFailure)> WriteAsync(
			string content = "text",
			FileStamp? expectedStamp = null
		)
		{
			return Io.WriteReplacementAsync(
				DocumentId,
				content,
				fileFormat: TestSnapshots.FileFormat,
				expectedStamp: expectedStamp ?? FileStamp.Missing,
				cancellationToken: CancellationToken.None
			);
		}
	}

	private static FileStamp StampOf(string hash) => new(Exists: true, Length: 1, LastWriteTimeUtc: DateTime.UnixEpoch, ContentHash: hash);

	private static Task<WorkspaceFileReplacementResult> Replaced(FileStamp? observed = null) =>
		Task.FromResult(new WorkspaceFileReplacementResult(WorkspaceFileReplacementOutcome.Replaced, observed));

	[TestMethod]
	public async Task ReadDocumentFileAsync_ReturnsTheFileTheFileSystemRead()
	{
		var fixture = new TestHost();
		var file = WorkspaceFileReadResult.FromContent("content", TestSnapshots.FileFormat, StampOf("a"));

		fixture.FileSystem.OnRead = (_, _) => Task.FromResult(file);

		DocumentReadResult outcome = await fixture.Io.ReadDocumentFileAsync(DocumentId, CancellationToken.None).ConfigureAwait(false);

		Assert.AreSame(file, outcome.File);
		Assert.IsNull(outcome.Failure);
		Assert.IsFalse(outcome.IsCanceled);
		Assert.AreSequenceEqual(["Read:document"], fixture.FileSystem.Calls);
	}

	[TestMethod]
	public async Task ReadDocumentFileAsync_ForADirectory_FailsWithTheDirectoryCode()
	{
		var fixture = new TestHost();

		fixture.FileSystem.OnRead = (_, _) => Task.FromResult(WorkspaceFileReadResult.Directory);

		DocumentReadResult outcome = await fixture.Io.ReadDocumentFileAsync(DocumentId, CancellationToken.None).ConfigureAwait(false);

		Assert.IsNull(outcome.File);
		Assert.AreEqual(WorkspaceOperationFailureCodes.IsDirectory, outcome.Failure?.Code);
	}

	[TestMethod]
	public async Task ReadDocumentFileAsync_WhenTheCallerTokenIsCanceled_ReportsCanceledWithoutReading()
	{
		var fixture = new TestHost();
		using var cancellation = new CancellationTokenSource();

		await cancellation.CancelAsync().ConfigureAwait(false);

		DocumentReadResult outcome = await fixture.Io.ReadDocumentFileAsync(DocumentId, cancellation.Token).ConfigureAwait(false);

		Assert.IsTrue(outcome.IsCanceled);
		Assert.IsEmpty(fixture.FileSystem.Calls);
	}

	[TestMethod]
	public async Task ReadDocumentFileAsync_WhenTheLifetimeIsCanceled_ReportsCanceledWithoutReading()
	{
		var fixture = new TestHost();

		fixture.Lifetime.Cancel();

		DocumentReadResult outcome = await fixture.Io.ReadDocumentFileAsync(DocumentId, CancellationToken.None).ConfigureAwait(false);

		Assert.IsTrue(outcome.IsCanceled);
		Assert.IsEmpty(fixture.FileSystem.Calls);
	}

	[TestMethod]
	[Timeout(15000, CooperativeCancellation = true)]
	public async Task ReadDocumentFileAsync_WhenTheLifetimeIsCanceledDuringTheRead_ReportsCanceled()
	{
		var fixture = new TestHost();
		var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		fixture.FileSystem.OnRead = async (_, token) =>
		{
			started.SetResult();
			await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);

			return WorkspaceFileReadResult.Missing;
		};

		Task<DocumentReadResult> pending = fixture.Io.ReadDocumentFileAsync(DocumentId, CancellationToken.None);

		await started.Task.WaitAsync(TestContext.CancellationToken).ConfigureAwait(false);
		fixture.Lifetime.Cancel();

		Assert.IsTrue((await pending.WaitAsync(TestContext.CancellationToken).ConfigureAwait(false)).IsCanceled);
	}

	[TestMethod]
	[Timeout(15000, CooperativeCancellation = true)]
	public async Task ReadDocumentFileAsync_WhenTheCallerTokenIsCanceledDuringTheRead_ReportsCanceled()
	{
		var fixture = new TestHost();
		using var cancellation = new CancellationTokenSource();
		var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		fixture.FileSystem.OnRead = async (_, token) =>
		{
			started.SetResult();
			await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);

			return WorkspaceFileReadResult.Missing;
		};

		Task<DocumentReadResult> pending = fixture.Io.ReadDocumentFileAsync(DocumentId, cancellation.Token);

		await started.Task.WaitAsync(TestContext.CancellationToken).ConfigureAwait(false);
		await cancellation.CancelAsync().ConfigureAwait(false);

		Assert.IsTrue((await pending.WaitAsync(TestContext.CancellationToken).ConfigureAwait(false)).IsCanceled);
	}

	[TestMethod]
	public async Task ReadDocumentFileAsync_WhenTheReadThrows_ReportsTheClassifiedFailureWithTheException()
	{
		var fixture = new TestHost();
		var exception = new UnauthorizedAccessException("denied");

		fixture.FileSystem.OnRead = (_, _) => throw exception;

		DocumentReadResult outcome = await fixture.Io.ReadDocumentFileAsync(DocumentId, CancellationToken.None).ConfigureAwait(false);

		Assert.AreEqual(WorkspaceOperationFailureCodes.AccessDenied, outcome.Failure?.Code);
		Assert.AreEqual("denied", outcome.Failure?.Message);
		Assert.AreSame(exception, outcome.Failure?.Exception);
	}

	[TestMethod]
	public async Task ReadDocumentFileAsync_WhenTheReadFailsOtherwise_ReportsTheReadFailureCode()
	{
		var fixture = new TestHost();

		fixture.FileSystem.OnRead = (_, _) => throw new IOException("broken");

		DocumentReadResult outcome = await fixture.Io.ReadDocumentFileAsync(DocumentId, CancellationToken.None).ConfigureAwait(false);

		Assert.AreEqual(WorkspaceOperationFailureCodes.ReadFailed, outcome.Failure?.Code);
	}

	[TestMethod]
	public async Task ReadDocumentFileAsync_WhenTheLifetimeSourceIsDisposed_ReportsAReadFailure()
	{
		var fixture = new TestHost();

		fixture.Lifetime.Dispose();

		DocumentReadResult outcome = await fixture.Io.ReadDocumentFileAsync(DocumentId, CancellationToken.None).ConfigureAwait(false);

		Assert.AreEqual(WorkspaceOperationFailureCodes.ReadFailed, outcome.Failure?.Code);
		Assert.IsInstanceOfType<ObjectDisposedException>(outcome.Failure?.Exception);
		Assert.IsEmpty(fixture.FileSystem.Calls);
	}

	[TestMethod]
	public void DecodeContent_ForADecodedRead_AdoptsTheContentAndTheFileFormat()
	{
		TextFileFormat onDisk = new(TextEncoding.Utf16LittleEndian, HasBom: true, LineTerminatorStyle.CrLf);
		var file = WorkspaceFileReadResult.FromContent("content", onDisk, StampOf("a"));

		WorkspaceOperationFailure? failure = StampedFileWorker.DecodeContent(
			file,
			currentFormat: TestSnapshots.FileFormat,
			noBomEncodings: [TextEncoding.Utf8],
			out string content,
			out TextFileFormat fileFormat
		);

		Assert.IsNull(failure);
		Assert.AreEqual("content", content);
		Assert.AreEqual(onDisk, fileFormat);
	}

	[TestMethod]
	public void DecodeContent_ForAMissingFile_KeepsTheCurrentFileFormat()
	{
		WorkspaceOperationFailure? failure = StampedFileWorker.DecodeContent(
			file: WorkspaceFileReadResult.Missing,
			currentFormat: TestSnapshots.FileFormat,
			noBomEncodings: [TextEncoding.Utf8],
			content: out _,
			out TextFileFormat fileFormat
		);

		Assert.IsNull(failure);
		Assert.AreEqual(TestSnapshots.FileFormat, fileFormat);
	}

	[TestMethod]
	public void DecodeContent_ForBytesThatDoNotDecode_ReportsTheInvalidEncodingFailureWithTheException()
	{
		var file = WorkspaceFileReadResult.FromBytes(StampOf("a"), new byte[] { 0xC3, 0x28 });

		WorkspaceOperationFailure? failure = StampedFileWorker.DecodeContent(
			file,
			currentFormat: TestSnapshots.FileFormat,
			noBomEncodings: [TextEncoding.Utf8],
			out string content,
			out TextFileFormat fileFormat
		);

		Assert.AreEqual(WorkspaceOperationFailureCodes.InvalidEncoding, failure?.Code);
		Assert.IsInstanceOfType<DecoderFallbackException>(failure?.Exception);
		Assert.AreEqual(string.Empty, content);
		Assert.AreEqual(default, fileFormat);
	}

	[TestMethod]
	public void DecodeContent_ForBytesWithoutABom_DecodesWithTheNoBomEncodingOfTheDocument()
	{
		// "ab" as UTF-16 little endian without a byte-order mark; UTF-8 would read the same bytes as four characters with NULs.
		var file = WorkspaceFileReadResult.FromBytes(StampOf("a"), new byte[] { 0x61, 0x00, 0x62, 0x00 });

		WorkspaceOperationFailure? failure = StampedFileWorker.DecodeContent(
			file,
			currentFormat: TestSnapshots.FileFormat,
			noBomEncodings: [TextEncoding.Utf16LittleEndian],
			out string content,
			out TextFileFormat fileFormat
		);

		Assert.IsNull(failure);
		Assert.AreEqual("ab", content);
		Assert.AreEqual(TextEncoding.Utf16LittleEndian, fileFormat.Encoding);
	}

	[TestMethod]
	public async Task WriteReplacementAsync_WritesTheEncodedContentToTheDocumentIdUnderTheExpectedStamp()
	{
		var fixture = new TestHost();

		FileStamp expected = StampOf("expected");
		byte[]? written = null;
		FileStamp? writtenExpectation = null;

		fixture.FileSystem.OnWrite = (_, content, expectedStamp, _) =>
		{
			written = content.ToArray();
			writtenExpectation = expectedStamp;

			return Replaced(StampOf("after"));
		};

		await fixture.WriteAsync("h\u00e9llo", expected).ConfigureAwait(false);

		Assert.AreSequenceEqual(WorkspaceTextCodec.Encode("h\u00e9llo", TestSnapshots.FileFormat), written);

		Assert.AreEqual(expected, writtenExpectation);
		Assert.AreSequenceEqual(["Write:document"], fixture.FileSystem.Calls);
	}

	[TestMethod]
	public async Task WriteReplacementAsync_WhenTheFileSystemReportsTheStamp_ResolvesItWithoutCapturing()
	{
		var fixture = new TestHost();
		FileStamp reported = StampOf("reported");

		fixture.FileSystem.OnWrite = (_, _, _, _) => Replaced(reported);

		(WorkspaceFileReplacementResult replacement, FileStamp? resolved, WorkspaceOperationFailure? failure) = await fixture
			.WriteAsync()
			.ConfigureAwait(false);

		Assert.AreEqual(WorkspaceFileReplacementOutcome.Replaced, replacement.Outcome);
		Assert.AreEqual(reported, resolved);
		Assert.IsNull(failure);
		Assert.AreSequenceEqual(["Write:document"], fixture.FileSystem.Calls);
	}

	[TestMethod]
	public async Task WriteReplacementAsync_WhenNoStampIsReported_CapturesItAfterTheWrite()
	{
		var fixture = new TestHost();
		FileStamp captured = StampOf("captured");

		fixture.FileSystem.OnWrite = (_, _, _, _) => Replaced();
		fixture.FileSystem.OnCaptureStamp = (_, _) => Task.FromResult(captured);

		(_, FileStamp? resolved, WorkspaceOperationFailure? failure) = await fixture.WriteAsync().ConfigureAwait(false);

		Assert.AreEqual(captured, resolved);
		Assert.IsNull(failure);
		Assert.AreSequenceEqual(["Write:document", "Capture:document"], fixture.FileSystem.Calls);
	}

	[TestMethod]
	public async Task WriteReplacementAsync_WhenTheCaptureThrows_ReportsAnUnknownReplacementStateWithTheException()
	{
		var fixture = new TestHost();
		var exception = new IOException("gone");

		fixture.FileSystem.OnWrite = (_, _, _, _) => Replaced();
		fixture.FileSystem.OnCaptureStamp = (_, _) => throw exception;

		(WorkspaceFileReplacementResult replacement, FileStamp? resolved, WorkspaceOperationFailure? failure) = await fixture
			.WriteAsync()
			.ConfigureAwait(false);

		Assert.AreEqual(WorkspaceFileReplacementOutcome.Replaced, replacement.Outcome);
		Assert.IsNull(resolved);
		Assert.AreEqual(WorkspaceOperationFailureCodes.ReplacementStateUnknown, failure?.Code);
		Assert.AreSame(exception, failure?.Exception);
		Assert.IsNotNull(failure);
		Assert.Contains("gone", failure.Message);
	}

	[TestMethod]
	public async Task WriteReplacementAsync_WhenTheCaptureIsCanceled_ReportsAnUnknownReplacementStateNotACancellation()
	{
		var fixture = new TestHost();

		fixture.FileSystem.OnWrite = (_, _, _, _) => Replaced();
		fixture.FileSystem.OnCaptureStamp = (_, _) => throw new OperationCanceledException();

		(_, FileStamp? resolved, WorkspaceOperationFailure? failure) = await fixture.WriteAsync().ConfigureAwait(false);

		Assert.IsNull(resolved);
		Assert.AreEqual(WorkspaceOperationFailureCodes.ReplacementStateUnknown, failure?.Code);
	}

	[TestMethod]
	[DataRow(WorkspaceFileReplacementOutcome.ExternalFileConflict)]
	[DataRow(WorkspaceFileReplacementOutcome.DestinationExists)]
	[DataRow(WorkspaceFileReplacementOutcome.ReplacementStateUnknown)]
	[DataRow(WorkspaceFileReplacementOutcome.Failed)]
	[DataRow(WorkspaceFileReplacementOutcome.Canceled)]
	public async Task WriteReplacementAsync_WhenTheWriteDidNotReplace_ResolvesNoStampAndCapturesNothing(WorkspaceFileReplacementOutcome outcome)
	{
		var fixture = new TestHost();

		fixture.FileSystem.OnWrite = (_, _, _, _) => Task.FromResult(new WorkspaceFileReplacementResult(outcome, StampOf("observed")));

		(WorkspaceFileReplacementResult replacement, FileStamp? resolved, WorkspaceOperationFailure? failure) = await fixture
			.WriteAsync()
			.ConfigureAwait(false);

		Assert.AreEqual(outcome, replacement.Outcome);
		Assert.IsNull(resolved);
		Assert.IsNull(failure);
		Assert.AreSequenceEqual(["Write:document"], fixture.FileSystem.Calls);
	}

	[TestMethod]
	public async Task ResolveMovedStampAsync_ReturnsTheCapturedStamp()
	{
		var fixture = new TestHost();
		FileStamp captured = StampOf("captured");

		fixture.FileSystem.OnCaptureStamp = (_, _) => Task.FromResult(captured);

		FileStamp resolved = await fixture.Io.ResolveMovedStampAsync(DocumentId, StampOf("expected"), CancellationToken.None).ConfigureAwait(false);

		Assert.AreEqual(captured, resolved);
	}

	[TestMethod]
	public async Task ResolveMovedStampAsync_WhenTheCaptureFails_FallsBackToTheExpectedStamp()
	{
		var fixture = new TestHost();
		FileStamp expected = StampOf("expected");

		fixture.FileSystem.OnCaptureStamp = (_, _) => throw new IOException("unreadable");

		Assert.AreEqual(expected, await fixture.Io.ResolveMovedStampAsync(DocumentId, expected, CancellationToken.None).ConfigureAwait(false));

		fixture.FileSystem.OnCaptureStamp = (_, _) => throw new OperationCanceledException();

		Assert.AreEqual(expected, await fixture.Io.ResolveMovedStampAsync(DocumentId, expected, CancellationToken.None).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task FindChangedDescendantStampAsync_WhenEveryStampMatches_ReturnsNullAfterCapturingAllInOrder()
	{
		var fixture = new TestHost();
		FileStamp[] stamps = [StampOf("a"), StampOf("b")];

		fixture.FileSystem.OnCaptureStamp = (path, _) => Task.FromResult(path == "first" ? stamps[0] : stamps[1]);

		(string DocumentId, FileStamp Stamp)? changed = await fixture
			.Io.FindChangedDescendantStampAsync(["first", "second"], stamps, CancellationToken.None)
			.ConfigureAwait(false);

		Assert.IsNull(changed);
		Assert.AreSequenceEqual(["Capture:first", "Capture:second"], fixture.FileSystem.Calls);
	}

	[TestMethod]
	public async Task FindChangedDescendantStampAsync_ReturnsTheFirstChangedDescendantAndStopsThere()
	{
		var fixture = new TestHost();
		FileStamp changedStamp = StampOf("changed");

		fixture.FileSystem.OnCaptureStamp = (path, _) => Task.FromResult(path == "second" ? changedStamp : StampOf("same"));

		(string DocumentId, FileStamp Stamp)? changed = await fixture
			.Io.FindChangedDescendantStampAsync(
				["first", "second", "third"],
				[StampOf("same"), StampOf("same"), StampOf("other")],
				CancellationToken.None
			)
			.ConfigureAwait(false);

		Assert.AreEqual("second", changed?.DocumentId);
		Assert.AreEqual(changedStamp, changed?.Stamp);
		Assert.AreSequenceEqual(["Capture:first", "Capture:second"], fixture.FileSystem.Calls);
	}
}

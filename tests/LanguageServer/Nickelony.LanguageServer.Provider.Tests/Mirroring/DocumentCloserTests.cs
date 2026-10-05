using Microsoft.Extensions.Logging;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers how the server copy of a document is closed: after the last open lease, behind a request lease, and after an idle trim.</summary>
[TestClass]
public sealed class DocumentCloserTests
{
	private const string DidClose = "textDocument/didClose";

	private static readonly TimeSpan s_waitTimeout = TimeSpan.FromSeconds(5);

	private static int CountSent(FakeLanguageServerClient client, string method) => client.GetSentMethodNames().Count(name => name == method);

	private static async Task<string> OpenAsync(SynchronizerTestHost fixture, string fileName = "a.lua")
	{
		string filePath = TestPaths.Script(fileName);

		fixture.Synchronizer.Open(filePath, "local a = 1");

		Assert.IsTrue(await fixture.Client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));
		Assert.IsTrue(await TestPolling.ForConditionAsync(() => fixture.Signals.Contains($"synchronized:{fileName}:v1"), s_waitTimeout));

		return filePath;
	}

	private static async Task<DocumentRequestLease> AcquireRequestLeaseAsync(SynchronizerTestHost fixture, string filePath)
	{
		var requestLease = new DocumentRequestLease();

		Assert.IsTrue(
			await fixture.Synchronizer.SynchronizeForRequestAsync(
				filePath,
				content: "local a = 1",
				requestLease,
				cancellationToken: CancellationToken.None
			)
		);

		Assert.IsTrue(requestLease.IsAcquired);

		return requestLease;
	}

	[TestMethod]
	public async Task CloseDocumentAsync_ForTheLastOpenLease_SendsTheCloseAndSignalsTheDocumentInvalidated()
	{
		using var fixture = new SynchronizerTestHost();

		string filePath = await OpenAsync(fixture);

		await fixture.Closer.CloseDocumentAsync(filePath, CancellationToken.None);

		Assert.AreEqual(1, CountSent(fixture.Client, DidClose));
		Assert.Contains("invalidated:a.lua", fixture.Signals);
		Assert.IsFalse(fixture.Documents.IsTrackedNormalized(filePath));
	}

	[TestMethod]
	public async Task CloseDocumentAsync_ForTheLastOpenLease_ForgetsAPendingCloseOfTheSamePath()
	{
		using var fixture = new SynchronizerTestHost();

		string filePath = await OpenAsync(fixture);

		// A marker left by an earlier deferral must not complete a later close a second time.
		fixture.PendingCloses.MarkPending(filePath);

		await fixture.Closer.CloseDocumentAsync(filePath, CancellationToken.None);

		Assert.IsFalse(fixture.PendingCloses.IsPending(filePath));
		Assert.AreEqual(1, CountSent(fixture.Client, DidClose));
	}

	[TestMethod]
	public async Task CloseDocumentAsync_WhileARequestLeaseIsActive_DefersTheCloseWithoutSendingIt()
	{
		using var fixture = new SynchronizerTestHost();

		string filePath = await OpenAsync(fixture);

		await AcquireRequestLeaseAsync(fixture, filePath);
		await fixture.Closer.CloseDocumentAsync(filePath, CancellationToken.None);

		Assert.IsTrue(fixture.PendingCloses.IsPending(filePath));
		Assert.AreEqual(0, CountSent(fixture.Client, DidClose));
		Assert.DoesNotContain("invalidated:a.lua", fixture.Signals);
		Assert.IsTrue(fixture.Documents.IsTrackedNormalized(filePath));
	}

	[TestMethod]
	public async Task CloseDocumentAsync_WhileTheClientIsNotReady_ClosesTheRecordWithoutSendingTheClose()
	{
		using var fixture = new SynchronizerTestHost();

		string filePath = await OpenAsync(fixture);

		fixture.Client.IsReady = false;

		await fixture.Closer.CloseDocumentAsync(filePath, CancellationToken.None);

		// A close never starts the connection, so the record closes locally and the server is told nothing.
		Assert.AreEqual(0, CountSent(fixture.Client, DidClose));
		Assert.Contains("invalidated:a.lua", fixture.Signals);
		Assert.IsFalse(fixture.Documents.IsTrackedNormalized(filePath));
		Assert.AreEqual(1, fixture.Client.StartCallCount);
	}

	[TestMethod]
	public async Task CloseDocumentAsync_WhenTheCloseSendFailsOnTheConnection_LogsAtDebugLevelAndDoesNotThrow()
	{
		using var logger = new RecordingLogger();
		using var fixture = new SynchronizerTestHost(logger: logger);

		string filePath = await OpenAsync(fixture);

		fixture.Client.SendNotificationHandler = static (method, _, _) =>
			method == DidClose ? Task.FromException(new IOException("The connection closed.")) : Task.CompletedTask;

		await fixture.Closer.CloseDocumentAsync(filePath, CancellationToken.None);

		Assert.IsFalse(logger.HasEntryAtLeast(LogLevel.Warning));

		Assert.Contains(
			static log => log.StartsWith("Debug|", StringComparison.Ordinal) && log.Contains("document close", StringComparison.Ordinal),
			logger.Logs
		);
	}

	[TestMethod]
	public async Task CloseDocumentAsync_WhenTheCloseSendFindsTheClientDisposedUnderALiveProvider_LogsAtDebugLevelAndDoesNotThrow()
	{
		using var logger = new RecordingLogger();
		using var fixture = new SynchronizerTestHost(logger: logger);

		string filePath = await OpenAsync(fixture);

		fixture.Client.SendNotificationHandler = static (method, _, _) =>
			method == DidClose ? Task.FromException(new ObjectDisposedException("client")) : Task.CompletedTask;

		await fixture.Closer.CloseDocumentAsync(filePath, CancellationToken.None);

		Assert.IsFalse(logger.HasEntryAtLeast(LogLevel.Warning));

		Assert.Contains(
			static log => log.StartsWith("Debug|", StringComparison.Ordinal) && log.Contains("document close", StringComparison.Ordinal),
			logger.Logs
		);
	}

	[TestMethod]
	public async Task CloseDocumentAsync_WhenTheCloseSendRacesTheDisposal_LogsNothingForTheClose()
	{
		using var logger = new RecordingLogger();
		using var fixture = new SynchronizerTestHost(logger: logger);

		string filePath = await OpenAsync(fixture);

		fixture.Client.SendNotificationHandler = (method, _, _) =>
		{
			if (method != DidClose)
				return Task.CompletedTask;

			fixture.Disposal.Signal();

			return Task.FromException(new ObjectDisposedException("client"));
		};

		await fixture.Closer.CloseDocumentAsync(filePath, CancellationToken.None);

		Assert.DoesNotContain(static log => log.Contains("document close", StringComparison.Ordinal), logger.Logs);
	}

	[TestMethod]
	public async Task CloseDocumentAsync_WhenTheCloseSendFailsUnexpectedly_LogsAWarningAndDoesNotThrow()
	{
		using var logger = new RecordingLogger();
		using var fixture = new SynchronizerTestHost(logger: logger);

		string filePath = await OpenAsync(fixture);

		fixture.Client.SendNotificationHandler = static (method, _, _) =>
			method == DidClose ? Task.FromException(new InvalidOperationException("unexpected")) : Task.CompletedTask;

		await fixture.Closer.CloseDocumentAsync(filePath, CancellationToken.None);

		Assert.Contains(
			static log => log.StartsWith("Warn|", StringComparison.Ordinal) && log.Contains("document close", StringComparison.Ordinal),
			logger.Logs
		);
	}

	[TestMethod]
	public async Task CloseTrimmedDocumentAsync_SignalsTheDocumentInvalidated()
	{
		using var fixture = new SynchronizerTestHost();

		string filePath = await OpenAsync(fixture);

		DocumentSnapshot document =
			fixture.Documents.GetDocumentSnapshot(filePath) ?? throw new InvalidOperationException("The opened document should be tracked.");

		// The tracker evicts the record before it asks for the server close.
		fixture.Documents.Close(filePath);

		await fixture.Closer.CloseTrimmedDocumentAsync(document);

		Assert.Contains("invalidated:a.lua", fixture.Signals);
		Assert.AreEqual(1, CountSent(fixture.Client, DidClose));
	}

	[TestMethod]
	public async Task CloseTrimmedDocumentAsync_WhenTheDocumentIsTrackedAgain_SendsNothingAndSignalsNothing()
	{
		using var fixture = new SynchronizerTestHost();

		string filePath = await OpenAsync(fixture);

		DocumentSnapshot document =
			fixture.Documents.GetDocumentSnapshot(filePath) ?? throw new InvalidOperationException("The opened document should be tracked.");

		// The record was recreated after the trim, so its own lifecycle owns the server copy.
		await fixture.Closer.CloseTrimmedDocumentAsync(document);

		Assert.AreEqual(0, CountSent(fixture.Client, DidClose));
		Assert.DoesNotContain("invalidated:a.lua", fixture.Signals);
	}

	[TestMethod]
	public async Task CloseTrimmedDocumentAsync_WhileTheClientIsNotReady_SignalsButSendsNothing()
	{
		using var fixture = new SynchronizerTestHost();

		string filePath = await OpenAsync(fixture);

		DocumentSnapshot document =
			fixture.Documents.GetDocumentSnapshot(filePath) ?? throw new InvalidOperationException("The opened document should be tracked.");

		fixture.Documents.Close(filePath);
		fixture.Client.IsReady = false;

		await fixture.Closer.CloseTrimmedDocumentAsync(document);

		Assert.AreEqual(0, CountSent(fixture.Client, DidClose));
		Assert.Contains("invalidated:a.lua", fixture.Signals);
	}

	[TestMethod]
	public async Task CloseTrimmedDocumentAsync_AfterDisposalStarted_DoesNothing()
	{
		using var fixture = new SynchronizerTestHost();

		string filePath = await OpenAsync(fixture);

		DocumentSnapshot document =
			fixture.Documents.GetDocumentSnapshot(filePath) ?? throw new InvalidOperationException("The opened document should be tracked.");

		fixture.Documents.Close(filePath);
		fixture.Disposal.Signal();

		await fixture.Closer.CloseTrimmedDocumentAsync(document);

		Assert.AreEqual(0, CountSent(fixture.Client, DidClose));
		Assert.DoesNotContain("invalidated:a.lua", fixture.Signals);
	}
}

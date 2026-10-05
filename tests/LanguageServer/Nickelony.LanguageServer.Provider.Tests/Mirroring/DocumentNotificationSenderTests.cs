using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers the notification that each synchronization directive stands for, and the change a session without a mode skips.</summary>
[TestClass]
public sealed class DocumentNotificationSenderTests
{
	private static DocumentNotificationSender CreateSender(
		FakeLanguageServerClient? client,
		RecordingLogger? logger = null,
		Func<string>? languageId = null
	) => new(TestProviderContext.Create(client, logger), languageId ?? (static () => "test"));

	private static DocumentSnapshot CreateDocument(string content = "local a = 1", int version = 1)
	{
		string filePath = TestPaths.Script("a.lua");
		return new DocumentSnapshot(filePath, uri: DocumentUriConversion.CreateFileUri(filePath), content, version);
	}

	private static Task<bool> SendChangeAsync(
		DocumentNotificationSender sender,
		DocumentSnapshot document,
		DocumentChangeRange? changeRange = null
	) => sender.SendAsync(new DocumentSynchronizationDirective(DocumentSynchronizationKind.Change, document, changeRange), CancellationToken.None);

	private static int CountEntries(RecordingLogger logger, LogLevel level) => logger.Entries.Count(entry => entry.Level == level);

	[TestMethod]
	public async Task SendAsync_ForAClose_SendsDidCloseForTheDocumentUri()
	{
		using var client = new FakeLanguageServerClient { ConnectionGeneration = 1 };

		DocumentNotificationSender sender = CreateSender(client);
		DocumentSnapshot document = CreateDocument();

		bool delivered = await sender.SendAsync(
			new DocumentSynchronizationDirective(DocumentSynchronizationKind.Close, document),
			CancellationToken.None
		);

		Assert.IsTrue(delivered);
		Assert.AreSequenceEqual(["textDocument/didClose"], client.GetSentMethodNames());

		JsonElement textDocument = client.GetLastNotificationParameters("textDocument/didClose").GetProperty("textDocument");

		Assert.AreEqual(document.Uri, textDocument.GetProperty("uri").GetString());
	}

	[TestMethod]
	public async Task SendAsync_ForAnOpen_SendsTheCurrentLanguageIdVersionAndContent()
	{
		using var client = new FakeLanguageServerClient { ConnectionGeneration = 1 };

		string languageId = "before";
		DocumentNotificationSender sender = CreateSender(client, languageId: () => languageId);
		DocumentSnapshot document = CreateDocument("local a = 1", version: 4);

		// The language identifier is read when the notification is sent, not when the sender is built.
		languageId = "after";

		bool delivered = await sender.SendAsync(
			new DocumentSynchronizationDirective(DocumentSynchronizationKind.Open, document),
			CancellationToken.None
		);

		Assert.IsTrue(delivered);

		JsonElement textDocument = client.GetLastNotificationParameters("textDocument/didOpen").GetProperty("textDocument");

		Assert.AreEqual(document.Uri, textDocument.GetProperty("uri").GetString());
		Assert.AreEqual("after", textDocument.GetProperty("languageId").GetString());
		Assert.AreEqual(4, textDocument.GetProperty("version").GetInt32());
		Assert.AreEqual("local a = 1", textDocument.GetProperty("text").GetString());
	}

	[TestMethod]
	public async Task SendAsync_ForAChangeUnderIncrementalSynchronization_SendsTheChangedRange()
	{
		using var client = new FakeLanguageServerClient { ConnectionGeneration = 1, TextDocumentSyncKind = TextDocumentSyncKind.Incremental };

		DocumentNotificationSender sender = CreateSender(client);
		DocumentSnapshot document = CreateDocument("local a = 2", version: 2);

		var changeRange = new DocumentChangeRange(new TextPositionRange(new TextPosition(0, 10), new TextPosition(0, 11)), "2");

		Assert.IsTrue(await SendChangeAsync(sender, document, changeRange));

		JsonElement parameters = client.GetLastNotificationParameters("textDocument/didChange");
		JsonElement change = parameters.GetProperty("contentChanges")[0];

		Assert.AreEqual(2, parameters.GetProperty("textDocument").GetProperty("version").GetInt32());
		Assert.AreEqual("2", change.GetProperty("text").GetString());
		Assert.AreEqual(10, change.GetProperty("range").GetProperty("start").GetProperty("character").GetInt32());
		Assert.AreEqual(11, change.GetProperty("range").GetProperty("end").GetProperty("character").GetInt32());
	}

	[TestMethod]
	public async Task SendAsync_ForAChangeUnderIncrementalSynchronizationWithoutARange_SendsTheWholeContent()
	{
		using var client = new FakeLanguageServerClient { ConnectionGeneration = 1, TextDocumentSyncKind = TextDocumentSyncKind.Incremental };

		DocumentNotificationSender sender = CreateSender(client);

		Assert.IsTrue(await SendChangeAsync(sender, CreateDocument("local a = 2", version: 2)));

		JsonElement change = client.GetLastNotificationParameters("textDocument/didChange").GetProperty("contentChanges")[0];

		Assert.AreEqual("local a = 2", change.GetProperty("text").GetString());
		Assert.IsFalse(change.TryGetProperty("range", out _));
	}

	[TestMethod]
	public async Task SendAsync_ForAChangeUnderFullSynchronization_SendsTheWholeContentEvenWithARange()
	{
		using var client = new FakeLanguageServerClient { ConnectionGeneration = 1, TextDocumentSyncKind = TextDocumentSyncKind.Full };

		DocumentNotificationSender sender = CreateSender(client);

		var changeRange = new DocumentChangeRange(new TextPositionRange(new TextPosition(0, 10), new TextPosition(0, 11)), "2");

		Assert.IsTrue(await SendChangeAsync(sender, CreateDocument("local a = 2", version: 2), changeRange));

		JsonElement change = client.GetLastNotificationParameters("textDocument/didChange").GetProperty("contentChanges")[0];

		Assert.AreEqual("local a = 2", change.GetProperty("text").GetString());
		Assert.IsFalse(change.TryGetProperty("range", out _));
	}

	[TestMethod]
	public async Task SendAsync_ForAChangeWithoutASynchronizationMode_SkipsItAndStillSendsTheOpenAndTheClose()
	{
		using var client = new FakeLanguageServerClient { ConnectionGeneration = 1, TextDocumentSyncKind = TextDocumentSyncKind.None };

		DocumentNotificationSender sender = CreateSender(client);
		DocumentSnapshot document = CreateDocument();

		Assert.IsFalse(await SendChangeAsync(sender, document));
		Assert.IsEmpty(client.GetSentMethodNames());

		Assert.IsTrue(
			await sender.SendAsync(new DocumentSynchronizationDirective(DocumentSynchronizationKind.Open, document), CancellationToken.None)
		);

		Assert.IsTrue(
			await sender.SendAsync(new DocumentSynchronizationDirective(DocumentSynchronizationKind.Close, document), CancellationToken.None)
		);

		Assert.AreSequenceEqual(["textDocument/didOpen", "textDocument/didClose"], client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task SendAsync_ForSkippedChanges_WarnsOncePerConnectionGeneration()
	{
		using var logger = new RecordingLogger();
		using var client = new FakeLanguageServerClient { ConnectionGeneration = 1, TextDocumentSyncKind = TextDocumentSyncKind.None };

		DocumentNotificationSender sender = CreateSender(client, logger);
		DocumentSnapshot document = CreateDocument();

		await SendChangeAsync(sender, document);
		await SendChangeAsync(sender, document);
		await SendChangeAsync(sender, document);

		// The first skip of the generation warns, and the later ones drop to debug so a keystroke does not log a warning.
		Assert.AreEqual(1, CountEntries(logger, LogLevel.Warning));
		Assert.AreEqual(2, CountEntries(logger, LogLevel.Debug));

		client.ConnectionGeneration = 2;

		await SendChangeAsync(sender, document);
		await SendChangeAsync(sender, document);

		Assert.AreEqual(2, CountEntries(logger, LogLevel.Warning));
		Assert.AreEqual(3, CountEntries(logger, LogLevel.Debug));
		Assert.Contains(static message => message.Contains("negotiated no document synchronization mode", StringComparison.Ordinal), logger.Messages);
	}

	[TestMethod]
	public void SendAsync_ForSkippedChangesThatRaceInOneGeneration_WarnsOncePerGeneration()
	{
		const int generations = 200;
		const int senders = 8;

		using var logger = new RecordingLogger();
		using var client = new FakeLanguageServerClient { ConnectionGeneration = 0, TextDocumentSyncKind = TextDocumentSyncKind.None };

		DocumentNotificationSender sender = CreateSender(client, logger);
		DocumentSnapshot document = CreateDocument();

		// Each round moves the client to a new generation, then releases every sender at once, so all of them skip a change before any warned.
		using var barrier = new Barrier(senders, _ => client.ConnectionGeneration++);

		Thread[] threads =
		[
			.. Enumerable
				.Range(0, senders)
				.Select(_ => new Thread(() =>
				{
					for (int round = 0; round < generations; round++)
					{
						barrier.SignalAndWait();
						SendChangeAsync(sender, document).GetAwaiter().GetResult();
					}
				})),
		];

		foreach (Thread thread in threads)
			thread.Start();

		foreach (Thread thread in threads)
			thread.Join();

		Assert.AreEqual(generations, CountEntries(logger, LogLevel.Warning));
		Assert.AreEqual(generations * (senders - 1), CountEntries(logger, LogLevel.Debug));
	}

	[TestMethod]
	public async Task SendAsync_WithoutAClient_DeliversNothing()
	{
		DocumentNotificationSender sender = CreateSender(client: null);
		DocumentSnapshot document = CreateDocument();

		Assert.IsFalse(
			await sender.SendAsync(new DocumentSynchronizationDirective(DocumentSynchronizationKind.Open, document), CancellationToken.None)
		);

		Assert.IsFalse(
			await sender.SendAsync(new DocumentSynchronizationDirective(DocumentSynchronizationKind.Close, document), CancellationToken.None)
		);

		Assert.IsFalse(await SendChangeAsync(sender, document));
	}

	[TestMethod]
	public async Task SendAsync_WhenTheConnectionFails_PropagatesTheFailureToTheCaller()
	{
		using var client = new FakeLanguageServerClient
		{
			ConnectionGeneration = 1,
			SendNotificationHandler = static (_, _, _) => Task.FromException(new IOException("The connection closed.")),
		};

		DocumentNotificationSender sender = CreateSender(client);

		await Assert.ThrowsExactlyAsync<IOException>(() => SendChangeAsync(sender, CreateDocument()));
	}
}

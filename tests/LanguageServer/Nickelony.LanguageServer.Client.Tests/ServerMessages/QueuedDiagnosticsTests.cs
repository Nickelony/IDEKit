using System.Collections.Concurrent;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class QueuedDiagnosticsTests
{
	private static readonly DiagnosticsDocumentKey s_firstDocument = DiagnosticsDocumentKey.FromUri("untitled:First");
	private static readonly DiagnosticsDocumentKey s_secondDocument = DiagnosticsDocumentKey.FromUri("untitled:Second");

	[TestMethod]
	public void DeliverPending_DeliversEachPayloadOnceAndEmptiesThePendingSet()
	{
		var pending = new ConcurrentDictionary<DiagnosticsDocumentKey, QueuedDiagnostics>(DiagnosticsDocumentKey.Comparer);
		var delivered = new List<string>();

		pending[s_firstDocument] = Queued(1, "first");
		pending[s_secondDocument] = Queued(1, "second");

		bool deliveredAny = QueuedDiagnostics.DeliverPending(
			pending,
			static _ => true,
			(key, queued) => delivered.Add(key.Value + ":" + ServerMessageTestHelpers.FirstMessage(queued.Parameters))
		);

		Assert.IsTrue(deliveredAny);
		Assert.IsEmpty(pending);

		Assert.AreSequenceEqual(
			["untitled:First:first", "untitled:Second:second"],
			[.. delivered],
			Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder
		);
	}

	[TestMethod]
	public void DeliverPending_DropsThePayloadOfAGenerationThatNoLongerAcceptsServerMessages()
	{
		var pending = new ConcurrentDictionary<DiagnosticsDocumentKey, QueuedDiagnostics>(DiagnosticsDocumentKey.Comparer);
		var delivered = new List<string>();

		pending[s_firstDocument] = Queued(1, "stale");
		pending[s_secondDocument] = Queued(2, "current");

		bool deliveredAny = QueuedDiagnostics.DeliverPending(pending, static generation => generation == 2, (key, _) => delivered.Add(key.Value));

		Assert.IsTrue(deliveredAny);
		Assert.IsEmpty(pending);
		Assert.AreSequenceEqual(["untitled:Second"], [.. delivered]);
	}

	[TestMethod]
	public void DeliverPending_WhenEveryPayloadIsDropped_ReturnsFalse()
	{
		var pending = new ConcurrentDictionary<DiagnosticsDocumentKey, QueuedDiagnostics>(DiagnosticsDocumentKey.Comparer)
		{
			[s_firstDocument] = Queued(1, "stale"),
		};

		bool deliveredAny = QueuedDiagnostics.DeliverPending(
			pending,
			static _ => false,
			static (_, _) => Assert.Fail("A dropped payload must not be delivered.")
		);

		Assert.IsFalse(deliveredAny);
		Assert.IsEmpty(pending);
	}

	[TestMethod]
	public void DeliverPending_WithoutPendingPayloads_ReturnsFalse()
	{
		var pending = new ConcurrentDictionary<DiagnosticsDocumentKey, QueuedDiagnostics>(DiagnosticsDocumentKey.Comparer);
		Assert.IsFalse(QueuedDiagnostics.DeliverPending(pending, static _ => true, static (_, _) => Assert.Fail("Nothing is pending.")));
	}

	[TestMethod]
	public void DeliverPending_WhilePayloadsAreQueuedConcurrently_DeliversWithoutThrowing()
	{
		// The window between a copy that sizes its array from the dictionary's count and the copy itself lies inside the framework, so the test
		// keeps a writer queueing without pause while the passes run. A copy sized from the count throws in a large share of the passes (measured:
		// a third or more with 64 keys), so the number of passes leaves an unguarded copy no chance to survive.
		const int DocumentCount = 64;
		const int Passes = 2000;

		var pending = new ConcurrentDictionary<DiagnosticsDocumentKey, QueuedDiagnostics>(DiagnosticsDocumentKey.Comparer);

		DiagnosticsDocumentKey[] documents =
		[
			.. Enumerable.Range(0, DocumentCount).Select(index => DiagnosticsDocumentKey.FromUri("untitled:Document" + index)),
		];

		QueuedDiagnostics queued = Queued(1, "warning");

		using var stopWriter = new CancellationTokenSource();

		Task writer = Task.Factory.StartNew(
			() =>
			{
				for (int next = 0; !stopWriter.IsCancellationRequested; next++)
					pending[documents[next % DocumentCount]] = queued;
			},

			TaskCreationOptions.LongRunning
		);

		int delivered = 0;

		try
		{
			for (int pass = 0; pass < Passes; pass++)
				QueuedDiagnostics.DeliverPending(pending, static _ => true, (_, _) => delivered++);
		}
		finally
		{
			stopWriter.Cancel();
			writer.Wait();
		}

		QueuedDiagnostics.DeliverPending(pending, static _ => true, (_, _) => delivered++);

		Assert.IsEmpty(pending);
		Assert.IsGreaterThan(0, delivered);
	}

	private static QueuedDiagnostics Queued(long connectionGeneration, string message) =>
		new(connectionGeneration, ServerMessageTestHelpers.CreateDiagnostics("untitled:Ignored", message));
}

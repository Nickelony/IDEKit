using System.Collections.Concurrent;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Client;

/// <summary>A diagnostics payload that waits for delivery, together with the connection generation that published it.</summary>
/// <param name="ConnectionGeneration">The generation that must still accept server messages when the payload is delivered.</param>
/// <param name="Parameters">The payload, detached from the caller's arrays.</param>
internal readonly record struct QueuedDiagnostics(long ConnectionGeneration, PublishDiagnosticsParams Parameters)
{
	/// <summary>Delivers a snapshot of the pending payloads, dropping each one whose generation stopped accepting server messages.</summary>
	/// <typeparam name="TKey">The key that coalesces the payloads of the pending set.</typeparam>
	/// <param name="pending">The pending payloads; each payload that the snapshot reaches leaves the set, delivered or dropped.</param>
	/// <param name="canAcceptServerMessagesForGeneration">Reports whether a connection generation still accepts server messages.</param>
	/// <param name="deliver">Receives each deliverable payload with its key.</param>
	/// <returns><see langword="true"/> when at least one payload was delivered; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// A generation can close while its payload waits, for example when its connection is marked unhealthy, so each stage checks
	/// it again before it delivers.
	/// </remarks>
	internal static bool DeliverPending<TKey>(
		ConcurrentDictionary<TKey, QueuedDiagnostics> pending,
		Func<long, bool> canAcceptServerMessagesForGeneration,
		Action<TKey, QueuedDiagnostics> deliver
	)
		where TKey : notnull
	{
		bool delivered = false;

		// The dictionary takes the snapshot under all of its locks. A collection-expression spread or a LINQ copy of the dictionary sizes its array from
		// the count and copies afterwards, and a payload queued in between makes the copy throw, which would end the loop that runs the pass.
#pragma warning disable IDE0305
		KeyValuePair<TKey, QueuedDiagnostics>[] snapshot = pending.ToArray();
#pragma warning restore IDE0305

		for (int i = 0; i < snapshot.Length; i++)
		{
			if (
				!pending.TryRemove(snapshot[i].Key, out QueuedDiagnostics queuedDiagnostics)
				|| !canAcceptServerMessagesForGeneration(queuedDiagnostics.ConnectionGeneration)
			)
			{
				continue;
			}

			deliver(snapshot[i].Key, queuedDiagnostics);
			delivered = true;
		}

		return delivered;
	}
}

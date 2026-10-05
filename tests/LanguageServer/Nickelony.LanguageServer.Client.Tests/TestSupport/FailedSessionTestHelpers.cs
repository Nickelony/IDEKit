namespace Nickelony.LanguageServer.Client.Tests;

/// <summary>Holds the failed-session queue of a client busy, so a test sees what waits for it.</summary>
internal static class FailedSessionTestHelpers
{
	/// <summary>Queues the disposal of a session whose connection blocks until the release task completes.</summary>
	/// <param name="dispatcher">The queue to hold busy.</param>
	/// <param name="release">The task whose completion lets the disposal finish.</param>
	internal static void QueueBlockedDisposal(FailedSessionDispatcher dispatcher, Task release)
	{
		var session = new ConnectionSession(connectionGeneration: 1, process: null, readStream: Stream.Null, writeStream: Stream.Null)
		{
			Connection = new GatedLanguageServerConnection(release, new TaskCompletionSource<bool>()),
		};

		dispatcher.Dispatch(session, "test");
	}
}

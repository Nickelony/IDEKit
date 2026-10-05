namespace Nickelony.LanguageServer.Provider;

/// <summary>Describes the outcome of an explicit close call for a tracked document.</summary>
/// <remarks>
/// Only <see cref="Closed"/> can require a <see cref="DocumentSynchronizationKind.Close"/> notification to the server,
/// and only when <see cref="DocumentTracker.Close"/> also returns a closing snapshot.
/// </remarks>
internal enum DocumentCloseOutcome
{
	/// <summary>The document was not tracked; the close was a no-op.</summary>
	Untracked = 0,

	/// <summary>
	/// The close released the last lease, or cleaned up an idle record that held none; the tracked record was removed and the server copy
	/// can be closed.
	/// </summary>
	Closed = 1,

	/// <summary>The close released one host-open lease, but other host-open leases remain.</summary>
	StillOpen = 2,

	/// <summary>
	/// The close released the last host-open lease, but temporary request leases remain, so the tracked record is kept until the caller retries
	/// the close after the leases drain.
	/// </summary>
	BusyWithRequests = 3,
}

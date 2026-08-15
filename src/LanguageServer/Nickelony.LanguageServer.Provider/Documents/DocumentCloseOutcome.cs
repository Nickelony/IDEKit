namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Describes the outcome of an explicit close call for a tracked document.
/// </summary>
/// <remarks>
/// This is the explicit-close result axis: it reports what a close call did to the tracked record so the caller can
/// decide whether to emit a <see cref="DocumentSynchronizationKind.Close"/>. It is independent of
/// <see cref="DocumentReferenceAcquisition"/>, which selects the references a
/// <see cref="TrackedDocumentStore.Synchronize"/> call acquires.
/// </remarks>
public enum DocumentCloseOutcome
{
	/// <summary>
	/// The document was not tracked; the close was a no-op.
	/// </summary>
	Untracked = 0,

	/// <summary>
	/// The close released the last reference, or cleaned up an idle record that held none; the tracked record was
	/// removed and the server copy can be closed.
	/// </summary>
	Closed = 1,

	/// <summary>
	/// The close released one host-open reference, but other host-open references remain.
	/// </summary>
	StillOpen = 2,

	/// <summary>
	/// The close released the last host-open reference, but temporary request references remain, so the tracked
	/// record is kept until the caller retries the close after the references drain.
	/// </summary>
	BusyWithRequests = 3
}

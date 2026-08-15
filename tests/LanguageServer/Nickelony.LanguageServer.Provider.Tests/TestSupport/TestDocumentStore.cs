namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>
/// Minimal tracked document store for framework tests.
/// </summary>
internal sealed class TestDocumentStore : TrackedDocumentStore
{
	/// <inheritdoc/>
	protected override TrackedDocumentState CreateTrackedDocumentState(TrackedDocumentInitialState initialState)
		=> new TestDocumentState(initialState);

	/// <inheritdoc/>
	protected override long GetLastAccessStamp(TrackedDocumentState state)
		=> state.LastAccessStamp;

	/// <inheritdoc/>
	protected override void TouchTrackedDocumentState(TrackedDocumentState state, long lastAccessStamp)
		=> ((TestDocumentState)state).Touch(lastAccessStamp);

	/// <inheritdoc/>
	protected override void ReopenTrackedDocumentState(TrackedDocumentState state, string content)
		=> ((TestDocumentState)state).Reopen(content);

	/// <inheritdoc/>
	protected override string ReplaceTrackedDocumentContent(TrackedDocumentState state, string content)
		=> ((TestDocumentState)state).UpdateContent(content);

	/// <inheritdoc/>
	protected override void RenameTrackedDocumentState(TrackedDocumentState state, string filePath, string uri)
		=> ((TestDocumentState)state).RenameTo(filePath, uri);

	/// <inheritdoc/>
	protected override void MarkTrackedDocumentClosed(TrackedDocumentState state)
		=> ((TestDocumentState)state).MarkClosed();

	/// <summary>
	/// Marks a tracked document as needing a fresh server-side open, mirroring the language-side invalidation the
	/// provider framework invokes when a synchronization delivery fails.
	/// </summary>
	/// <param name="filePath">The normalized document path to invalidate.</param>
	internal void Invalidate(string filePath)
		=> WithTrackedDocument(filePath, static state => ((TestDocumentState)state).MarkClosed());
}

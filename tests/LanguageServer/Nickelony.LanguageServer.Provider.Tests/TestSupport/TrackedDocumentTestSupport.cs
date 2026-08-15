namespace Nickelony.LanguageServer.Provider.Tests;

internal sealed class TestTrackedDocumentStore : TrackedDocumentStore
{
	protected override TrackedDocumentState CreateTrackedDocumentState(TrackedDocumentInitialState initialState)
		=> new TestTrackedDocumentState(initialState);

	protected override long GetLastAccessStamp(TrackedDocumentState state)
		=> state.LastAccessStamp;

	protected override void TouchTrackedDocumentState(TrackedDocumentState state, long lastAccessStamp)
		=> ((TestTrackedDocumentState)state).Touch(lastAccessStamp);

	protected override void ReopenTrackedDocumentState(TrackedDocumentState state, string content)
		=> ((TestTrackedDocumentState)state).Reopen(content);

	protected override string ReplaceTrackedDocumentContent(TrackedDocumentState state, string content)
		=> ((TestTrackedDocumentState)state).Update(content);

	protected override void RenameTrackedDocumentState(TrackedDocumentState state, string filePath, string uri)
		=> ((TestTrackedDocumentState)state).Rename(filePath, uri);

	protected override void MarkTrackedDocumentClosed(TrackedDocumentState state)
		=> ((TestTrackedDocumentState)state).Close();
}

internal sealed class TestTrackedDocumentState : TrackedDocumentState
{
	public TestTrackedDocumentState(TrackedDocumentInitialState initialState)
		: base(initialState)
	{ }

	public void Touch(long lastAccessStamp)
		=> SetLastAccessStamp(lastAccessStamp);

	public void Reopen(string content)
		=> ReopenDocument(content);

	public string Update(string content)
		=> ReplaceContent(content);

	public void Rename(string filePath, string uri)
		=> RenameDocument(filePath, uri);

	public void Close()
		=> MarkDocumentClosed();
}

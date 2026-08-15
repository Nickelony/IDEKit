namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>
/// Minimal tracked document state for framework tests: it carries no language-specific data and only exposes
/// the state mutations the test store must apply across the class boundary.
/// </summary>
internal sealed class TestDocumentState : TrackedDocumentState
{
	/// <summary>
	/// Initializes a new instance of the <see cref="TestDocumentState"/> class.
	/// </summary>
	/// <param name="initialState">The initial tracked-document state.</param>
	public TestDocumentState(TrackedDocumentInitialState initialState)
		: base(initialState)
	{ }

	/// <summary>Updates the idle-document access stamp used for LRU trimming.</summary>
	public void Touch(long lastAccessStamp)
		=> SetLastAccessStamp(lastAccessStamp);

	/// <summary>Marks the document as reopened with fresh synchronized content.</summary>
	public void Reopen(string content)
		=> ReopenDocument(content);

	/// <summary>Replaces the tracked content and advances the version.</summary>
	/// <param name="content">The replacement content.</param>
	/// <returns>The previous content.</returns>
	public string UpdateContent(string content)
		=> ReplaceContent(content);

	/// <summary>Replaces the tracked file path and URI after a rename.</summary>
	/// <param name="filePath">The normalized replacement file path.</param>
	/// <param name="uri">The replacement file URI.</param>
	public void RenameTo(string filePath, string uri)
		=> RenameDocument(filePath, uri);

	/// <summary>Marks the tracked document as closed locally while preserving cached state.</summary>
	public void MarkClosed()
		=> MarkDocumentClosed();
}

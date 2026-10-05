namespace Nickelony.IDEKit.LanguageFeatures.Synchronization;

/// <summary>Receives the documents an editor host mirrors to a language service, such as a language server, by id and full text.</summary>
/// <remarks>
/// A caller balances each open with one close of the same id, and a rename moves an open document to its new id without closing it. A target throws
/// only for an invalid argument.
/// </remarks>
public interface IDocumentSynchronizationTarget
{
	/// <summary>Opens a document and starts tracking its text.</summary>
	/// <param name="documentId">The identity of the document; a language-server target takes its local file path.</param>
	/// <param name="documentText">The text of the document.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="documentId"/> or <paramref name="documentText"/> is <see langword="null"/>.
	/// </exception>
	void OpenDocument(string documentId, string documentText);

	/// <summary>Replaces the tracked text of a document.</summary>
	/// <param name="documentId">The identity of the document.</param>
	/// <param name="documentText">The new text of the document.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="documentId"/> or <paramref name="documentText"/> is <see langword="null"/>.
	/// </exception>
	void UpdateDocument(string documentId, string documentText);

	/// <summary>Renames an open document to a new id after its file moved.</summary>
	/// <param name="oldDocumentId">The identity the document had.</param>
	/// <param name="newDocumentId">The identity the document has now.</param>
	/// <param name="documentText">The text of the document.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="oldDocumentId"/>, <paramref name="newDocumentId"/>, or <paramref name="documentText"/> is <see langword="null"/>.
	/// </exception>
	/// <remarks>
	/// The open moves with the document, so the close that balances it names <paramref name="newDocumentId"/>.
	/// </remarks>
	void RenameDocument(string oldDocumentId, string newDocumentId, string documentText);

	/// <summary>Closes a document that an earlier open started tracking.</summary>
	/// <param name="documentId">The identity of the document.</param>
	/// <exception cref="ArgumentNullException"><paramref name="documentId"/> is <see langword="null"/>.</exception>
	void CloseDocument(string documentId);
}

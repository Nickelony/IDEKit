namespace Nickelony.LanguageServer.Provider;

/// <summary>Reopens the documents that a restart captured, on the session the restart started.</summary>
internal interface IDocumentRestartReplay
{
	/// <summary>Reopens the captured documents on the new session.</summary>
	/// <param name="documents">The snapshots captured by the document tracker before the restart.</param>
	/// <param name="cancellationToken">A token that can cancel the replay.</param>
	/// <returns>
	/// <see langword="null"/> when every document was processed; otherwise, the documents that still need a reopen,
	/// starting with the one whose reopen failed, so a later start can resume the replay.
	/// </returns>
	Task<IReadOnlyList<DocumentSnapshot>?> ReopenTrackedDocumentsAsync(
		IReadOnlyList<DocumentSnapshot> documents,
		CancellationToken cancellationToken
	);
}

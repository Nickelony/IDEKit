namespace Nickelony.LanguageServer.Provider;

/// <summary>Describes a tracked-document path rename that may need to be mirrored to the language server as a close/open pair.</summary>
/// <param name="PreviousDocument">The tracked snapshot before the rename when the server copy was open; otherwise, <see langword="null"/>.</param>
/// <param name="RenamedDocument">The renamed tracked document snapshot.</param>
/// <param name="ReopenServerDocument">Whether the rename should be mirrored as a close/open document cycle.</param>
internal readonly record struct DocumentRenameDirective(
	DocumentSnapshot? PreviousDocument,
	DocumentSnapshot RenamedDocument,
	bool ReopenServerDocument
);

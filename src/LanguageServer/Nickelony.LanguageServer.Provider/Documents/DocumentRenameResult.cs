namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Describes the outcome of a tracked-document path rekey and, when the record was rekeyed, the request that
/// mirrors the move to the server.
/// </summary>
/// <param name="Outcome">The outcome of the rename attempt.</param>
/// <param name="Request">
/// The rename request to mirror to the server when the record was rekeyed; otherwise, <see langword="null"/>.
/// </param>
public readonly record struct DocumentRenameResult(DocumentRenameOutcome Outcome, DocumentRenameRequest? Request);

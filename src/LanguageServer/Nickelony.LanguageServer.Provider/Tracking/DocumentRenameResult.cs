namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Describes the outcome of a tracked-document path rename and, when the rename landed, the directive that mirrors it to the server.
/// </summary>
/// <param name="Outcome">The outcome of the rename attempt.</param>
/// <param name="Directive">The rename directive to mirror to the server when the rename landed; otherwise, <see langword="null"/>.</param>
internal readonly record struct DocumentRenameResult(DocumentRenameOutcome Outcome, DocumentRenameDirective? Directive);

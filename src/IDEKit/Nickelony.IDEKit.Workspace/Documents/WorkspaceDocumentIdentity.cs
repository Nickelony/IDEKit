namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>Identifies one logical instance of a workspace document.</summary>
/// <remarks>
/// The key remains stable when the document instance is renamed or saved at a new path; a document reopened after the instance was deleted receives
/// a new key. The store generates the key with a new <see cref="Guid"/> for every opened document, so an empty <see cref="Guid"/> never identifies
/// a tracked instance.
/// </remarks>
public readonly record struct WorkspaceDocumentKey(Guid Value);

/// <summary>Identifies a specific version of a workspace document in requests and results.</summary>
/// <remarks>
/// A request carries the identity the caller expects; a result echoes the requested identity so a caller can correlate the result with its request
/// even when validation rejected it. <see cref="DocumentId"/> is the normalized path, and <see cref="Version"/> is the optimistic-concurrency token,
/// so requests that do not mutate the document (for example a reload) still use the same identity shape.
/// </remarks>
public readonly record struct WorkspaceDocumentRequestIdentity(WorkspaceDocumentKey DocumentKey, string DocumentId, long Version);

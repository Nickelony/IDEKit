namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Lightweight value-type carrier for an in-flight document synchronization request.
/// </summary>
/// <param name="Kind">The synchronization action to perform.</param>
/// <param name="Document">The document snapshot to synchronize.</param>
/// <param name="ChangeRange">The incremental change range, when the synchronization is incremental.</param>
/// <remarks>
/// <para>
/// A <see cref="DocumentSynchronizationKind.Change"/> request with a <see langword="null"/> <see cref="ChangeRange"/>
/// means the caller opted out of the incremental diff computation (<c>includeChangeRange: false</c>) and must send
/// the full document content instead.
/// </para>
/// <para>
/// A <see cref="DocumentSynchronizationKind.Close"/> request carries only the snapshot whose server copy should be
/// closed; its <see cref="ChangeRange"/> is always <see langword="null"/>.
/// </para>
/// </remarks>
public readonly record struct DocumentSynchronizationRequest(
	DocumentSynchronizationKind Kind,
	DocumentSnapshot Document,
	DocumentChangeRange? ChangeRange = null);

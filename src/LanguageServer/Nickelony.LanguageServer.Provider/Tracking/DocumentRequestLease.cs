using System.Diagnostics.CodeAnalysis;

namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Identifies one temporary request-driven lease acquired from a tracked document, so the lease can be released by identity even when the document
/// record's path is renamed after the acquisition.
/// </summary>
/// <remarks>
/// <para>
/// The document tracker binds the lease under its tracker lock, in the same step that acquires the request lease, so an acquired lease
/// is always releasable.
/// </para>
/// <para>
/// A lease is single-use: an instance can be bound once, and binding an already-bound
/// or released lease fails with an <see cref="InvalidOperationException"/> instead of silently
/// losing the release handle. <see cref="DocumentTracker.ReleaseRequestByLease"/> releases a bound lease exactly once; later calls are no-ops.
/// <see cref="CurrentFilePath"/> keeps reporting the record's current normalized path after the release, so callers can still follow a record
/// whose lease has been released, even after a rename.
/// </para>
/// </remarks>
internal sealed class DocumentRequestLease
{
	private int _released;
	private TrackedDocumentState? _state;

	/// <summary>Indicates whether the lease was bound to a tracked document record.</summary>
	internal bool IsAcquired => Volatile.Read(ref _state) is not null;

	/// <summary>The current normalized file path of the bound document record, or <see langword="null"/> when no lease was acquired.</summary>
	internal string? CurrentFilePath => Volatile.Read(ref _state)?.FilePath;

	internal bool CanBind => Volatile.Read(ref _state) is null && Volatile.Read(ref _released) == 0;

	/// <summary>Binds the lease to the document record whose request lease was acquired.</summary>
	/// <param name="state">The tracked document state that owns the acquired lease.</param>
	/// <exception cref="InvalidOperationException">The lease was already bound or released.</exception>
	internal void Bind(TrackedDocumentState state)
	{
		if (!CanBind)
			throw new InvalidOperationException("A document request lease is single-use and cannot be bound again.");

		Volatile.Write(ref _state, state);
	}

	/// <summary>Claims the one-time release of the bound lease.</summary>
	/// <param name="state">Receives the bound document state when the claim succeeded.</param>
	/// <returns><see langword="true"/> when this call owns the release; otherwise, <see langword="false"/>.</returns>
	internal bool TryClaimRelease([NotNullWhen(true)] out TrackedDocumentState? state)
	{
		state = Volatile.Read(ref _state);
		return state is not null && Interlocked.Exchange(ref _released, 1) == 0;
	}
}

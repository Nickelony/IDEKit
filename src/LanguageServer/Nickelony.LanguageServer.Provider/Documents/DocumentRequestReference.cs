using System.Diagnostics.CodeAnalysis;

namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Identifies one temporary request-driven reference acquired from a tracked document, so the reference can be
/// released by identity even when the document record was rekeyed (renamed) after the acquisition.
/// </summary>
/// <remarks>
/// <para>
/// The tracked-document store binds the reference while it holds its store lock, so an acquired reference stays
/// releasable on every exit path, including when a store hook throws after the acquisition.
/// </para>
/// <para>
/// A reference is single-use: each instance can be bound once, and binding a reference that was already bound or
/// released fails with an <see cref="InvalidOperationException"/> instead of silently losing the release handle.
/// <c>TrackedDocumentStore.ReleaseRequestByReference</c> releases a bound reference exactly once;
/// later calls are no-ops. <see cref="CurrentFilePath"/> keeps reporting the record's current normalized path
/// after the release, so callers can follow a record that was renamed while its reference was being released.
/// </para>
/// </remarks>
public sealed class DocumentRequestReference
{
	private int _released;
	private TrackedDocumentState? _state;

	/// <summary>
	/// Gets a value indicating whether the reference was bound to a tracked document record.
	/// </summary>
	public bool IsAcquired => Volatile.Read(ref _state) is not null;

	/// <summary>
	/// Gets the current normalized file path of the bound document record, or <see langword="null"/> when no
	/// reference was acquired.
	/// </summary>
	public string? CurrentFilePath => Volatile.Read(ref _state)?.FilePath;

	/// <summary>
	/// Gets a value indicating whether the reference can still be bound to a document record.
	/// </summary>
	internal bool CanBind => Volatile.Read(ref _state) is null && Volatile.Read(ref _released) == 0;

	/// <summary>
	/// Binds the reference to the document record whose request reference was acquired. A reference is single-use;
	/// binding one that was already bound or released throws.
	/// </summary>
	/// <param name="state">The tracked document state that owns the acquired reference.</param>
	/// <exception cref="InvalidOperationException">The reference was already bound or released.</exception>
	internal void Bind(TrackedDocumentState state)
	{
		if (!CanBind)
			throw new InvalidOperationException("A document request reference is single-use and cannot be bound again.");

		Volatile.Write(ref _state, state);
	}

	/// <summary>
	/// Claims the one-time release of the bound reference.
	/// </summary>
	/// <param name="state">Receives the bound document state when the claim succeeded.</param>
	/// <returns><see langword="true"/> when this call owns the release; otherwise, <see langword="false"/>.</returns>
	internal bool TryClaimRelease([NotNullWhen(true)] out TrackedDocumentState? state)
	{
		state = Volatile.Read(ref _state);

		return state is not null && Interlocked.Exchange(ref _released, 1) == 0;
	}
}

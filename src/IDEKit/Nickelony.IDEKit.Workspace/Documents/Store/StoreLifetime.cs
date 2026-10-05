namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>Owns the cancellation that every operation of a store observes until the store is disposed.</summary>
/// <remarks>
/// An operation that disposal waits for never links after <see cref="Dispose"/>. A directory operation that is not yet admitted
/// is not waited for, so it links through <see cref="CreateLinkedCancellationOrNull"/> and reports its canceled outcome when disposal
/// already completed.
/// </remarks>
internal sealed class StoreLifetime : IDisposable
{
	private readonly CancellationTokenSource _source = new();

	/// <summary>Links a caller's token to the lifetime.</summary>
	/// <param name="cancellationToken">The caller's token.</param>
	/// <returns>A source that is canceled when the caller's token or the lifetime is, which the caller disposes.</returns>
	/// <exception cref="ObjectDisposedException">The lifetime was disposed.</exception>
	internal CancellationTokenSource CreateLinkedCancellation(CancellationToken cancellationToken) =>
		CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _source.Token);

	/// <summary>Links a caller's token to the lifetime unless the lifetime was disposed.</summary>
	/// <param name="cancellationToken">The caller's token.</param>
	/// <returns>The linked source, or <see langword="null"/> when the lifetime was disposed.</returns>
	internal CancellationTokenSource? CreateLinkedCancellationOrNull(CancellationToken cancellationToken)
	{
		try
		{
			return CreateLinkedCancellation(cancellationToken);
		}
		catch (ObjectDisposedException)
		{
			return null;
		}
	}

	/// <summary>Cancels every source linked to the lifetime and every source linked to it later.</summary>
	/// <exception cref="ObjectDisposedException">The lifetime was disposed.</exception>
	/// <remarks>
	/// A callback registered on a linked token can run inline on the canceling thread, so a caller that holds a lock a callback may need
	/// cancels after it releases the lock. A repeated call and a throwing callback leave the lifetime canceled and do not throw. The owner cancels
	/// before it disposes, because a disposed lifetime drops its registrations without invoking them.
	/// </remarks>
	internal void Cancel()
	{
		try
		{
			_source.Cancel();
		}
		catch (AggregateException)
		{
			// A cancellation callback registered on the lifetime token threw. The token is canceled either
			// way, and a host callback failure must not fault the store's disposal.
		}
	}

	/// <summary>Releases the source; a later link attempt fails.</summary>
	public void Dispose() => _source.Dispose();
}

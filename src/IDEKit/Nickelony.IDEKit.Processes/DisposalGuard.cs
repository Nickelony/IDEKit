namespace Nickelony.IDEKit.Processes;

/// <summary>
/// Guards a disposable adapter so member calls and disposal can run concurrently.
/// </summary>
/// <remarks>
/// Disposal is a one-time claim: the first <see cref="TryBeginDispose"/> caller owns the teardown and every
/// later caller is a no-op. Members run through <see cref="Execute{T}(object, Func{T})"/>, which gives a racing
/// member one contract instead of the adapter's inner failure: a member called after disposal, or one whose
/// operation fails because disposal completed while it ran, throws <see cref="ObjectDisposedException"/>. Only
/// the failure a released resource raises while it tears down is translated (the
/// <see cref="System.Diagnostics.Process"/> torn-down <see cref="InvalidOperationException"/> shape); every
/// other failure keeps its own type, so a racing <c>Kill</c>'s <see cref="System.ComponentModel.Win32Exception"/>
/// or <see cref="System.IO.IOException"/>, a genuine read fault, and a caller's
/// <see cref="System.OperationCanceledException"/> are not masked by the disposal. A member whose own failure is
/// meaningful before disposal runs through <see cref="ExecuteUnmasked{T}(object, Func{T})"/>, which never
/// translates.
/// </remarks>
internal sealed class DisposalGuard
{
	private int _disposed;

	/// <summary>
	/// Gets a value indicating whether disposal has been claimed.
	/// </summary>
	internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;

	/// <summary>
	/// Claims disposal for the calling thread.
	/// </summary>
	/// <returns><see langword="true"/> for the caller that claimed disposal; otherwise, <see langword="false"/>.</returns>
	internal bool TryBeginDispose() => Interlocked.Exchange(ref _disposed, 1) == 0;

	/// <summary>
	/// Runs an operation against the guarded adapter and translates a failure observed after disposal.
	/// </summary>
	/// <typeparam name="T">The operation result type.</typeparam>
	/// <param name="instance">The guarded adapter, used as the name reported by <see cref="ObjectDisposedException"/>.</param>
	/// <param name="operation">The operation to run.</param>
	/// <returns>The operation result.</returns>
	/// <exception cref="ObjectDisposedException">The adapter was disposed before or during the operation.</exception>
	internal T Execute<T>(object instance, Func<T> operation)
	{
		ObjectDisposedException.ThrowIf(IsDisposed, instance);

		try
		{
			return operation();
		}
		catch (Exception exception) when (ShouldTranslate(exception))
		{
			throw TranslateDisposalFailure(instance, exception);
		}
	}

	/// <summary>
	/// Runs an operation against the guarded adapter and translates a failure observed after disposal.
	/// </summary>
	/// <param name="instance">The guarded adapter, used as the name reported by <see cref="ObjectDisposedException"/>.</param>
	/// <param name="operation">The operation to run.</param>
	/// <exception cref="ObjectDisposedException">The adapter was disposed before or during the operation.</exception>
	internal void Execute(object instance, Action operation)
	{
		ObjectDisposedException.ThrowIf(IsDisposed, instance);

		try
		{
			operation();
		}
		catch (Exception exception) when (ShouldTranslate(exception))
		{
			throw TranslateDisposalFailure(instance, exception);
		}
	}

	/// <summary>
	/// Runs an operation whose own failures must reach the caller unchanged.
	/// </summary>
	/// <remarks>
	/// Only the pre-check reports disposal: a failure the operation raises while disposal runs keeps its
	/// own type, so a member whose failure is meaningful before disposal - an exit code read before the
	/// process exited, for example - is never reported as the disposal result.
	/// </remarks>
	/// <typeparam name="T">The operation result type.</typeparam>
	/// <param name="instance">The guarded adapter, used as the name reported by <see cref="ObjectDisposedException"/>.</param>
	/// <param name="operation">The operation to run.</param>
	/// <returns>The operation result.</returns>
	/// <exception cref="ObjectDisposedException">Disposal has been claimed.</exception>
	internal T ExecuteUnmasked<T>(object instance, Func<T> operation)
	{
		ThrowIfDisposed(instance);

		return operation();
	}

	/// <summary>
	/// Guards a call against a claimed disposal.
	/// </summary>
	/// <param name="instance">The guarded adapter, used as the name reported by <see cref="ObjectDisposedException"/>.</param>
	/// <exception cref="ObjectDisposedException">Disposal has been claimed.</exception>
	internal void ThrowIfDisposed(object instance)
		=> ObjectDisposedException.ThrowIf(IsDisposed, instance);

	// A failure an operation raises after disposal is the adapter's inner reaction to the released
	// resources, not a real failure of the caller's call, so it is reported as the documented disposal
	// result. Only the shape a released resource raises while it tears down is translated - an
	// InvalidOperationException from the disposed Process - because only it is indistinguishable from "the
	// adapter is gone". Every other failure keeps its own type: an inner ObjectDisposedException already
	// matches the contract and is left as it is, and an IOException or Win32Exception from a racing Kill, a
	// genuine read fault, or a caller's OperationCanceledException must not be masked by the disposal.
	private bool ShouldTranslate(Exception exception)
		=> IsDisposed && exception is InvalidOperationException and not ObjectDisposedException;

	// ObjectDisposedException has no constructor that accepts an inner exception, so the original failure's
	// type and message are carried on the disposal message instead of being discarded.
	private static ObjectDisposedException TranslateDisposalFailure(object instance, Exception exception)
		=> new(
			instance.GetType().FullName,
			$"The adapter was disposed before or during the operation. The underlying failure was {exception.GetType().Name}: {exception.Message}");
}

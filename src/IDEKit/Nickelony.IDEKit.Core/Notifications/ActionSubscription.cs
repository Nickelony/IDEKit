namespace Nickelony.IDEKit.Core.Notifications;

/// <summary>
/// Turns an unsubscribe action into a single-shot <see cref="IDisposable"/> so an event subscription can be detached with a <c>using</c>
/// or a stored handle.
/// </summary>
/// <remarks>
/// Disposing runs the action once and clears the reference, so a second dispose is a no-op. The class is not thread-safe.
/// </remarks>
public sealed class ActionSubscription : IDisposable
{
	private Action? _unsubscribe;

	/// <param name="unsubscribe">The action that detaches the handlers.</param>
	/// <exception cref="ArgumentNullException"><paramref name="unsubscribe"/> is <see langword="null"/>.</exception>
	public ActionSubscription(Action unsubscribe)
	{
		ArgumentNullException.ThrowIfNull(unsubscribe);
		_unsubscribe = unsubscribe;
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		_unsubscribe?.Invoke();
		_unsubscribe = null;
	}
}

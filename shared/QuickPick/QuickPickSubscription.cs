#if AVALONIAQUICKPICK

namespace Nickelony.QuickPick.Avalonia;

#else

namespace Nickelony.QuickPick.Wpf;

#endif

/// <summary>Turns an unsubscribe action into a single-shot <see cref="IDisposable"/>, so the handlers of a window detach with one call.</summary>
/// <remarks>
/// Disposing runs the action once and clears the reference, so a second dispose does nothing. The class is not thread-safe.
/// </remarks>
internal sealed class QuickPickSubscription : IDisposable
{
	private Action? _unsubscribe;

	/// <param name="unsubscribe">The action that detaches the handlers.</param>
	internal QuickPickSubscription(Action unsubscribe) => _unsubscribe = unsubscribe;

	/// <inheritdoc/>
	public void Dispose()
	{
		_unsubscribe?.Invoke();
		_unsubscribe = null;
	}
}

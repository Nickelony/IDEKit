namespace Nickelony.IDEKit.Core.Notifications;

/// <summary>Implemented by a source that raises a change notification when the content it exposes to consumers may have changed.</summary>
/// <remarks>
/// Raise <see cref="Changed"/> whenever the exposed content may have changed; a subscribed consumer refreshes itself on the notification,
/// so no manual invalidation is needed. Implementations may raise the event on any thread, so a subscriber that touches thread-affine state
/// must marshal that work to its own thread.
/// </remarks>
public interface IChangeNotificationSource
{
	/// <summary>Raised when the content the source exposes to consumers may have changed.</summary>
	event EventHandler? Changed;
}

namespace Nickelony.IDEKit.Core.Notifications;

/// <summary>
/// Implemented by a source that raises a change notification when the content it exposes to
/// consumers may have changed.
/// </summary>
/// <remarks>
/// <para>
/// A consumer that displays the source's content (for example a line-status margin or a code-action
/// indicator) subscribes to <see cref="Changed"/> while it is attached and unsubscribes when it is
/// detached, so an attached consumer refreshes itself after the source changes without a manual
/// invalidation call.
/// </para>
/// <para>
/// Implementations may raise the event on any thread; a consumer or host subscriber that touches
/// thread-affine state must marshal that work to its own thread.
/// </para>
/// </remarks>
public interface IChangeNotificationSource
{
	/// <summary>
	/// Raised when the content the source exposes to consumers may have changed.
	/// </summary>
	event EventHandler? Changed;
}

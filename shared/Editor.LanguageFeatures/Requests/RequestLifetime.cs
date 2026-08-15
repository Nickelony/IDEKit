using Nickelony.IDEKit.Core.Requests;

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Requests;
#else
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Requests;
#endif

/// <summary>
/// Owns the request lifetime the language-feature controllers share: one
/// <see cref="LatestRequestCoordinator"/> plus the controller's disposal gate, so the disposal flag and
/// the cancel/invalidate pipeline are defined once instead of being re-implemented by every controller.
/// </summary>
/// <remarks>
/// <para>
/// The type is composed into a controller rather than inherited from: a controller derives from nothing
/// and the lifetime is an implementation detail, so the shape is reusable without publishing a base
/// class or committing to a public inheritance hierarchy. The disposal gate is a single volatile flag
/// read by every public entry point, so a lifecycle fix lands here once instead of at every controller.
/// </para>
/// <para>
/// A controller marks the lifetime disposed before it tears down its own state, so a request
/// continuation that observes the flag stops publishing before any controller state disappears.
/// </para>
/// </remarks>
internal sealed class RequestLifetime
{
	private readonly LatestRequestCoordinator _coordinator;
	private volatile bool _isDisposed;

	/// <summary>
	/// Initializes a lifetime over a new coordinator.
	/// </summary>
	public RequestLifetime()
		: this(new LatestRequestCoordinator())
	{
	}

	/// <summary>
	/// Initializes a lifetime over an existing coordinator, so a controller that already exposes a
	/// request coordinator (the completion session) shares one request lifetime with it.
	/// </summary>
	/// <param name="coordinator">The coordinator whose lifetime this instance gates.</param>
	/// <exception cref="ArgumentNullException"><paramref name="coordinator"/> is <see langword="null"/>.</exception>
	public RequestLifetime(LatestRequestCoordinator coordinator)
	{
		ArgumentNullException.ThrowIfNull(coordinator);

		_coordinator = coordinator;
	}

	/// <summary>
	/// Gets a value indicating whether the owning controller has been disposed.
	/// </summary>
	public bool IsDisposed => _isDisposed;

	/// <summary>
	/// Gets the request coordinator whose requests this lifetime gates.
	/// </summary>
	public LatestRequestCoordinator Coordinator => _coordinator;

	/// <summary>
	/// Cancels the outstanding request's token, if any, unless the owner is already disposed.
	/// </summary>
	public void CancelInFlightRequest()
	{
		if (!_isDisposed)
			_coordinator.CancelPendingRequest();
	}

	/// <summary>
	/// Marks outstanding requests as stale unless the owner is already disposed.
	/// </summary>
	public void InvalidateRequests()
	{
		if (!_isDisposed)
			_coordinator.Invalidate();
	}

	/// <summary>
	/// Cancels the outstanding request and marks outstanding requests as stale, so a context change
	/// leaves no request that could still publish. A no-op once the owner is disposed.
	/// </summary>
	public void CancelAndInvalidate()
	{
		if (_isDisposed)
			return;

		_coordinator.CancelPendingRequest();
		_coordinator.Invalidate();
	}

	/// <summary>
	/// Ends the lifetime: marks the owner disposed, then cancels and invalidates outstanding requests.
	/// The method is idempotent.
	/// </summary>
	public void Dispose()
	{
		if (_isDisposed)
			return;

		_isDisposed = true;
		_coordinator.CancelPendingRequest();
		_coordinator.Invalidate();
	}
}

using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views;

/// <summary>
/// Coordinates view attachment, publication, and synchronization with workspace document authority.
/// </summary>
/// <remarks>
/// <para>
/// The manager tracks its active operations and its registered views. It tracks views by normalized
/// document id and records failed view synchronization as unsynchronized, retaining the failure
/// detail for reporting; a successful refresh or identity acknowledgment clears that state, while
/// unregistering the view always removes it. The recovery operations (commit, reload, and conflict
/// resolution) retry the synchronization of a view whose only outstanding state is such a failure
/// instead of blocking the operation permanently.
/// </para>
/// <para>
/// The manager owns no thread or message loop: the constructor delegate is the only view-affinity
/// mechanism, and the manager never reads or invokes a view member while holding its own state lock.
/// A throwing view accessor is isolated: reads used for bookkeeping degrade to a safe default, and
/// the operation reports the view as unsynchronized where it can report a result. The
/// <see cref="IWorkspaceDocumentView.ApplyRequested"/> subscription is the one documented exception
/// to dispatching: the manager never dispatches the subscription add or a direct unregister removal,
/// so event accessors must be usable from any thread.
/// </para>
/// </remarks>
public sealed partial class WorkspaceDocumentManager : IWorkspaceDocumentManager
{
	private readonly object _stateLock = new();
	private readonly IWorkspaceDocumentStore _store;
	private readonly Func<Action, Task> _dispatchViewAction;

	// The view-keyed dictionary uses reference equality: a view's identity is its instance, and a
	// consumer type that overrides value equality must not make two distinct views collide. View ids
	// are captured through the dispatch delegate at registration, so the manager never reads a view
	// member while holding the state lock.
	private readonly Dictionary<IWorkspaceDocumentView, ViewRegistration> _registrations = new(ReferenceEqualityComparer.Instance);
	private readonly Dictionary<string, List<IWorkspaceDocumentView>> _viewsByDocument;
	private readonly Dictionary<string, IWorkspaceDocumentView> _viewsByViewId = new(StringComparer.Ordinal);
	private readonly List<Task> _activeOperations = [];
	private Task? _stopTask;
	private bool _stopping;

	// The detail reported when a view cannot supply a usable id, shared by attachment validation and
	// the attach path so both report the same message.
	private const string ViewIdUnreportedMessage = "The view did not report a view id.";

	/// <summary>
	/// Initializes a new instance of the <see cref="WorkspaceDocumentManager"/> class.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The dispatch delegate is the only view-affinity mechanism: the manager runs every action that
	/// touches a view member through it and never blocks while waiting. The returned task must
	/// complete only after the action has run, so the manager can read the results the action
	/// captured; completion may itself be asynchronous, which supports queue-based dispatchers that
	/// cannot block their caller.
	/// </para>
	/// <para>
	/// The delegate hosts every view thread-affinity decision: actions may be issued from arbitrary
	/// threads and from concurrent manager operations, and the manager may invoke the delegate again
	/// from inside a dispatched action when a view raises
	/// <see cref="IWorkspaceDocumentView.ApplyRequested"/>. A delegate whose views are not thread-safe
	/// or reentrancy-safe must serialize or queue the actions itself; the manager gives no ordering
	/// guarantee between concurrent operations. One manager coordinates one store: a second manager
	/// over the same store would track only its own views.
	/// </para>
	/// <para>
	/// A view fault is reported through the operation result where the operation reports one and is
	/// recorded as unsynchronized view state otherwise; a fault raised by the delegate itself surfaces
	/// as a failure of the operation (the open path reports it as
	/// <see cref="WorkspaceDocumentManagerOpenOutcome.OpenFailed"/>).
	/// </para>
	/// </remarks>
	/// <param name="store">The workspace document store that owns document content and disk operations.</param>
	/// <param name="dispatchViewAction">
	/// The delegate that runs actions touching a view's members where view access is legal, such as a
	/// message-loop dispatcher or the direct inline adapter that runs the action and returns
	/// <see cref="Task.CompletedTask"/>.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="store"/> or <paramref name="dispatchViewAction"/> is <see langword="null"/>.</exception>
	public WorkspaceDocumentManager(
		IWorkspaceDocumentStore store,
		Func<Action, Task> dispatchViewAction)
	{
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(dispatchViewAction);

		_store = store;
		_dispatchViewAction = dispatchViewAction;
		_viewsByDocument = new Dictionary<string, List<IWorkspaceDocumentView>>(_store.PathComparison.Comparer);
	}

	/// <inheritdoc/>
	public IWorkspaceDocumentReader Documents => _store;

	// Tracks the registration state of one attached view. Instances are only read and written under
	// the state lock; the view id is captured at registration and never changes for a registration.
	// The last failure survives until a successful refresh or identity acknowledgment clears the
	// unsynchronized flag, so reports can explain why the view needs attention. Each registration owns
	// its subscription token, so a removal matches its own add even when the same view is unregistered
	// and re-attached while an earlier attach is still subscribing.
	private sealed class ViewRegistration(
		string viewId,
		string documentId,
		WorkspaceDocumentKey documentKey,
		long version,
		ApplyRequestedSubscription subscription)
	{
		public string ViewId { get; } = viewId;

		public string DocumentId { get; set; } = documentId;

		public WorkspaceDocumentKey DocumentKey { get; set; } = documentKey;

		public long Version { get; set; } = version;

		public bool Unsynchronized { get; set; }

		public WorkspaceOperationFailure? LastFailure { get; set; }

		public ApplyRequestedSubscription Subscription { get; } = subscription;
	}

	// A per-registration subscription token. Delegate equality compares the target and the method, so
	// every registration owns a distinct forwarder instance and the multicast `-=` removes exactly the
	// entry that registration added. A shared method-group handler cannot be told apart: `-=` drops the
	// last matching entry, so when the same view is unregistered and re-attached while an earlier
	// attach is still subscribing, one attach's undo could strip the other's live handler. The token
	// keeps every add matched to its own removal.
	private sealed class ApplyRequestedSubscription
	{
		private readonly WorkspaceDocumentManager _manager;

		public ApplyRequestedSubscription(WorkspaceDocumentManager manager)
		{
			_manager = manager;
			Handler = Forward;
		}

		public EventHandler<WorkspaceDocumentViewApplyRequestedEventArgs> Handler { get; }

		private void Forward(object? sender, WorkspaceDocumentViewApplyRequestedEventArgs eventArgs)
			=> _manager.OnApplyRequested(sender, eventArgs);
	}
}

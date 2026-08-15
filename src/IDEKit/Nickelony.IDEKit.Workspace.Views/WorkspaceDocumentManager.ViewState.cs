using Nickelony.IDEKit.Workspace.Documents;
using System.Runtime.CompilerServices;

namespace Nickelony.IDEKit.Workspace.Views;

public sealed partial class WorkspaceDocumentManager
{
	private static WorkspaceDocumentViewIdentityResult TryAcknowledgeIdentity(
		IWorkspaceDocumentView view,
		WorkspaceDocumentViewIdentityChange change)
	{
		try
		{
			return view.AcknowledgeIdentity(change);
		}
		catch (Exception exception)
		{
			return new WorkspaceDocumentViewIdentityResult(
				WorkspaceDocumentViewIdentityOutcome.Failed,
				new WorkspaceOperationFailure(WorkspaceViewOperationFailureCodes.ViewIdentityUpdateFailed, exception.Message, exception));
		}
	}

	// Moves a view's registration to the identity of the snapshot it acknowledged and clears the
	// unsynchronized state: a successful identity acknowledgment makes the view current, exactly like
	// a successful refresh. The previous document id is read from the registration itself, so a stale
	// caller-supplied id cannot leave the view in the vacated list or duplicate it in the new one.
	private void UpdateViewIdentity(
		IWorkspaceDocumentView view,
		WorkspaceDocumentSnapshot snapshot)
	{
		lock (_stateLock)
		{
			if (!_registrations.TryGetValue(view, out ViewRegistration? registration))
				return;

			string oldDocumentId = registration.DocumentId;
			registration.DocumentId = snapshot.DocumentId;
			registration.DocumentKey = snapshot.DocumentKey;
			registration.Version = snapshot.Version;
			registration.Unsynchronized = false;
			registration.LastFailure = null;

			if (string.Equals(oldDocumentId, snapshot.DocumentId, StringComparison.Ordinal))
				return;

			if (_viewsByDocument.TryGetValue(oldDocumentId, out List<IWorkspaceDocumentView>? oldViews))
			{
				oldViews.Remove(view);
				if (oldViews.Count == 0)
					_viewsByDocument.Remove(oldDocumentId);
			}

			if (!_viewsByDocument.TryGetValue(snapshot.DocumentId, out List<IWorkspaceDocumentView>? newViews))
			{
				newViews = [];
				_viewsByDocument.Add(snapshot.DocumentId, newViews);
			}

			newViews.Add(view);
		}
	}

	// Reads a view id for diagnostics without letting a throwing accessor escape a teardown loop: a
	// view that cannot report a usable id is reported with the documented marker plus a per-instance
	// discriminator, and the remaining views are still released. The discriminator keeps two such views
	// from collapsing into one issue when the issue list is normalized by view id.
	private static string ReadViewId(IWorkspaceDocumentView view)
	{
		try
		{
			string viewId = view.ViewId;
			if (!string.IsNullOrWhiteSpace(viewId))
				return viewId;
		}
		catch
		{
			// Fall through to the unidentified marker.
		}

		return $"(unidentified view {RuntimeHelpers.GetHashCode(view):X8})";
	}

	/// <inheritdoc/>
	public void UnregisterOpenView(IWorkspaceDocumentView view)
	{
		ArgumentNullException.ThrowIfNull(view);
		ViewRegistration? registration;

		lock (_stateLock)
			registration = RemoveViewRegistrationLocked(view);

		if (registration is null)
			return;

		try
		{
			view.ApplyRequested -= registration.Subscription.Handler;
		}
		catch
		{
			// The registration is already removed, so later raises are ignored; a throwing event accessor
			// must not fault the caller, matching the stop and delete teardown paths.
		}
	}

	// Removes a view's registration under the state lock and returns it, or null when the view was not
	// registered. The event subscription is not touched here: view accessors must not run while the
	// state lock is held.
	private ViewRegistration? RemoveViewRegistrationLocked(IWorkspaceDocumentView view)
	{
		if (!_registrations.Remove(view, out ViewRegistration? registration))
			return null;

		_viewsByViewId.Remove(registration.ViewId);

		if (_viewsByDocument.TryGetValue(registration.DocumentId, out List<IWorkspaceDocumentView>? documentViews))
		{
			documentViews.Remove(view);
			if (documentViews.Count == 0)
				_viewsByDocument.Remove(registration.DocumentId);
		}

		return registration;
	}

	// Removes a view's registration only while it is the given registration instance, so a rollback
	// cannot detach a registration that a concurrent re-attach created after this attach's own was
	// replaced. The event subscription is not touched here: view accessors must not run while the
	// state lock is held.
	private bool RemoveRegistrationLockedIfCurrent(IWorkspaceDocumentView view, ViewRegistration registration)
	{
		if (!_registrations.TryGetValue(view, out ViewRegistration? current)
			|| !ReferenceEquals(current, registration))
			return false;

		return RemoveViewRegistrationLocked(view) is not null;
	}

	// Snapshots the registered views under the state lock; view members are not read here.
	private IWorkspaceDocumentView[] GetRegisteredViews()
	{
		lock (_stateLock)
			return [.. _registrations.Keys];
	}

	// Reads the normalized document id a registered view is currently bound to, or null when the view
	// is no longer registered.
	private string? GetRegisteredDocumentId(IWorkspaceDocumentView view)
	{
		lock (_stateLock)
			return _registrations.TryGetValue(view, out ViewRegistration? registration) ? registration.DocumentId : null;
	}

	private bool IsViewMarkedUnsynchronized(IWorkspaceDocumentView view)
	{
		lock (_stateLock)
			return _registrations.TryGetValue(view, out ViewRegistration? registration) && registration.Unsynchronized;
	}

	// Resolves the current snapshot of the document a registered view is attached to, or null when
	// the view is no longer registered or its document is no longer tracked. The binding is read
	// under the state lock; the snapshot is resolved through the document authority.
	private WorkspaceDocumentSnapshot? GetRegisteredViewSnapshot(IWorkspaceDocumentView view)
	{
		string documentId;
		lock (_stateLock)
		{
			if (!_registrations.TryGetValue(view, out ViewRegistration? registration))
				return null;

			documentId = registration.DocumentId;
		}

		return GetCurrentSnapshot(documentId);
	}

	private bool IsRegistered(IWorkspaceDocumentView view)
	{
		lock (_stateLock)
			return !_stopping && _registrations.ContainsKey(view);
	}

	private void RecordViewResult(
		IWorkspaceDocumentView view,
		bool synchronized,
		WorkspaceOperationFailure? failure,
		WorkspaceDocumentSnapshot? snapshot)
	{
		lock (_stateLock)
		{
			// An unregistered view is no longer coordinated; recording state for it would leak until the
			// manager stops.
			if (!_registrations.TryGetValue(view, out ViewRegistration? registration))
				return;

			if (synchronized)
			{
				registration.Unsynchronized = false;
				registration.LastFailure = null;

				// The recorded version only advances: two interleaved refreshes can deliver an older
				// snapshot after a newer one, and moving the version backwards would make a later
				// IsViewBehind check skip a needed refresh.
				if (snapshot is not null)
					registration.Version = Math.Max(registration.Version, snapshot.Version);
			}
			else
			{
				registration.Unsynchronized = true;

				// The recorded failure describes the current synchronization problem; a later failure
				// without detail keeps the last known reason instead of discarding it.
				if (failure is not null)
					registration.LastFailure = failure;
			}
		}
	}

	private bool IsViewBehind(IWorkspaceDocumentView view, long version)
	{
		lock (_stateLock)
			return _registrations.TryGetValue(view, out ViewRegistration? registration)
				&& registration.Version < version;
	}

	private void RecordViewFailure(IWorkspaceDocumentView view, WorkspaceOperationFailure? failure = null)
	{
		lock (_stateLock)
		{
			// Unregistered views are no longer coordinated; tracking them would retain them until stop.
			if (_registrations.TryGetValue(view, out ViewRegistration? registration))
			{
				registration.Unsynchronized = true;

				// Keep the last known failure detail when a later failure has none.
				if (failure is not null)
					registration.LastFailure = failure;
			}
		}
	}
}

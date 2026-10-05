using System.Diagnostics.CodeAnalysis;
using Nickelony.IDEKit.Core.Threading;
using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views.Coordination;

/// <summary>
/// Tracks the views attached to documents, by view instance, by view id and by document id, and queues the changes of the attached documents.
/// </summary>
/// <remarks>
/// <para>
/// The tracker shares the coordinator's lock and its stop gate. A registration is admitted under the same critical section that checks for a stop,
/// and the stop's teardown drains the tracker under that lock, so a view cannot register after the teardown snapshot and keep a live subscription
/// on a stopped coordinator.
/// </para>
/// <para>
/// The tracker never reads or invokes a view member while it holds the lock. The view-keyed dictionary compares by reference,
/// so a consumer type that overrides value equality cannot make two distinct views collide and the lock never reaches a view's <c>Equals</c>.
/// View ids are captured through the dispatcher at registration, so a lookup by id never reads a view. The tracker holds state
/// and answers queries only; the members that close, detach or subscribe a view are on the attacher.
/// </para>
/// <para>
/// An attached document is keyed by its document key, which a rename keeps. The registration that first attaches a key queues its open, and the removal
/// of its last registration queues its close, under the lock that changes the registrations, so the queue holds one open, then the content
/// changes and renames, then one close for each attachment of a document. A snapshot changes an attached document only when its version is newer
/// than the one the tracker holds, so a late operation cannot move the document backwards.
/// </para>
/// </remarks>
internal sealed class ViewTracker
{
	private readonly object _owner;
	private readonly object _syncRoot;

	private readonly StopGate<Task> _stopGate;

	private readonly EventHandler<ApplyRequestedEventArgs> _onApplyRequested;

	private readonly Dictionary<IWorkspaceDocumentView, ViewEntry> _registrations = new(ReferenceEqualityComparer.Instance);
	private readonly Dictionary<string, List<IWorkspaceDocumentView>> _viewsByDocument;
	private readonly Dictionary<string, IWorkspaceDocumentView> _viewsByViewId = new(StringComparer.Ordinal);
	private readonly Dictionary<WorkspaceDocumentKey, AttachedDocumentEntry> _attachedDocuments = [];
	private readonly Queue<AttachedDocumentChangedEventArgs> _pendingChanges = new();

	/// <param name="owner">The coordinator, named by the exception that rejects a registration after a stop began.</param>
	/// <param name="syncRoot">The coordinator's lock, which also orders the stop gate.</param>
	/// <param name="stopGate">The coordinator's stop gate, read under <paramref name="syncRoot"/>.</param>
	/// <param name="options">The workspace options whose path comparison decides whether two document ids name one document.</param>
	/// <param name="onApplyRequested">The handler that receives the apply requests of every registered view.</param>
	internal ViewTracker(
		object owner,
		object syncRoot,
		StopGate<Task> stopGate,
		WorkspaceOptions options,
		EventHandler<ApplyRequestedEventArgs> onApplyRequested
	)
	{
		_owner = owner;
		_syncRoot = syncRoot;
		_stopGate = stopGate;
		_onApplyRequested = onApplyRequested;
		_viewsByDocument = new Dictionary<string, List<IWorkspaceDocumentView>>(options.PathComparison.Comparer);
	}

	/// <summary>Describes whether a view or a view id is already registered.</summary>
	internal enum Claim
	{
		/// <summary>Neither the view nor its id is registered.</summary>
		None,

		/// <summary>The view instance is registered.</summary>
		ViewRegistered,

		/// <summary>Another view holds the view id.</summary>
		ViewIdInUse,
	}

	/// <summary>Reports the result of one registration.</summary>
	internal readonly struct RegisterResult
	{
		private RegisterResult(ViewEntry? entry, bool closeDuplicateAttachment)
		{
			Entry = entry;
			CloseDuplicateAttachment = closeDuplicateAttachment;
		}

		/// <summary>The new entry, which is <see langword="null"/> exactly when the view or its id was already registered.</summary>
		internal ViewEntry? Entry { get; }

		/// <summary>Whether the view instance or its id was already registered.</summary>
		internal bool AlreadyRegistered => Entry is null;

		/// <summary>
		/// Whether a different view instance duplicated an existing view id, so the caller closes the new attachment. A second open
		/// of the same instance must not close the registration the first call created.
		/// </summary>
		internal bool CloseDuplicateAttachment { get; }

		/// <summary>Reports a view that was registered as <paramref name="entry"/>.</summary>
		internal static RegisterResult Created(ViewEntry entry) => new(entry, closeDuplicateAttachment: false);

		/// <summary>Reports a second registration of the same view instance, which leaves the first registration alone.</summary>
		internal static RegisterResult SameView() => new(entry: null, closeDuplicateAttachment: false);

		/// <summary>Reports a different view instance that reported an id another view holds.</summary>
		internal static RegisterResult DuplicateViewId() => new(entry: null, closeDuplicateAttachment: true);
	}

	/// <summary>Determines whether a view or a view id is registered, by the instance and the id the view reported.</summary>
	/// <param name="view">The view instance.</param>
	/// <param name="viewId">The id the view reported.</param>
	internal Claim FindClaim(IWorkspaceDocumentView view, string viewId)
	{
		lock (_syncRoot)
		{
			if (_registrations.ContainsKey(view))
				return Claim.ViewRegistered;

			return _viewsByViewId.ContainsKey(viewId) ? Claim.ViewIdInUse : Claim.None;
		}
	}

	/// <summary>Admits an attach that is about to open a view, unless the coordinator is stopping or the view is already registered.</summary>
	/// <param name="view">The view instance.</param>
	/// <returns><see langword="true"/> when the view is not registered and the attach may proceed; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="ObjectDisposedException">The coordinator began stopping.</exception>
	internal bool TryAdmitAttach(IWorkspaceDocumentView view)
	{
		lock (_syncRoot)
		{
			_stopGate.ThrowIfStopping(_owner);
			return !_registrations.ContainsKey(view);
		}
	}

	/// <summary>Registers a view that opened a document, unless the view or its id is already registered.</summary>
	/// <param name="view">The view instance.</param>
	/// <param name="viewId">The id the view reported.</param>
	/// <param name="snapshot">The snapshot of the document the view opened.</param>
	/// <exception cref="ObjectDisposedException">The coordinator began stopping.</exception>
	internal RegisterResult Register(IWorkspaceDocumentView view, string viewId, WorkspaceDocumentSnapshot snapshot)
	{
		lock (_syncRoot)
		{
			_stopGate.ThrowIfStopping(_owner);

			bool sameInstance = _registrations.ContainsKey(view);
			bool alreadyRegistered = sameInstance || _viewsByViewId.ContainsKey(viewId);

			if (alreadyRegistered)
				return sameInstance ? RegisterResult.SameView() : RegisterResult.DuplicateViewId();

			var registration = new ViewEntry(
				viewId,
				documentId: snapshot.DocumentId,
				documentKey: snapshot.DocumentKey,
				version: snapshot.Version,
				persistedVersion: snapshot.PersistedVersion,
				forwarder: new ApplyRequestedForwarder(_onApplyRequested)
			);

			_registrations.Add(view, registration);
			_viewsByViewId.Add(viewId, view);

			AddToDocument(snapshot.DocumentId, view);
			AttachDocument(snapshot);

			return RegisterResult.Created(registration);
		}
	}

	/// <summary>Determines whether the view instance is registered.</summary>
	/// <param name="view">The view instance.</param>
	internal bool Contains(IWorkspaceDocumentView view)
	{
		lock (_syncRoot)
			return _registrations.ContainsKey(view);
	}

	/// <summary>Determines whether the view is registered and the coordinator is not stopping.</summary>
	/// <param name="view">The view instance.</param>
	internal bool IsRegisteredAndNotStopping(IWorkspaceDocumentView view)
	{
		lock (_syncRoot)
			return !_stopGate.IsStopping && _registrations.ContainsKey(view);
	}

	/// <summary>Determines whether the live registration of a view is the given one.</summary>
	/// <param name="view">The view instance.</param>
	/// <param name="registration">The registration an attach created.</param>
	internal bool IsCurrent(IWorkspaceDocumentView view, ViewEntry registration)
	{
		lock (_syncRoot)
			return _registrations.TryGetValue(view, out ViewEntry? current) && ReferenceEquals(current, registration);
	}

	/// <summary>Removes a view's registration and returns it, or <see langword="null"/> when the view was not registered.</summary>
	/// <param name="view">The view instance.</param>
	/// <remarks>
	/// The event subscription is not touched here: view accessors must not run while the lock is held.
	/// </remarks>
	internal ViewEntry? Remove(IWorkspaceDocumentView view)
	{
		lock (_syncRoot)
			return RemoveLocked(view);
	}

	/// <summary>Removes a view's registration only while it is the given registration instance.</summary>
	/// <param name="view">The view instance.</param>
	/// <param name="registration">The registration an attach created.</param>
	/// <returns><see langword="true"/> when the registration was removed; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// A rollback therefore cannot detach a registration that a concurrent re-attach created after this attach's own was replaced.
	/// The event subscription is not touched here: view accessors must not run while the lock is held.
	/// </remarks>
	internal bool RemoveIfCurrent(IWorkspaceDocumentView view, ViewEntry registration)
	{
		lock (_syncRoot)
		{
			if (!_registrations.TryGetValue(view, out ViewEntry? current) || !ReferenceEquals(current, registration))
				return false;

			return RemoveLocked(view) is not null;
		}
	}

	/// <summary>Snapshots the registered views; view members are not read.</summary>
	internal IWorkspaceDocumentView[] GetRegisteredViews()
	{
		lock (_syncRoot)
			return [.. _registrations.Keys];
	}

	/// <summary>Reads the normalized document id a registered view is attached to.</summary>
	/// <param name="view">The view instance.</param>
	/// <returns>The document id, or <see langword="null"/> when the view is not registered.</returns>
	internal string? GetDocumentId(IWorkspaceDocumentView view)
	{
		lock (_syncRoot)
			return _registrations.TryGetValue(view, out ViewEntry? registration) ? registration.DocumentId : null;
	}

	/// <summary>Returns the views registered to a document, optionally without one of them.</summary>
	/// <param name="documentId">The normalized document id.</param>
	/// <param name="sourceView">The view to leave out, or <see langword="null"/> to return every view of the document.</param>
	internal IWorkspaceDocumentView[] GetPeers(string documentId, IWorkspaceDocumentView? sourceView)
	{
		lock (_syncRoot)
		{
			if (!_viewsByDocument.TryGetValue(documentId, out List<IWorkspaceDocumentView>? documentViews))
				return [];

			if (sourceView is null)
				return [.. documentViews];

			var peers = new IWorkspaceDocumentView[documentViews.Count];
			int count = 0;

			foreach (IWorkspaceDocumentView view in documentViews)
			{
				if (!ReferenceEquals(view, sourceView))
					peers[count++] = view;
			}

			if (count != peers.Length)
				Array.Resize(ref peers, count);

			return peers;
		}
	}

	/// <summary>Returns the views registered to the document with the given key.</summary>
	/// <param name="documentKey">The document key.</param>
	/// <remarks>
	/// The document key, not the document id, identifies the document: after a rename or save-as vacates an id, a concurrent open at that path
	/// registers a different document under the same id string, so peer matching by id would rekey the unrelated view.
	/// </remarks>
	internal IWorkspaceDocumentView[] GetPeersByDocumentKey(WorkspaceDocumentKey documentKey)
	{
		lock (_syncRoot)
		{
			List<IWorkspaceDocumentView>? peers = null;

			foreach ((IWorkspaceDocumentView view, ViewEntry registration) in _registrations)
			{
				if (registration.DocumentKey != documentKey)
					continue;

				(peers ??= []).Add(view);
			}

			return peers is null ? [] : [.. peers];
		}
	}

	/// <summary>Determines whether a view is marked unsynchronized.</summary>
	/// <param name="view">The view instance.</param>
	internal bool IsMarkedUnsynchronized(IWorkspaceDocumentView view)
	{
		lock (_syncRoot)
			return _registrations.TryGetValue(view, out ViewEntry? registration) && registration.IsUnsynchronized;
	}

	/// <summary>Reads whether a view is marked unsynchronized, with the last failure recorded for it.</summary>
	/// <param name="view">The view instance.</param>
	/// <param name="failure">Receives the last recorded failure when the view is marked unsynchronized.</param>
	/// <returns><see langword="true"/> when the view is registered and marked unsynchronized; otherwise, <see langword="false"/>.</returns>
	internal bool TryGetUnsynchronized(IWorkspaceDocumentView view, out WorkspaceOperationFailure? failure)
	{
		lock (_syncRoot)
		{
			if (_registrations.TryGetValue(view, out ViewEntry? registration) && registration.IsUnsynchronized)
			{
				failure = registration.LastFailure;
				return true;
			}

			failure = null;
			return false;
		}
	}

	/// <summary>Determines whether the snapshot a view last acknowledged is older than <paramref name="snapshot"/>.</summary>
	/// <param name="view">The view instance.</param>
	/// <param name="snapshot">The document snapshot to compare against.</param>
	/// <remarks>
	/// A commit moves only the persisted version, so a view at the current logical version is behind when its persisted version differs,
	/// and the refresh hands it the clean snapshot. A snapshot of an older logical version never counts, so a late refresh cannot move
	/// a view backwards.
	/// </remarks>
	internal bool IsBehind(IWorkspaceDocumentView view, WorkspaceDocumentSnapshot snapshot)
	{
		lock (_syncRoot)
		{
			return _registrations.TryGetValue(view, out ViewEntry? registration)
				&& (
					registration.Version < snapshot.Version
					|| (registration.Version == snapshot.Version && registration.PersistedVersion != snapshot.PersistedVersion)
				);
		}
	}

	/// <summary>Records the outcome of synchronizing a view.</summary>
	/// <param name="view">The view instance.</param>
	/// <param name="synchronized">Whether the view synchronized.</param>
	/// <param name="failure">The failure of an unsuccessful synchronization, or <see langword="null"/>.</param>
	/// <param name="snapshot">The snapshot the view synchronized to, or <see langword="null"/>.</param>
	/// <remarks>
	/// A detached view is not coordinated; recording state for it would leak until the coordinator stops.
	/// </remarks>
	internal void RecordResult(
		IWorkspaceDocumentView view,
		bool synchronized,
		WorkspaceOperationFailure? failure,
		WorkspaceDocumentSnapshot? snapshot
	)
	{
		lock (_syncRoot)
		{
			if (!_registrations.TryGetValue(view, out ViewEntry? registration))
				return;

			if (synchronized)
			{
				registration.IsUnsynchronized = false;
				registration.LastFailure = null;

				// The recorded version only advances: two interleaved refreshes can deliver an older
				// snapshot after a newer one, and moving the version backwards would make a later
				// IsBehind check report a current view as behind, so it is refreshed again.
				if (snapshot is not null && snapshot.Version >= registration.Version)
				{
					registration.Version = snapshot.Version;
					registration.PersistedVersion = snapshot.PersistedVersion;
				}
			}
			else
			{
				registration.IsUnsynchronized = true;

				// The recorded failure describes the current synchronization problem; a later failure
				// without detail keeps the last known reason instead of discarding it.
				if (failure is not null)
					registration.LastFailure = failure;
			}
		}
	}

	/// <summary>Marks a view unsynchronized and keeps the failure that explains it.</summary>
	/// <param name="view">The view instance.</param>
	/// <param name="failure">The failure, or <see langword="null"/> to keep the last known one.</param>
	/// <remarks>
	/// Detached views are not coordinated; tracking them would retain them until stop.
	/// </remarks>
	internal void RecordFailure(IWorkspaceDocumentView view, WorkspaceOperationFailure? failure = null)
	{
		lock (_syncRoot)
		{
			if (!_registrations.TryGetValue(view, out ViewEntry? registration))
				return;

			registration.IsUnsynchronized = true;

			// Keep the last known failure detail when a later failure has none.
			if (failure is not null)
				registration.LastFailure = failure;
		}
	}

	/// <summary>Moves a view's registration to the identity of the snapshot it acknowledged and clears its unsynchronized state.</summary>
	/// <param name="view">The view instance.</param>
	/// <param name="snapshot">The snapshot the view acknowledged.</param>
	/// <remarks>
	/// A successful identity acknowledgment makes the view current, exactly like a successful refresh. The previous document id is read
	/// from the registration itself, so a stale caller-supplied id cannot leave the view in the vacated list or duplicate it in the new one.
	/// </remarks>
	internal void UpdateIdentity(IWorkspaceDocumentView view, WorkspaceDocumentSnapshot snapshot)
	{
		lock (_syncRoot)
		{
			if (!_registrations.TryGetValue(view, out ViewEntry? registration))
				return;

			string oldDocumentId = registration.DocumentId;
			WorkspaceDocumentKey oldDocumentKey = registration.DocumentKey;

			registration.DocumentId = snapshot.DocumentId;
			registration.DocumentKey = snapshot.DocumentKey;
			registration.Version = snapshot.Version;
			registration.PersistedVersion = snapshot.PersistedVersion;
			registration.IsUnsynchronized = false;
			registration.LastFailure = null;

			// A view that attached while a move ran attached its document under the old id, so its acknowledgment moves the attached document too. A
			// registration that changes its document key counts against the new key.
			if (oldDocumentKey != snapshot.DocumentKey)
			{
				DetachDocument(oldDocumentKey);
				AttachDocument(snapshot);
			}
			else if (_attachedDocuments.TryGetValue(snapshot.DocumentKey, out AttachedDocumentEntry? document))
			{
				Observe(document, snapshot);
			}

			if (string.Equals(oldDocumentId, snapshot.DocumentId, StringComparison.Ordinal))
				return;

			RemoveFromDocument(oldDocumentId, view);
			AddToDocument(snapshot.DocumentId, view);
		}
	}

	/// <summary>
	/// Removes every registration and returns them, so the stop can release the views, and queues the close of every attached document.
	/// </summary>
	/// <remarks>
	/// Every collection that tracks a view is released: leaving any of them populated would retain every view after the teardown and leave
	/// the stopped state inconsistent.
	/// </remarks>
	internal KeyValuePair<IWorkspaceDocumentView, ViewEntry>[] Drain()
	{
		lock (_syncRoot)
		{
			KeyValuePair<IWorkspaceDocumentView, ViewEntry>[] registered = [.. _registrations];

			_registrations.Clear();
			_viewsByDocument.Clear();
			_viewsByViewId.Clear();

			foreach (AttachedDocumentEntry document in _attachedDocuments.Values)
			{
				_pendingChanges.Enqueue(
					new AttachedDocumentChangedEventArgs(AttachedDocumentChangeKind.Closed, document.Snapshot, oldDocumentId: null)
				);
			}

			_attachedDocuments.Clear();

			return registered;
		}
	}

	/// <summary>Returns the newest snapshot the tracker holds of each document that has at least one registered view.</summary>
	internal WorkspaceDocumentSnapshot[] GetAttachedDocuments()
	{
		lock (_syncRoot)
			return [.. _attachedDocuments.Values.Select(document => document.Snapshot)];
	}

	/// <summary>Moves an attached document to a snapshot an operation produced, and queues its content change or its rename.</summary>
	/// <param name="snapshot">A snapshot the store returned, or <see langword="null"/> when the operation returned none.</param>
	/// <remarks>
	/// A snapshot of a document without a registered view, or one that is not newer than the tracked one, changes nothing.
	/// </remarks>
	internal void ObserveSnapshot(WorkspaceDocumentSnapshot? snapshot)
	{
		if (snapshot is null)
			return;

		lock (_syncRoot)
		{
			if (_attachedDocuments.TryGetValue(snapshot.DocumentKey, out AttachedDocumentEntry? document))
				Observe(document, snapshot);
		}
	}

	/// <summary>Determines whether a change waits to be raised.</summary>
	internal bool HasPendingChanges
	{
		get
		{
			lock (_syncRoot)
				return _pendingChanges.Count > 0;
		}
	}

	/// <summary>Takes the oldest change that waits to be raised.</summary>
	/// <param name="change">Receives the change, or <see langword="null"/> when none waits.</param>
	internal bool TryTakeChange([NotNullWhen(true)] out AttachedDocumentChangedEventArgs? change)
	{
		lock (_syncRoot)
			return _pendingChanges.TryDequeue(out change);
	}

	private void AttachDocument(WorkspaceDocumentSnapshot snapshot)
	{
		if (_attachedDocuments.TryGetValue(snapshot.DocumentKey, out AttachedDocumentEntry? document))
		{
			document.ViewCount++;
			Observe(document, snapshot);
			return;
		}

		_attachedDocuments.Add(snapshot.DocumentKey, new AttachedDocumentEntry(snapshot));
		_pendingChanges.Enqueue(new AttachedDocumentChangedEventArgs(AttachedDocumentChangeKind.Opened, snapshot, oldDocumentId: null));
	}

	private void DetachDocument(WorkspaceDocumentKey documentKey)
	{
		if (!_attachedDocuments.TryGetValue(documentKey, out AttachedDocumentEntry? document) || --document.ViewCount > 0)
			return;

		_attachedDocuments.Remove(documentKey);
		_pendingChanges.Enqueue(new AttachedDocumentChangedEventArgs(AttachedDocumentChangeKind.Closed, document.Snapshot, oldDocumentId: null));
	}

	// The store raises the version of a document on every change of its text or its id, so the newest version always carries both.
	private void Observe(AttachedDocumentEntry document, WorkspaceDocumentSnapshot snapshot)
	{
		WorkspaceDocumentSnapshot previous = document.Snapshot;

		if (snapshot.Version <= previous.Version)
			return;

		document.Snapshot = snapshot;

		if (!string.Equals(previous.DocumentId, snapshot.DocumentId, StringComparison.Ordinal))
			_pendingChanges.Enqueue(new AttachedDocumentChangedEventArgs(AttachedDocumentChangeKind.Renamed, snapshot, previous.DocumentId));
		else if (!string.Equals(previous.Content, snapshot.Content, StringComparison.Ordinal))
			_pendingChanges.Enqueue(new AttachedDocumentChangedEventArgs(AttachedDocumentChangeKind.ContentChanged, snapshot, oldDocumentId: null));
	}

	private ViewEntry? RemoveLocked(IWorkspaceDocumentView view)
	{
		if (!_registrations.Remove(view, out ViewEntry? registration))
			return null;

		_viewsByViewId.Remove(registration.ViewId);
		RemoveFromDocument(registration.DocumentId, view);
		DetachDocument(registration.DocumentKey);

		return registration;
	}

	private void AddToDocument(string documentId, IWorkspaceDocumentView view)
	{
		if (!_viewsByDocument.TryGetValue(documentId, out List<IWorkspaceDocumentView>? documentViews))
		{
			documentViews = [];
			_viewsByDocument.Add(documentId, documentViews);
		}

		documentViews.Add(view);
	}

	private void RemoveFromDocument(string documentId, IWorkspaceDocumentView view)
	{
		if (!_viewsByDocument.TryGetValue(documentId, out List<IWorkspaceDocumentView>? documentViews))
			return;

		// Removes by identity: Equals is a view member, and the lock must not invoke one.
		documentViews.RemoveAll(listed => ReferenceEquals(listed, view));

		if (documentViews.Count == 0)
			_viewsByDocument.Remove(documentId);
	}
}

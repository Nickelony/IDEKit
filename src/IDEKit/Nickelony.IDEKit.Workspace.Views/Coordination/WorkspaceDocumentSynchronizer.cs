using Nickelony.IDEKit.LanguageFeatures.Synchronization;
using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views.Coordination;

/// <summary>Mirrors the documents that have an attached view to a synchronization target, such as a language server.</summary>
/// <remarks>
/// <para>
/// A document opens on the target when its first view attaches, updates on each change of its text, renames when its id changes, and closes when its last
/// view detaches or a delete or a stop closes it, so the target holds one copy however many views show the document. A rename from an included id
/// to an excluded one closes the document, and the reverse opens it.
/// </para>
/// <para>
/// The synchronizer handles <see cref="IWorkspaceViewCoordinator.AttachedDocumentChanged"/>, which the coordinator raises on its dispatcher,
/// so create and dispose it on the dispatcher thread.
/// </para>
/// </remarks>
public sealed class WorkspaceDocumentSynchronizer : IDisposable
{
	private readonly IWorkspaceViewCoordinator _coordinator;
	private readonly IDocumentSynchronizationTarget _target;
	private readonly Func<string, bool>? _includes;

	// The document key survives a rename, so a late change of a renamed document still finds the copy it belongs to.
	private readonly Dictionary<WorkspaceDocumentKey, (string DocumentId, long Version)> _opened = [];

	private bool _isDisposed;

	/// <param name="coordinator">The coordinator whose attached documents the target mirrors.</param>
	/// <param name="target">The target that receives the documents.</param>
	/// <param name="includes">Decides by document id whether the target takes a document, or <see langword="null"/> to take every document.</param>
	/// <exception cref="ArgumentNullException"><paramref name="coordinator"/> or <paramref name="target"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// The documents that already have an attached view open on the target before the constructor returns.
	/// </remarks>
	public WorkspaceDocumentSynchronizer(
		IWorkspaceViewCoordinator coordinator,
		IDocumentSynchronizationTarget target,
		Func<string, bool>? includes = null
	)
	{
		ArgumentNullException.ThrowIfNull(coordinator);
		ArgumentNullException.ThrowIfNull(target);

		_coordinator = coordinator;
		_target = target;
		_includes = includes;

		coordinator.AttachedDocumentChanged += HandleAttachedDocumentChanged;

		// The coordinator's own snapshots carry the document keys, so an id that a rename vacated and an open reused cannot open the wrong document. An
		// open the coordinator has not raised yet finds its document open already, and a change it has not raised yet is no newer than the snapshot.
		foreach (WorkspaceDocumentSnapshot snapshot in coordinator.AttachedDocuments)
			Open(snapshot);
	}

	/// <summary>Unsubscribes from the coordinator and closes every document the synchronizer opened on the target.</summary>
	public void Dispose()
	{
		if (_isDisposed)
			return;

		_isDisposed = true;
		_coordinator.AttachedDocumentChanged -= HandleAttachedDocumentChanged;

		foreach ((string documentId, _) in _opened.Values)
			_target.CloseDocument(documentId);

		_opened.Clear();
	}

	private void HandleAttachedDocumentChanged(object? sender, AttachedDocumentChangedEventArgs eventArgs)
	{
		if (_isDisposed)
			return;

		switch (eventArgs.Kind)
		{
			case AttachedDocumentChangeKind.Opened:
				Open(eventArgs.Snapshot);
				break;

			case AttachedDocumentChangeKind.ContentChanged:
				Update(eventArgs.Snapshot);
				break;

			case AttachedDocumentChangeKind.Renamed:
				Rename(eventArgs.Snapshot);
				break;

			case AttachedDocumentChangeKind.Closed:
				Close(eventArgs.Snapshot.DocumentKey);
				break;
		}
	}

	private bool Includes(string documentId) => _includes?.Invoke(documentId) ?? true;

	private void Open(WorkspaceDocumentSnapshot snapshot)
	{
		if (_opened.ContainsKey(snapshot.DocumentKey) || !Includes(snapshot.DocumentId))
			return;

		_target.OpenDocument(snapshot.DocumentId, snapshot.Content);
		_opened.Add(snapshot.DocumentKey, (snapshot.DocumentId, snapshot.Version));
	}

	// A change no newer than the text the target holds arrived after the constructor read a newer snapshot, so it is skipped.
	private void Update(WorkspaceDocumentSnapshot snapshot)
	{
		if (!_opened.TryGetValue(snapshot.DocumentKey, out (string DocumentId, long Version) opened) || snapshot.Version <= opened.Version)
			return;

		_target.UpdateDocument(opened.DocumentId, snapshot.Content);
		_opened[snapshot.DocumentKey] = (opened.DocumentId, snapshot.Version);
	}

	private void Rename(WorkspaceDocumentSnapshot snapshot)
	{
		if (!_opened.TryGetValue(snapshot.DocumentKey, out (string DocumentId, long Version) opened))
		{
			Open(snapshot);
			return;
		}

		if (snapshot.Version <= opened.Version)
			return;

		if (Includes(snapshot.DocumentId))
		{
			_target.RenameDocument(opened.DocumentId, snapshot.DocumentId, snapshot.Content);
			_opened[snapshot.DocumentKey] = (snapshot.DocumentId, snapshot.Version);
		}
		else
		{
			_opened.Remove(snapshot.DocumentKey);
			_target.CloseDocument(opened.DocumentId);
		}
	}

	private void Close(WorkspaceDocumentKey documentKey)
	{
		if (_opened.Remove(documentKey, out (string DocumentId, long Version) opened))
			_target.CloseDocument(opened.DocumentId);
	}
}

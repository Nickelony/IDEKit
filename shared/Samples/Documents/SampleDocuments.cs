using System.IO;
using Nickelony.IDEKit.Core.Threading;
using Nickelony.IDEKit.FileWatching;
using Nickelony.IDEKit.LanguageFeatures.Editing;
using Nickelony.IDEKit.LanguageFeatures.Synchronization;
using Nickelony.IDEKit.Workspace;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.FileSystem;
using Nickelony.IDEKit.Workspace.Reloading;
using Nickelony.IDEKit.Workspace.Replacement;
using Nickelony.IDEKit.Workspace.Views;
using Nickelony.IDEKit.Workspace.Views.Coordination;

namespace Nickelony.Samples;

/// <summary>
/// Keeps the document authority of the sample: it opens, saves, reloads, edits, moves and deletes the files
/// through a <see cref="WorkspaceViewCoordinator"/> over a <see cref="WorkspaceDocumentStore"/>.
/// </summary>
/// <remarks>
/// The coordinator keeps every view of a document in step with the store, so a reload, a language-server edit or the edit of another view
/// reaches an editor through its <see cref="IWorkspaceDocumentView"/>. <see cref="Reloader"/> watches the folder and calls back into this type
/// for each reload and conflict, and a deletion moves the file into the trash folder of <see cref="SampleTrashFileSystem"/>.
/// </remarks>
internal sealed class SampleDocuments : IAsyncDisposable
{
	// One options value serves the store, the file system and the coordinator, so the path comparison cannot drift between them.

	private readonly WorkspaceDocumentStore _store;
	private readonly WorkspaceViewCoordinator _coordinator;
	private readonly WorkspaceEditApplier _edits;
	private readonly Action<Action> _runOnUiThread;

	/// <param name="trashRoot">The folder that receives the deleted files and folders.</param>
	/// <param name="dispatcher">Runs the actions that touch an editor on the UI thread, where the coordinator reaches its views.</param>
	/// <param name="runOnUiThread">Runs an action on the UI thread, where the callers of the events touch the editors.</param>
	/// <param name="watcherFactory">Creates the file watcher of <see cref="Reloader"/>, or <see langword="null"/> for the default watcher.</param>
	internal SampleDocuments(
		string trashRoot,
		IUiDispatcher dispatcher,
		Action<Action> runOnUiThread,
		FileWatcherFactory? watcherFactory = null
	)
	{
		_store = new WorkspaceDocumentStore(new SampleTrashFileSystem(new LocalWorkspaceFileSystem(Options), trashRoot), Options);
		_coordinator = new WorkspaceViewCoordinator(_store, dispatcher);
		_coordinator.AttachedDocumentChanged += (_, _) => DocumentChanged?.Invoke(this, EventArgs.Empty);

		// An edit that names a missing file is an issue, so a save never brings the file into existence.
		_edits = new WorkspaceEditApplier(
			_coordinator,
			new WorkspaceEditApplierOptions { OpenOptions = SampleEncodings.OpenOptions with { CreateIfMissing = false } }
		);

		_runOnUiThread = runOnUiThread;

		// A conflict means the editor holds unsaved text that the changed file contradicts: the sample writes the editor text over the file.
		FileReloadHooks<WorkspaceDocumentConflictResolutionMode?> hooks = WorkspaceViewReloadHooksFactory.Create(
			coordinator: _coordinator,
			dispatcher,

			decideConflict: (_, _) =>
				Task.FromResult<WorkspaceDocumentConflictResolutionMode?>(WorkspaceDocumentConflictResolutionMode.ResolveWithLogical),

			reportFailure: (result, _) => ReportAsync($"Reload failed: {result.Outcome}")
		);

		Reloader = new SampleFileReloader(_store, ObserveReloads(hooks), ReportAsync, watcherFactory);
		Reloader.FilesChanged += (_, _) => _runOnUiThread(() => FilesChanged?.Invoke(this, EventArgs.Empty));
	}

	/// <summary>Watches the files and reloads the documents whose files change on disk.</summary>
	internal SampleFileReloader Reloader { get; }

	/// <summary>The comparison that decides whether two paths name one document.</summary>
	internal static StringComparer PathComparer => Options.PathComparison.Comparer;

	/// <summary>The options that the store, the file system and the file list of the quick pick share.</summary>
	internal static WorkspaceOptions Options { get; } = WorkspaceOptions.Default;

	/// <summary>
	/// Raised on the UI thread after the text, the identity or the views of a document changed, for example when a view published its text.
	/// </summary>
	internal event EventHandler? DocumentChanged;

	/// <summary>Raised on the UI thread with the path and the text of a file whose change on disk replaced the editor text.</summary>
	internal event EventHandler<DiskTextAppliedEventArgs>? DiskTextApplied;

	/// <summary>Raised on the UI thread with the path and the text of a file that the editor text replaced on disk.</summary>
	internal event EventHandler<EditorTextWrittenEventArgs>? EditorTextWritten;

	/// <summary>Raised on the UI thread with a one-line message about a change on disk that the sample handled.</summary>
	internal event EventHandler<StatusChangedEventArgs>? StatusChanged;

	/// <summary>Raised on the UI thread after files changed, appeared or disappeared in the watched folder.</summary>
	internal event EventHandler? FilesChanged;

	/// <summary>Opens a file in the store and returns its text; a missing file opens as a new, empty document that a save creates.</summary>
	/// <param name="path">The file to open.</param>
	/// <param name="encoding">
	/// The one encoding to read the file with, or <see langword="null"/> to try the candidates of <see cref="SampleEncodings"/>;
	/// a file that the store tracks already keeps the encoding it was read with.
	/// </param>
	/// <exception cref="IOException">
	/// The store could not open the file; the message starts with the outcome and the failure code, then names the path.
	/// </exception>
	internal async Task<string> OpenAsync(string path, TextEncoding? encoding = null)
	{
		WorkspaceDocumentOpenOptions options = encoding is { } only
			? SampleEncodings.OpenOptions with
			{
				NoBomEncodings = [only],
			}
			: SampleEncodings.OpenOptions;

		WorkspaceViewCoordinatorOpenResult result = await _coordinator.OpenAsync(path, options);

		if (result.Snapshot is { } snapshot)
			return snapshot.Content;

		string? code = (result.Failure ?? result.StoreResult?.Failure)?.Code;

		// The outcome and the code come first, because a long path would push them out of the status line.
		throw new IOException($"{result.Outcome}{(code is null ? string.Empty : $" ({code})")}: the store could not open {path}.");
	}

	/// <summary>
	/// Attaches a view of an editor to the document of a file, so the coordinator keeps the two in step; one document takes any number of views.
	/// </summary>
	/// <remarks>
	/// Call it on the UI thread.
	/// </remarks>
	internal async Task AttachAsync(string path, IWorkspaceDocumentView view)
	{
		WorkspaceViewCoordinatorOpenResult result = await _coordinator.OpenWithViewAsync(path, SampleEncodings.OpenOptions, view);

		if (result.Outcome is not (WorkspaceViewCoordinatorOpenOutcome.Opened or WorkspaceViewCoordinatorOpenOutcome.AlreadyOpen))
			throw new IOException($"The view of {path} could not be attached: {result.Outcome}.");
	}

	/// <summary>Detaches a view, which leaves the document in the store, so a tab opened again shows the same text.</summary>
	internal void Detach(IWorkspaceDocumentView view)
	{
		_coordinator.DetachView(view);
		view.Close();
	}

	/// <summary>Mirrors each document that a view shows to a language server, once however many views show it.</summary>
	/// <param name="server">The language server that receives the documents.</param>
	/// <param name="includes">Decides by path whether the server takes a document.</param>
	/// <remarks>
	/// Call it on the UI thread, and dispose the result before the server.
	/// </remarks>
	internal WorkspaceDocumentSynchronizer SynchronizeServer(IDocumentSynchronizationTarget server, Func<string, bool> includes) =>
		new(_coordinator, server, includes);

	/// <summary>Whether the store has unsaved changes of a file.</summary>
	internal bool IsDirty(string path) => _store.TryGetSnapshot(path, out WorkspaceDocumentSnapshot? snapshot) && snapshot.IsDirty;

	/// <summary>Has every view of a file publish the text it holds back, so the store holds the text the user sees.</summary>
	internal async Task<bool> PublishAsync(string path) =>
		(await _coordinator.PublishPendingEditsAsync(path)).ViewSynchronization.Outcome == WorkspaceDocumentViewSynchronizationOutcome.Synchronized;

	/// <summary>
	/// Makes the editor text the logical content of the file and writes it, and returns the text it wrote,
	/// or <see langword="null"/> when the write failed.
	/// </summary>
	/// <remarks>
	/// Call it on the UI thread. The written text can differ from what an editor showed before the call, because the views merge another view's
	/// change when they publish.
	/// </remarks>
	internal async Task<string?> SaveAsync(string path)
	{
		WorkspaceDocumentCommitResult? committed = await CommitAsync(path);
		return committed is { Outcome: WorkspaceDocumentCommitOutcome.Committed, Snapshot: { } written } ? written.Content : null;
	}

	/// <summary>Makes the editor text the logical content of the file and writes it, in another encoding when the format names one.</summary>
	/// <param name="path">The file to write.</param>
	/// <param name="format">The encoding and the byte-order mark to write with, or <see langword="null"/> to keep those of the document.</param>
	/// <returns>
	/// The commit result, or <see langword="null"/> when a view held back its text, the document is not tracked or the store refused the format.
	/// </returns>
	/// <remarks>
	/// Call it on the UI thread. A format reaches the document through a replacement, so a write that fails, for example because the text holds
	/// a character the encoding cannot represent, leaves the document with the format it had, and the file as it was.
	/// </remarks>
	internal async Task<WorkspaceDocumentCommitResult?> CommitAsync(string path, TextFileFormat? format = null)
	{
		WorkspaceViewCoordinatorPublicationResult published = await _coordinator.PublishPendingEditsAsync(path);

		if (published.Snapshot is not { } current)
			return null;

		TextFileFormat previous = current.FileFormat;

		if (format is { } chosen)
		{
			var target = previous with { Encoding = chosen.Encoding, HasBom = chosen.HasBom };

			WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentMutationResult> replaced = await _coordinator.ReplaceAsync(
				new WorkspaceDocumentReplaceRequest(current.Identity, current.Content, target)
			);

			if (replaced.StoreResult is not { Applied: true, Snapshot: { } changed })
				return null;

			current = changed;
		}

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentCommitResult> committed = await _coordinator.CommitAsync(
			new WorkspaceDocumentCommitRequest(current.Identity, current.OnDiskStamp)
		);

		if (
			format is not null
			&& committed.StoreResult?.Outcome != WorkspaceDocumentCommitOutcome.Committed
			&& (committed.StoreResult?.Snapshot ?? committed.BlockedSnapshot) is { } kept
		)
		{
			await _coordinator.ReplaceAsync(new WorkspaceDocumentReplaceRequest(kept.Identity, kept.Content, previous));
		}

		return committed.StoreResult;
	}

	/// <summary>Reads a file again with an encoding, in every view of it, after the views published the text they hold.</summary>
	/// <returns>The reload result, or <see langword="null"/> when a view held back its text or the document is not tracked.</returns>
	/// <remarks>
	/// Call it on the UI thread. The store reports <see cref="WorkspaceDocumentReloadOutcome.UnsavedChanges"/> for a document with unsaved changes,
	/// which the caller settles by saving or reverting before it asks again.
	/// </remarks>
	internal async Task<WorkspaceDocumentReloadResult?> ReopenAsync(string path, TextEncoding encoding)
	{
		WorkspaceViewCoordinatorPublicationResult published = await _coordinator.PublishPendingEditsAsync(path);

		if (published.Snapshot is not { } current)
			return null;

		return (await _coordinator.ReloadAsync(new WorkspaceDocumentReloadRequest(current.Identity, encoding))).StoreResult;
	}

	/// <summary>Reads the format of a tracked document: its encoding, byte-order mark and line terminator style.</summary>
	/// <returns>Whether the store tracks the file.</returns>
	internal bool TryGetFormat(string path, out TextFileFormat format)
	{
		bool tracked = _store.TryGetSnapshot(path, out WorkspaceDocumentSnapshot? snapshot);
		format = tracked ? snapshot!.FileFormat : default;

		return tracked;
	}

	/// <summary>Replaces the unsaved text of a file with the text on disk, in every view of it.</summary>
	internal async Task<bool> RevertAsync(string path)
	{
		if (!_store.TryGetSnapshot(path, out WorkspaceDocumentSnapshot? current))
			return false;

		WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentMutationResult> discarded = await _coordinator.DiscardAsync(
			new WorkspaceDocumentDiscardRequest(current.Identity)
		);

		return discarded.StoreResult.Applied;
	}

	/// <summary>Applies the edits of a language server to the documents they name and keeps every view in step.</summary>
	/// <param name="edit">The edits of the language server.</param>
	/// <param name="isShown">Tells whether a tab shows the document of a path, which keeps its edited text unsaved.</param>
	/// <returns>The number of documents the edit changed.</returns>
	/// <exception cref="InvalidOperationException">The edit cannot be applied as asked, or the store refused a replacement.</exception>
	/// <remarks>
	/// The applier opens a document that no tab shows, and the sample saves
	/// each one the edit changed, so the edit does not leave a change nobody can see. An edit with a plan issue, such as an invalid range or a view
	/// that did not publish its text, changes nothing; a replacement that fails keeps the documents replaced before it.
	/// </remarks>
	internal async Task<int> ApplyEditAsync(WorkspaceEdit edit, Func<string, bool> isShown)
	{
		WorkspaceEditApplyResult applied = await _edits.ApplyAsync(edit);

		// The replacement result is absent exactly when the plan has an issue.
		if (applied.Replacement is not { } replaced)
		{
			WorkspaceEditPlanIssue issue = applied.Plan.Issues[0];
			throw new InvalidOperationException($"The edit cannot be applied: {issue.Kind} in {Path.GetFileName(issue.DocumentId)}.");
		}

		foreach (string path in replaced.ChangedDocumentIds.Where(path => !isShown(path)))
			await SaveAsync(path);

		if (replaced.Outcome != DocumentReplacementOutcome.Completed)
		{
			throw new InvalidOperationException(
				$"The edit was applied to {replaced.ChangedDocumentIds.Count} document(s) only: {replaced.Failure?.Message}"
			);
		}

		return replaced.ChangedDocumentIds.Count;
	}

	/// <summary>
	/// Renames a file's document, and every view of it follows; a file the store does not track yet is opened first, because the store
	/// renames documents.
	/// </summary>
	internal async Task<string> RenameFileAsync(string path, string newPath)
	{
		await OpenAsync(path);

		if (!_store.TryGetSnapshot(path, out WorkspaceDocumentSnapshot? current))
			return "DocumentNotFound";

		WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentRenameResult> renamed = await _coordinator.RenameAsync(
			new WorkspaceDocumentRenameRequest(current.Identity, current.OnDiskStamp, newPath)
		);

		return renamed.StoreResult.Outcome.ToString();
	}

	/// <summary>Renames a folder, and every view of a document inside it follows.</summary>
	internal async Task<string> RenameFolderAsync(string path, string newPath)
	{
		WorkspaceViewCoordinatorDirectoryResult<WorkspaceDocumentDirectoryRenameResult> renamed = await _coordinator.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(path, newPath)
		);

		return renamed.StoreResult?.Outcome.ToString() ?? $"Blocked by {Describe(renamed.ViewSynchronization)}";
	}

	/// <summary>
	/// Moves a file into the trash; a view with unpublished edits blocks it, and the coordinator closes the views of a deleted file.
	/// </summary>
	internal async Task<string> DeleteFileAsync(string path)
	{
		await OpenAsync(path);

		if (!_store.TryGetSnapshot(path, out WorkspaceDocumentSnapshot? current))
			return "DocumentNotFound";

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentDeleteResult> deleted = await _coordinator.DeleteAsync(
			new WorkspaceDocumentDeleteRequest(current.Identity, current.OnDiskStamp)
		);

		return deleted.StoreResult?.Outcome.ToString() ?? $"Blocked by {Describe(deleted.ViewSynchronization)}";
	}

	/// <summary>Moves a folder into the trash; a view with unpublished edits inside it blocks it.</summary>
	internal async Task<string> DeleteFolderAsync(string path)
	{
		WorkspaceViewCoordinatorDirectoryResult<WorkspaceDocumentDirectoryDeleteResult> deleted = await _coordinator.DeleteDirectoryAsync(
			new WorkspaceDocumentDirectoryDeleteRequest(path)
		);

		return deleted.StoreResult?.Outcome.ToString() ?? $"Blocked by {Describe(deleted.ViewSynchronization)}";
	}

	public async ValueTask DisposeAsync()
	{
		// The reloads stop first, so no pass reaches a coordinator that is already disposed.
		await Reloader.DisposeAsync();
		await _coordinator.DisposeAsync();
		await _store.DisposeAsync();
	}

	private static string Describe(WorkspaceDocumentViewSynchronizationResult synchronization) =>
		string.Join(", ", synchronization.Issues.Select(issue => issue.ViewId));

	// The library hooks reload and resolve through the coordinator; the sample also tells the editors when the disk text replaced theirs and
	// when their text replaced the file, so the change markers follow.
	private FileReloadHooks<WorkspaceDocumentConflictResolutionMode?> ObserveReloads(FileReloadHooks<WorkspaceDocumentConflictResolutionMode?> hooks)
	{
		Func<
			WorkspaceDocumentReloadResult,
			WorkspaceDocumentConflictResolutionMode?,
			CancellationToken,
			Task<WorkspaceDocumentConflictResolutionResult?>
		>? resolve = hooks.ResolveConflict;

		return new FileReloadHooks<WorkspaceDocumentConflictResolutionMode?>
		{
			ReloadDocument = async (path, cancellationToken) =>
			{
				WorkspaceDocumentReloadResult result = await hooks.ReloadDocument(path, cancellationToken).ConfigureAwait(false);

				if (result is { Outcome: WorkspaceDocumentReloadOutcome.Reloaded, Snapshot: { } reloaded })
				{
					string text = reloaded.Content;
					_runOnUiThread(() => DiskTextApplied?.Invoke(this, new DiskTextAppliedEventArgs(path, text)));
				}

				return result;
			},

			DecideReload = hooks.DecideReload,

			ResolveConflict = resolve is null
				? null
				: async (conflict, mode, cancellationToken) =>
				{
					WorkspaceDocumentConflictResolutionResult? resolved = await resolve(conflict, mode, cancellationToken).ConfigureAwait(false);
					string name = Path.GetFileName(conflict.RequestedIdentity.DocumentId);

					await ReportAsync(
						$"{name} changed on disk while it had unsaved edits; the editor text replaced the file: {resolved?.Outcome.ToString() ?? "blocked"}"
					);

					if (resolved is { Outcome: WorkspaceDocumentConflictResolutionOutcome.ResolvedWithLogical, Snapshot: { } written })
					{
						string text = written.Content;
						_runOnUiThread(() => EditorTextWritten?.Invoke(this, new EditorTextWrittenEventArgs(written.DocumentId, text)));
					}

					return resolved;
				},

			ReportReloadFailure = hooks.ReportReloadFailure,
		};
	}

	private Task ReportAsync(string message)
	{
		_runOnUiThread(() => StatusChanged?.Invoke(this, new StatusChangedEventArgs(message)));
		return Task.CompletedTask;
	}
}

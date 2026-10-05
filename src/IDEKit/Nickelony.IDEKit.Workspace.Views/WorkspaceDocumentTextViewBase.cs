using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.Core.Threading;
using Nickelony.IDEKit.Core.Validation;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Views;

/// <summary>
/// Keeps the text of one editor in step with a workspace document, so a host implements only reading, replacing and locking the text.
/// </summary>
/// <remarks>
/// The editor holds pending edits while its text differs from the last snapshot the view acknowledged, and a derived
/// view calls <see cref="HandleTextChanged"/> after each change of that text. A publication carries the line terminator style of its text,
/// so converting the terminators of the editor needs no call of the host, and a text without a line terminator keeps the style the document recorded.
/// The coordinator calls every member through its dispatcher. A <see cref="ReplaceText"/> or <see cref="IsReadOnly"/> that throws turns
/// into the failed or unavailable outcome of the call, with the exception attached; a <see cref="ReadText"/> that throws propagates to the caller.
/// </remarks>
public abstract class WorkspaceDocumentTextViewBase : IWorkspaceDocumentDeleteLeaseView, IWorkspaceDocumentPublishingView, IDisposable
{
	private const string NoDocumentMessage = "The view has no open document.";

	private readonly DispatcherDebouncer? _publishDebouncer;
	private readonly bool _publishesOnEachChange;
	private readonly PublicationGate _publication = new();
	private readonly DeleteLeaseCounter _leases;

	private WorkspaceDocumentSnapshot? _synchronized;
	private bool _wasDirty;

	// The text the view synchronized before the conflicting change, against which the typed change is measured until the conflict ends.
	private string? _conflictBaseText;

	private bool _isReplacing;
	private bool _isDisposed;

	/// <param name="viewId">The stable identifier of the view, which the coordinator reports in its results.</param>
	/// <param name="dispatcher">The dispatcher of the thread that owns the editor, which runs a debounced publication.</param>
	/// <param name="publishDelay">
	/// How long the text must stay unchanged before the view publishes it; <see cref="TimeSpan.Zero"/> publishes on each change,
	/// and <see langword="null"/> publishes only when the coordinator asks through <see cref="PublishPendingEdits"/>.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="viewId"/> or <paramref name="dispatcher"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="publishDelay"/> is negative.</exception>
	protected WorkspaceDocumentTextViewBase(string viewId, IUiDispatcher dispatcher, TimeSpan? publishDelay)
	{
		ArgumentNullException.ThrowIfNull(viewId);
		ArgumentNullException.ThrowIfNull(dispatcher);

		ViewId = viewId;
		_leases = new DeleteLeaseCounter(() => IsReadOnly, value => IsReadOnly = value);

		if (publishDelay is not { } delay)
			return;

		if (NumericValidation.NonNegative(delay, nameof(publishDelay)) == TimeSpan.Zero)
			_publishesOnEachChange = true;
		else
			_publishDebouncer = new DispatcherDebouncer(delay, dispatcher);
	}

	/// <inheritdoc/>
	public event EventHandler<ApplyRequestedEventArgs>? ApplyRequested;

	/// <summary>Raised after <see cref="IsDirty"/> changed, so a tab can show whether the document has unsaved changes.</summary>
	public event EventHandler? DirtyChanged;

	/// <summary>
	/// Raised after <see cref="HasConflict"/> changed, so the host can offer <see cref="KeepViewText"/> and <see cref="TakeStoreText"/>.
	/// </summary>
	/// <remarks>
	/// The view raises it inside the coordinator's acknowledgment, which a UI dispatcher runs inline on its thread, so a handler posts
	/// its resolution.
	/// </remarks>
	public event EventHandler? ConflictChanged;

	/// <summary>Raised after an identity change gave the document another <see cref="DocumentId"/>, so the host can follow the rename.</summary>
	/// <remarks>
	/// The view raises it inside the coordinator's acknowledgment, so a handler that attaches or detaches views posts that work.
	/// </remarks>
	public event EventHandler? DocumentRenamed;

	/// <inheritdoc/>
	public string ViewId { get; }

	/// <inheritdoc/>
	public WorkspaceDocumentKey? DocumentKey => _synchronized?.DocumentKey;

	/// <inheritdoc/>
	public bool HasPendingEdits => _synchronized is not null && !string.Equals(ReadText(), SynchronizedText, StringComparison.Ordinal);

	/// <inheritdoc/>
	/// <remarks>
	/// The view
	/// reports a conflict when it moves onto text another writer changed while it held pending edits, and a line hunk of each change overlaps
	/// with different lines. The typed text then stays pending and unpublished until <see cref="KeepViewText"/>, <see cref="TakeStoreText"/>,
	/// an adopted snapshot such as a discard, or <see cref="Close"/> ends the conflict. The
	/// <see href="https://github.com/Nickelony/LanguageServer/blob/main/docs/WorkspaceViewsGuide.md#merging-another-writers-change">workspace views guide</see>
	/// owns the merge rule.
	/// </remarks>
	public bool HasConflict => _conflictBaseText is not null;

	/// <summary>The id of the document the view shows, or <see langword="null"/> while the view is detached.</summary>
	public string? DocumentId => _synchronized?.DocumentId;

	/// <summary>Indicates whether the editor text differs from the file, typed or published.</summary>
	public bool IsDirty => HasPendingEdits || _synchronized?.IsDirty == true;

	/// <summary>The text of the last snapshot the view acknowledged.</summary>
	internal string SynchronizedText { get; private set; } = string.Empty;

	/// <summary>Indicates whether the editor refuses typing.</summary>
	/// <remarks>
	/// The first delete lease sets it, and the release of the last one restores the value it had.
	/// </remarks>
	protected abstract bool IsReadOnly { get; set; }

	/// <inheritdoc/>
	/// <exception cref="ArgumentNullException"><paramref name="snapshot"/> is <see langword="null"/>.</exception>
	/// <exception cref="ObjectDisposedException">The view is disposed.</exception>
	public WorkspaceDocumentViewOpenResult Open(WorkspaceDocumentSnapshot snapshot)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		if (_synchronized is not null)
			return new WorkspaceDocumentViewOpenResult(WorkspaceDocumentViewOpenOutcome.AlreadyOpen);

		if (Adopt(snapshot) is { } exception)
		{
			return new WorkspaceDocumentViewOpenResult(
				WorkspaceDocumentViewOpenOutcome.Unavailable,
				Failure(WorkspaceViewOperationFailureCodes.OpenFailed, exception)
			);
		}

		return new WorkspaceDocumentViewOpenResult(WorkspaceDocumentViewOpenOutcome.Opened);
	}

	/// <inheritdoc/>
	/// <exception cref="ArgumentNullException"><paramref name="snapshot"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// A view with no open document, or a disposed one, reports <see cref="WorkspaceDocumentViewRefreshOutcome.Failed"/>, and a snapshot older
	/// than the one the view holds is ignored.
	/// </remarks>
	public WorkspaceDocumentViewRefreshResult Refresh(WorkspaceDocumentSnapshot snapshot)
	{
		ArgumentNullException.ThrowIfNull(snapshot);

		if (_isDisposed || _synchronized is null)
		{
			return new WorkspaceDocumentViewRefreshResult(
				WorkspaceDocumentViewRefreshOutcome.Failed,
				NoDocument(WorkspaceViewOperationFailureCodes.ViewRefreshFailed)
			);
		}

		if (IsOlderThanHeld(snapshot))
			return new WorkspaceDocumentViewRefreshResult(WorkspaceDocumentViewRefreshOutcome.Refreshed);

		// The coordinator skips a view with pending edits, so this guards a race with a keystroke only.
		if (HasPendingEdits)
			return new WorkspaceDocumentViewRefreshResult(WorkspaceDocumentViewRefreshOutcome.MarkedStale);

		if (Adopt(snapshot) is { } exception)
		{
			return new WorkspaceDocumentViewRefreshResult(
				WorkspaceDocumentViewRefreshOutcome.Failed,
				Failure(WorkspaceViewOperationFailureCodes.ViewRefreshFailed, exception)
			);
		}

		return new WorkspaceDocumentViewRefreshResult(WorkspaceDocumentViewRefreshOutcome.Refreshed);
	}

	/// <inheritdoc/>
	/// <exception cref="ArgumentNullException"><paramref name="change"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// Text the user typed meanwhile stays pending against the renamed document,
	/// merged with a change another writer made meanwhile, as an overtaken publication is. A view with no open document,
	/// or a disposed one, reports <see cref="WorkspaceDocumentViewIdentityOutcome.Failed"/>.
	/// </remarks>
	public WorkspaceDocumentViewIdentityResult AcknowledgeIdentity(WorkspaceDocumentViewIdentityChange change)
	{
		ArgumentNullException.ThrowIfNull(change);

		if (_isDisposed || _synchronized is null)
		{
			return new WorkspaceDocumentViewIdentityResult(
				WorkspaceDocumentViewIdentityOutcome.Failed,
				NoDocument(WorkspaceViewOperationFailureCodes.ViewIdentityUpdateFailed)
			);
		}

		string? oldDocumentId = DocumentId;

		Exception? failure = HasPendingEdits || HasConflict ? FollowAnotherWriter(change.Snapshot, publishes: false) : Adopt(change.Snapshot);

		if (failure is not null)
		{
			return new WorkspaceDocumentViewIdentityResult(
				WorkspaceDocumentViewIdentityOutcome.Failed,
				Failure(WorkspaceViewOperationFailureCodes.ViewIdentityUpdateFailed, failure)
			);
		}

		if (!string.Equals(oldDocumentId, DocumentId, StringComparison.Ordinal))
			DocumentRenamed?.Invoke(this, EventArgs.Empty);

		return new WorkspaceDocumentViewIdentityResult(WorkspaceDocumentViewIdentityOutcome.Updated);
	}

	/// <inheritdoc/>
	/// <exception cref="ArgumentNullException"><paramref name="result"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// <para>
	/// The acknowledgment of the view's own publication moves the base of the edits to the snapshot of the result, and a publication held
	/// back meanwhile follows against the new base. When another writer changed the document first, the view merges its typed change into the newer
	/// text by line hunks and publishes the result, unless a hunk of each side overlaps with different lines, which <see cref="HasConflict"/> reports
	/// while the acknowledgment still counts as applied. An applied change that is not the view's own publication, such as a discard,
	/// replaces the editor text.
	/// </para>
	/// <para>
	/// A snapshot older than the one the view holds is ignored, and a view with no open document,
	/// or a disposed one, reports <see cref="WorkspaceDocumentViewApplyOutcome.Failed"/>.
	/// </para>
	/// </remarks>
	public WorkspaceDocumentViewApplyResult AcknowledgeApply(WorkspaceDocumentMutationResult result)
	{
		ArgumentNullException.ThrowIfNull(result);

		if (_isDisposed || _synchronized is null)
		{
			return new WorkspaceDocumentViewApplyResult(
				WorkspaceDocumentViewApplyOutcome.Failed,
				NoDocument(WorkspaceViewOperationFailureCodes.ViewAcknowledgeFailed)
			);
		}

		bool applied = result.Applied && result.Snapshot is not null;
		bool isOwnPublication = _publication.Acknowledges(result, applied);
		bool isRepublishMarked = isOwnPublication && _publication.Complete();

		if (result.Snapshot is { } snapshot && !IsOlderThanHeld(snapshot))
		{
			if (applied && !isOwnPublication)
			{
				if (Adopt(snapshot) is { } exception)
				{
					return new WorkspaceDocumentViewApplyResult(
						WorkspaceDocumentViewApplyOutcome.Failed,
						Failure(WorkspaceViewOperationFailureCodes.ViewAcknowledgeFailed, exception)
					);
				}
			}
			else if (isOwnPublication && IsChangedByAnotherWriter(result, snapshot))
			{
				return FollowAnotherWriter(snapshot, publishes: true) is { } exception
					? new WorkspaceDocumentViewApplyResult(
						WorkspaceDocumentViewApplyOutcome.Failed,
						Failure(WorkspaceViewOperationFailureCodes.ViewAcknowledgeFailed, exception)
					)
					: new WorkspaceDocumentViewApplyResult(WorkspaceDocumentViewApplyOutcome.Applied);
			}
			else if (!(applied && isOwnPublication) && !string.Equals(snapshot.Content, SynchronizedText, StringComparison.Ordinal))
			{
				if (FollowAnotherWriter(snapshot, publishes: false) is { } exception)
				{
					return new WorkspaceDocumentViewApplyResult(
						WorkspaceDocumentViewApplyOutcome.Failed,
						Failure(WorkspaceViewOperationFailureCodes.ViewAcknowledgeFailed, exception)
					);
				}
			}
			else
			{
				Rebase(snapshot);
			}
		}

		// The republish raises inside this acknowledgment, so it runs last, after the view recorded the new base.
		if (isRepublishMarked)
			PublishPendingEdits();

		return applied ? new WorkspaceDocumentViewApplyResult(WorkspaceDocumentViewApplyOutcome.Applied) : StoreRejected(result);
	}

	/// <inheritdoc/>
	/// <remarks>
	/// While an earlier publication awaits its acknowledgment, the view raises nothing and publishes again once that acknowledgment arrives,
	/// because both would carry the same base version. A view with a conflict raises nothing, and neither does a call after disposal,
	/// such as a debounced tick that fires late.
	/// </remarks>
	public void PublishPendingEdits()
	{
		_publishDebouncer?.Cancel();

		if (!_isDisposed && !HasConflict && HasPendingEdits)
			Publish();
	}

	/// <summary>Ends a conflict with the view's side of each conflicting region and publishes the result.</summary>
	/// <remarks>
	/// Call it on the dispatcher thread; a view without a conflict does nothing. Every hunk of either side that conflicts with none stays,
	/// so the other writer loses only its side of the conflicting regions. When the store changed again meanwhile, the publication is rejected,
	/// and the view merges or reports a conflict as it does for any other publication. A <see cref="ReplaceText"/> that throws propagates,
	/// and the conflict stays.
	/// </remarks>
	public void KeepViewText() => Resolve(keepsViewSide: true);

	/// <summary>Ends a conflict with the store's side of each conflicting region and publishes the result.</summary>
	/// <remarks>
	/// A host that lets the user decide offers it beside <see cref="KeepViewText"/> as the choice that accepts the other writer's text.
	/// Call it on the dispatcher thread; a view without a conflict does nothing. Every hunk of either side that conflicts with none stays,
	/// so the typed text loses only its side of the conflicting regions. A store that changed again meanwhile answers the publication
	/// with its newer snapshot, which the view merges. A <see cref="ReplaceText"/> that throws propagates, and the conflict stays.
	/// </remarks>
	public void TakeStoreText() => Resolve(keepsViewSide: false);

	/// <summary>
	/// Ends <paramref name="request"/> when it is the publication in flight, because its apply threw and no acknowledgment follows.
	/// </summary>
	/// <param name="request">The replacement whose apply threw.</param>
	/// <remarks>
	/// The text stays pending, so the next change or save publishes it again.
	/// </remarks>
	internal void EndLostPublication(WorkspaceDocumentReplaceRequest request)
	{
		if (_publication.IsInFlight(request))
			_ = _publication.Complete();
	}

	/// <inheritdoc/>
	/// <remarks>
	/// The view forgets a publication that awaits its acknowledgment and a conflict, and retries a read-only restore that a lease release
	/// left pending.
	/// </remarks>
	public void Close()
	{
		_publishDebouncer?.Cancel();
		_synchronized = null;
		_ = _publication.Complete();

		// A restore that fails again stays pending, and the next lease release retries it.
		_ = _leases.RetryRestore();

		RaiseDirtyChanged();
		SetConflict(null);
	}

	/// <inheritdoc/>
	/// <remarks>
	/// The first entry saves <see cref="IsReadOnly"/> and sets it, and a nested entry only counts. A view with no open document,
	/// or a disposed one, reports a failure and leaves the editor alone.
	/// </remarks>
	public WorkspaceDocumentViewDeleteLeaseResult EnterDeleteLease()
	{
		if (_isDisposed)
			return FailedLease("The view is disposed.");

		return _synchronized is null ? FailedLease(NoDocumentMessage) : _leases.Enter();
	}

	/// <inheritdoc/>
	/// <remarks>
	/// The release of the last entry restores <see cref="IsReadOnly"/>, also after a close, so a lease entered before the close does not leave
	/// the editor read-only. A release counts down even when the restore throws, and the next release of the last entry or <see cref="Close"/>
	/// retries the restore. A disposed view reports a failure.
	/// </remarks>
	public WorkspaceDocumentViewDeleteLeaseResult ReleaseDeleteLease() => _isDisposed ? FailedLease("The view is disposed.") : _leases.Release();

	/// <remarks>
	/// Call it on the dispatcher thread, because it cancels the debounced publication that the dispatcher runs.
	/// </remarks>
	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}

	/// <summary>Stops the debounced publication and forgets a publication that awaits its acknowledgment.</summary>
	/// <param name="disposing"><see langword="true"/> when <see cref="Dispose()"/> called it.</param>
	protected virtual void Dispose(bool disposing)
	{
		if (_isDisposed)
			return;

		_isDisposed = true;

		if (!disposing)
			return;

		_publishDebouncer?.Dispose();
		_ = _publication.Complete();
	}

	/// <summary>Returns the text the editor shows.</summary>
	protected abstract string ReadText();

	/// <summary>Makes <paramref name="text"/> the text the editor shows.</summary>
	/// <param name="text">The text of the snapshot the view adopts, or the typed text with another writer's change merged in.</param>
	/// <remarks>
	/// Replace only the runs of lines and the characters that differ, as <c>ReplaceContent</c> of an editor binding does, so the caret,
	/// the selection and the scroll position outside them stay. The view ignores a <see cref="HandleTextChanged"/> call that the replacement raises.
	/// When it throws, the view keeps the snapshot it had unless the editor shows the new text anyway, so other editor text counts as pending edits.
	/// </remarks>
	protected abstract void ReplaceText(string text);

	/// <summary>Updates the dirty state after the editor text changed, and publishes the text when the publish delay asks for it.</summary>
	/// <remarks>
	/// A call after disposal is ignored, because an editor can raise a change while it is torn down.
	/// </remarks>
	protected void HandleTextChanged()
	{
		if (_isDisposed || _isReplacing)
			return;

		RaiseDirtyChanged();

		if (_publishesOnEachChange)
			PublishPendingEdits();
		else if (_publishDebouncer is not null && HasPendingEdits)
			_publishDebouncer.Arm(PublishPendingEdits);
	}

	private static WorkspaceOperationFailure Failure(string code, Exception exception) => new(code, exception.Message, exception);

	private static WorkspaceOperationFailure NoDocument(string code) => new(code, NoDocumentMessage);

	private static WorkspaceDocumentViewDeleteLeaseResult FailedLease(string message)
	{
		return new(
			WorkspaceDocumentViewDeleteLeaseOutcome.Failed,
			new WorkspaceOperationFailure(WorkspaceViewOperationFailureCodes.DeleteLeaseFailed, message)
		);
	}

	private static WorkspaceDocumentViewApplyResult StoreRejected(WorkspaceDocumentMutationResult result)
	{
		return new(
			WorkspaceDocumentViewApplyOutcome.Failed,
			new WorkspaceOperationFailure(WorkspaceViewOperationFailureCodes.ViewApplyFailed, $"The store answered {result.Outcome}.")
		);
	}

	private bool IsOlderThanHeld(WorkspaceDocumentSnapshot snapshot) =>
		_synchronized is { } held && held.DocumentKey == snapshot.DocumentKey && snapshot.Version < held.Version;

	// The store rejects a publication as stale when another writer changed the same document instance after the base the publication carries.
	private bool IsChangedByAnotherWriter(WorkspaceDocumentMutationResult result, WorkspaceDocumentSnapshot snapshot)
	{
		return result.Outcome == WorkspaceDocumentMutationOutcome.StaleDocument
			&& _synchronized is { } held
			&& held.DocumentKey == snapshot.DocumentKey
			&& snapshot.Version > held.Version;
	}

	// Moves the base of a view that holds pending edits onto a snapshot whose text another writer changed, merging the typed change into it by
	// line hunks (LineMerger). A merge shows the merged text and, when asked, publishes it; a conflicting region keeps the typed text pending
	// against the snapshot and opens a conflict. An open conflict keeps its base and follows the newer store text, which its resolution merges.
	// Returns the exception of a replacement that threw, which keeps the base the view had, so the next move merges again.
	private Exception? FollowAnotherWriter(WorkspaceDocumentSnapshot snapshot, bool publishes)
	{
		if (HasConflict)
		{
			Rebase(snapshot);
			return null;
		}

		string typed = ReadText();

		if (string.Equals(typed, SynchronizedText, StringComparison.Ordinal) || string.Equals(typed, snapshot.Content, StringComparison.Ordinal))
			return Adopt(snapshot);

		if (string.Equals(snapshot.Content, SynchronizedText, StringComparison.Ordinal))
		{
			Rebase(snapshot);
		}
		else if (LineMerger.TryMerge(baseText: SynchronizedText, viewText: typed, storeText: snapshot.Content, out string? merged))
		{
			// The editor replaces only the lines the other writer changed, so the caret stays where the typing put it.
			try
			{
				if (!string.Equals(merged, typed, StringComparison.Ordinal))
					ShowText(merged);
			}
			catch (Exception exception)
			{
				return exception;
			}

			Rebase(snapshot);
		}
		else
		{
			string conflictBaseText = SynchronizedText;
			Rebase(snapshot);
			SetConflict(conflictBaseText);
			return null;
		}

		if (publishes)
			Publish();

		return null;
	}

	// Ends a conflict with one side of each conflicting region, keeps every other hunk of both sides, and publishes the result.
	private void Resolve(bool keepsViewSide)
	{
		if (_isDisposed || _conflictBaseText is not { } conflictBaseText || _synchronized is not { } snapshot)
			return;

		string typed = ReadText();
		string resolved = LineMerger.Resolve(baseText: conflictBaseText, viewText: typed, storeText: snapshot.Content, keepsViewSide);

		if (!string.Equals(resolved, typed, StringComparison.Ordinal))
			ShowText(resolved);

		SetConflict(null);
		RaiseDirtyChanged();
		Publish();
	}

	// Raises the editor text against the synchronized snapshot, unless an earlier publication awaits its acknowledgment.
	private void Publish()
	{
		if (_isDisposed || _synchronized is not { } snapshot || ApplyRequested is not { } handler || _publication.TryHoldBack())
			return;

		string text = ReadText();
		var request = new WorkspaceDocumentReplaceRequest(snapshot.Identity, text, WithStyleOf(text, snapshot.FileFormat));

		// An inline dispatcher acknowledges inside the raise, so the publication is recorded first.
		_publication.Record(request);
		handler(this, new ApplyRequestedEventArgs(request));
	}

	// The line terminator style describes the text, so a publication carries the style of the text it publishes, and an edit of the terminators needs no
	// call of the host. A text without a line terminator keeps the style the document recorded.
	private static TextFileFormat WithStyleOf(string text, TextFileFormat format)
	{
		LineTerminatorStyle detected = LineTerminatorDetector.Detect(text);
		return detected == LineTerminatorStyle.None ? format : format with { LineTerminatorStyle = detected };
	}

	// Shows the snapshot text, then records the snapshot, so a replacement that throws leaves the view on the snapshot it had. The view still
	// records it when the editor shows the snapshot text anyway, because that text is no pending edit. Returns the exception of a failed replacement.
	private Exception? Adopt(WorkspaceDocumentSnapshot snapshot)
	{
		try
		{
			ShowText(snapshot.Content);
		}
		catch (Exception exception)
		{
			if (string.Equals(ReadText(), snapshot.Content, StringComparison.Ordinal))
			{
				Rebase(snapshot);
				SetConflict(null);
			}

			return exception;
		}

		Rebase(snapshot);
		SetConflict(null);
		return null;
	}

	// Replaces the editor text while the view ignores the change notifications the replacement raises.
	private void ShowText(string text)
	{
		_isReplacing = true;

		try
		{
			ReplaceText(text);
		}
		finally
		{
			_isReplacing = false;
		}
	}

	private void Rebase(WorkspaceDocumentSnapshot snapshot)
	{
		_synchronized = snapshot;
		SynchronizedText = snapshot.Content;
		RaiseDirtyChanged();
	}

	private void RaiseDirtyChanged()
	{
		bool isDirty = IsDirty;

		if (isDirty == _wasDirty)
			return;

		_wasDirty = isDirty;
		DirtyChanged?.Invoke(this, EventArgs.Empty);
	}

	private void SetConflict(string? conflictBaseText)
	{
		bool hadConflict = HasConflict;
		_conflictBaseText = conflictBaseText;

		if (HasConflict != hadConflict)
			ConflictChanged?.Invoke(this, EventArgs.Empty);
	}
}

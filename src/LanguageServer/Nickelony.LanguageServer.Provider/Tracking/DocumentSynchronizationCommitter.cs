using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Text;

namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Commits the content and the leases of a synchronization to the tracked records and returns the action that mirrors it to the server.
/// </summary>
/// <param name="syncRoot">The tracker's lock, which the commit takes for the state mutation.</param>
/// <param name="documents">The set of tracked records.</param>
/// <remarks>
/// <para>
/// The provider normalizes each document path once as it enters the request pipeline and threads that value through the per-document scheduler,
/// so the committer does not normalize again.
/// </para>
/// <para>
/// Content and version are committed when the directive is returned, before the caller sends the returned notification. A caller that fails
/// to deliver the notification must invalidate the tracked state (for example by closing the document) so the next synchronization emits a full open
/// instead of computing incremental ranges against content the server never received.
/// </para>
/// <para>
/// Calls for the same document path must be serialized by the caller, because each change range is computed against the content
/// the previous call committed; delivering out of order or synchronizing concurrently produces ranges for content the server never received,
/// and the returned version lets a caller detect reordering.
/// </para>
/// </remarks>
internal sealed class DocumentSynchronizationCommitter(object syncRoot, TrackedDocumentSet documents)
{
	/// <summary>Synchronizes the tracked state for a document and returns the LSP action required to mirror it to the server.</summary>
	/// <param name="normalizedFilePath">The normalized local file path of the document.</param>
	/// <param name="content">The latest document content.</param>
	/// <param name="leases">The leases the synchronization acquires for the document.</param>
	/// <param name="includeChangeRange">
	/// Whether content changes should compute an incremental change range. Callers that negotiated full synchronization can pass
	/// <see langword="false"/> to skip the diff computation.
	/// </param>
	/// <param name="requestLease">
	/// The lease object that receives the acquired request lease. The release contract lives on <see cref="DocumentRequestLease"/>;
	/// pass a fresh instance when, and only when, <see cref="DocumentLeaseKinds.Request"/> is set in <paramref name="leases"/>.
	/// </param>
	/// <param name="documentId">
	/// The id the host supplied for the document, or <see langword="null"/> when it equals <paramref name="normalizedFilePath"/>.
	/// A new record takes it, and an existing record takes it when the synchronization acquires an open lease.
	/// </param>
	/// <returns>
	/// A synchronization directive when the server copy must be updated; otherwise, <see langword="null"/> - a <see langword="null"/> result
	/// still records the requested open/request leases and refreshes the access stamp.
	/// </returns>
	/// <exception cref="ArgumentException">
	/// <paramref name="requestLease"/> is supplied without <see cref="DocumentLeaseKinds.Request"/>,
	/// or <see cref="DocumentLeaseKinds.Request"/> is set without a <paramref name="requestLease"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException"><paramref name="requestLease"/> was already bound or released.</exception>
	/// <remarks>
	/// Synchronization can create an idle server-open record when neither lease option is selected. The record is retained until explicitly closed
	/// or removed by trimming.
	/// </remarks>
	internal DocumentSynchronizationDirective? Synchronize(
		string normalizedFilePath,
		string content,
		DocumentLeaseKinds leases,
		bool includeChangeRange,
		DocumentRequestLease? requestLease,
		string? documentId = null
	)
	{
		// The flags are translated once here; the rest of the method only needs the acquisition decisions.
		bool acquireOpenLease = (leases & DocumentLeaseKinds.Open) != 0;
		bool acquireRequestLease = (leases & DocumentLeaseKinds.Request) != 0;

		if (!acquireRequestLease && requestLease is not null)
		{
			throw new ArgumentException(
				"A request lease can only be supplied together with the DocumentLeaseKinds.Request flag.",
				nameof(requestLease)
			);
		}

		if (acquireRequestLease && requestLease is null)
		{
			// Without a lease object nothing can release the acquired lease, so the record would stay request-busy.
			throw new ArgumentException(
				"The DocumentLeaseKinds.Request flag requires the lease object that releases the acquisition.",
				nameof(requestLease)
			);
		}

		// Populated on the incremental-change path and consumed after the tracker lock is released; every other path
		// returns from inside the lock, so the locals are definitely assigned below.
		string previousContent;
		DocumentSnapshot changeSnapshot;

		lock (syncRoot)
		{
			if (acquireRequestLease && requestLease is not null && !requestLease.CanBind)
			{
				throw new InvalidOperationException(
					"The supplied document request lease was already bound or released; document request leases are single-use."
				);
			}

			if (!documents.TryGet(normalizedFilePath, out TrackedDocumentState? state))
			{
				state = new TrackedDocumentState(
					new TrackedDocumentDescriptor(
						FilePath: normalizedFilePath,
						Uri: DocumentUriConversion.CreateFileUri(normalizedFilePath),
						content,
						Version: 1,
						IsOpen: true,
						OpenLeaseCount: acquireOpenLease ? 1 : 0,
						RequestLeaseCount: acquireRequestLease ? 1 : 0,
						LastAccessStamp: documents.NextAccessStamp(),
						documentId
					)
				);

				if (acquireRequestLease)
					requestLease?.Bind(state);

				documents.Set(normalizedFilePath, state);
				return new(DocumentSynchronizationKind.Open, state.CreateSnapshot());
			}

			if (acquireOpenLease)
			{
				state.Leases.AcquireOpen();

				if (documentId is not null)
					state.AdoptDocumentId(documentId);
			}

			if (acquireRequestLease)
			{
				state.Leases.AcquireRequest();
				requestLease?.Bind(state);
			}

			state.SetLastAccessStamp(documents.NextAccessStamp());

			if (!state.IsOpen)
			{
				state.ReopenDocument(content);
				return new(DocumentSynchronizationKind.Open, state.CreateSnapshot());
			}

			if (!string.Equals(state.Content, content, StringComparison.Ordinal))
			{
				previousContent = state.ReplaceContent(content);
				changeSnapshot = state.CreateSnapshot();

				if (!includeChangeRange)
					return new(DocumentSynchronizationKind.Change, changeSnapshot);
			}
			else
			{
				return null;
			}
		}

		// The incremental change range is computed outside the tracker lock: the inputs are immutable strings and the
		// computation reads no tracker state, so the lock only covers the state mutation above.
		TextIncrementalEdit change = TextIncrementalEditCalculator.Compute(previousContent, content);

		return new(
			DocumentSynchronizationKind.Change,
			changeSnapshot,
			new DocumentChangeRange(TextLineMap.GetPositionRange(previousContent, change.Range), Text: change.NewText)
		);
	}
}

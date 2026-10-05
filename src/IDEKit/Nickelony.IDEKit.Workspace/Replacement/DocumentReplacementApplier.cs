using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Replacement;

/// <summary>
/// Applies prepared whole-content document replacements through the workspace document authority, with no-op detection and non-atomic
/// partial results.
/// </summary>
/// <remarks>
/// Narrower than the Language Server Protocol's <c>WorkspaceEdit</c>: it applies prepared whole-content replacements
/// and does not create, rename, delete, or compute edits from scratch. Targets are processed in list order and the operation is non-atomic,
/// so a confirmed change stays applied when a later target fails. Each replacement completes before the next target is attempted. A no-op target
/// or a <see cref="WorkspaceDocumentMutationOutcome.NoChange"/> result writes nothing and adds no change record.
/// </remarks>
public sealed class DocumentReplacementApplier
{
	private readonly Func<WorkspaceDocumentReplaceRequest, CancellationToken, Task<DocumentReplacementAttemptResult>> _replaceAsync;
	private readonly WorkspaceOptions _options;
	private readonly bool _continueOnFailure;

	/// <param name="replaceAsync">
	/// Replaces the logical content of one tracked document and returns the document authority's result, such as the result
	/// of <see cref="IWorkspaceDocumentStore.Replace"/> in a completed task, or refuses before it reaches the document. It receives the token
	/// passed to <see cref="ApplyAsync"/>, and it throws an <see cref="OperationCanceledException"/> of that token only before it reaches
	/// the document.
	/// </param>
	/// <param name="options">
	/// The workspace options whose path comparison is used for the target and document ids. It de-duplicates the changed
	/// and unknown document ids in the returned results and groups targets that share a document id under a different spelling for version
	/// chaining and same-document rejection propagation. Supply the options that the store uses so neither can drift from document identity;
	/// the default follows the operating system.
	/// </param>
	/// <param name="applierOptions">The behavior choices of the applier; the default stops at the first target that is not applied.</param>
	/// <exception cref="ArgumentNullException"><paramref name="replaceAsync"/> is <see langword="null"/>.</exception>
	public DocumentReplacementApplier(
		Func<WorkspaceDocumentReplaceRequest, CancellationToken, Task<DocumentReplacementAttemptResult>> replaceAsync,
		WorkspaceOptions? options = null,
		DocumentReplacementApplierOptions? applierOptions = null
	)
	{
		ArgumentNullException.ThrowIfNull(replaceAsync);

		_replaceAsync = replaceAsync;
		_options = options ?? WorkspaceOptions.Default;
		_continueOnFailure = (applierOptions ?? DocumentReplacementApplierOptions.Default).ContinueOnFailure;
	}

	/// <summary>
	/// Applies prepared replacements in order, stopping at the first target whose result is not applied unless the applier was constructed
	/// with <see cref="DocumentReplacementApplierOptions.ContinueOnFailure"/>.
	/// </summary>
	/// <param name="targets">The prepared replacements to apply.</param>
	/// <param name="cancellationToken">A token that can cancel the application before the next target is attempted.</param>
	/// <returns>The application result with the per-target outcomes and the confirmed change set.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="targets"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="targets"/> contains a <see langword="null"/> entry, an entry whose document id or identity document id is empty or whitespace,
	/// or an entry whose <see cref="DocumentReplacementTarget.AfterFileFormat"/> asks for a byte-order mark that its encoding has none of.
	/// </exception>
	/// <exception cref="OperationCanceledException">
	/// The replacement delegate threw an <see cref="OperationCanceledException"/> of another token.
	/// </exception>
	/// <remarks>
	/// The targets are validated before the returned task starts,
	/// so an argument error throws from the call. A target that was not attempted because an earlier target failed or the application was canceled
	/// is marked <see cref="DocumentReplacementTargetOutcome.NotApplied"/>, and so is a target the delegate refused and a store rejection proven
	/// not to have mutated the document. A delegate that throws an <see cref="OperationCanceledException"/> of <paramref name="cancellationToken"/>
	/// after the token was canceled reports that it did not reach the document: the target is not applied and the result is canceled or partial.
	/// A delegate that threw anything else, or faulted, is <see cref="DocumentReplacementTargetOutcome.Unknown"/>. Once a document's replacement
	/// is not applied, later targets for that document are not attempted.
	/// </remarks>
	public Task<DocumentReplacementResult> ApplyAsync(IReadOnlyList<DocumentReplacementTarget> targets, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(targets);

		ValidateTargets(targets);

		return ApplyCoreAsync(targets, cancellationToken);
	}

	private async Task<DocumentReplacementResult> ApplyCoreAsync(
		IReadOnlyList<DocumentReplacementTarget> targets,
		CancellationToken cancellationToken
	)
	{
		if (targets.Count == 0)
			return CreateCompletedResult([], [], _options);

		var documentChanges = new List<DocumentReplacementChange>();
		var targetResults = new List<DocumentReplacementTargetResult>(targets.Count);
		var unknownDocumentIds = new List<string>();

		// One or more prepared changes can target the same document. The store advances the document
		// version on each accepted replacement, so every later request for the same document uses the
		// version confirmed by the earlier result instead of its own stale preflight version. The
		// dictionary uses the configured target-id comparison, so case-variant ids that the result
		// de-duplication treats as one document are also tracked as one document.
		var observedVersions = new Dictionary<string, long>(_options.PathComparison.Comparer);

		// Documents whose replacement did not apply (deterministic rejection or unknown outcome). A
		// later target for the same document is not attempted: it was prepared against a document state
		// the applier has not confirmed, and a rejection must never authorize a later write against
		// state the application did not observe.
		var rejectedDocuments = new HashSet<string>(_options.PathComparison.Comparer);

		WorkspaceOperationFailure? firstFailure = null;

		for (int targetIndex = 0; targetIndex < targets.Count; targetIndex++)
		{
			DocumentReplacementTarget target = targets[targetIndex];

			if (cancellationToken.IsCancellationRequested)
			{
				AddNotAppliedTargetResults(targets, targetResults, targetIndex);
				return CreateCanceledOrPartialResult(documentChanges, targetResults, unknownDocumentIds, firstFailure, options: _options);
			}

			// A target whose before and after content and format match writes nothing, so it is applied
			// without consulting the store - and without being affected by an earlier rejection of a
			// different target for the same document.
			if (
				string.Equals(target.BeforeContent, target.AfterContent, StringComparison.Ordinal)
				&& target.BeforeFileFormat == target.AfterFileFormat
			)
			{
				// The store is not consulted, so the actual version is only observable when an earlier
				// target of the same document recorded one; otherwise, it is unknown.
				long? noOpVersion = observedVersions.TryGetValue(target.Identity.DocumentId, out long recordedVersion) ? recordedVersion : null;

				targetResults.Add(
					CreateResult(
						target,
						preparedOperationCount: 0,
						outcome: DocumentReplacementTargetOutcome.Applied,
						failure: null,
						actualVersion: noOpVersion
					)
				);

				continue;
			}

			// Reaching this point with a rejected document requires ContinueOnFailure: without it the
			// application already returned when the rejection was recorded. A target that would write is
			// not attempted for a document whose replacement was not applied, because it was prepared
			// against a document state the applier has not confirmed.
			if (rejectedDocuments.Contains(target.Identity.DocumentId))
			{
				WorkspaceOperationFailure rejectedFailure = new(
					DocumentReplacementFailureCodes.TargetSkipped,
					$"The workspace document '{target.Identity.DocumentId}' was not replaced because an earlier target for the same document was not applied."
				);

				firstFailure ??= rejectedFailure;

				targetResults.Add(
					CreateResult(
						target,
						preparedOperationCount: GetPreparedReplacementCount(target),
						outcome: DocumentReplacementTargetOutcome.NotApplied,
						failure: rejectedFailure,
						actualVersion: null
					)
				);

				continue;
			}

			long effectiveVersion = observedVersions.TryGetValue(target.Identity.DocumentId, out long previousVersion)
				? previousVersion
				: target.Identity.Version;

			// Both failure paths below assign a detail before the partial result is created.
			WorkspaceOperationFailure failure;

			try
			{
				DocumentReplacementAttemptResult attempt = await _replaceAsync(
						new WorkspaceDocumentReplaceRequest(
							new WorkspaceDocumentRequestIdentity(target.Identity.DocumentKey, target.Identity.DocumentId, effectiveVersion),
							target.AfterContent,
							target.AfterFileFormat
						),

						cancellationToken
					)
					.ConfigureAwait(false);

				if (attempt.StoreResult is not { } result)
				{
					// The delegate refused before it reached the document, so the target is known not to have changed it.
					failure = attempt.Failure!;
					rejectedDocuments.Add(target.Identity.DocumentId);

					targetResults.Add(
						CreateResult(
							target,
							preparedOperationCount: GetPreparedReplacementCount(target),
							outcome: DocumentReplacementTargetOutcome.NotApplied,
							failure,
							actualVersion: null
						)
					);
				}
				else if (RecordSuccessfulReplacement(target, result, documentChanges, observedVersions, targetResults))
				{
					continue;
				}
				else if (IsDeterministicRejection(result.Outcome))
				{
					// The store returns these outcomes before any mutation, so the replacement is known
					// not to have touched the document.
					failure = new WorkspaceOperationFailure(
						DocumentReplacementFailureCodes.TargetNotChanged,
						$"The workspace document '{target.Identity.DocumentId}' was not replaced: {result.Outcome}."
					);

					rejectedDocuments.Add(target.Identity.DocumentId);

					targetResults.Add(
						CreateResult(
							target,
							preparedOperationCount: GetPreparedReplacementCount(target),
							outcome: DocumentReplacementTargetOutcome.NotApplied,
							failure,
							actualVersion: null
						)
					);
				}
				else
				{
					// The outcome is outside the document authority's vocabulary, so whether the document was
					// mutated cannot be established: the outcome is unknown, not "not changed".
					failure = new WorkspaceOperationFailure(
						DocumentReplacementFailureCodes.TargetOutcomeUnknown,
						$"The workspace document '{target.Identity.DocumentId}' returned the unrecognized replacement outcome '{result.Outcome}'."
					);

					rejectedDocuments.Add(target.Identity.DocumentId);
					unknownDocumentIds.Add(target.Identity.DocumentId);

					targetResults.Add(
						CreateResult(
							target,
							preparedOperationCount: GetPreparedReplacementCount(target),
							outcome: DocumentReplacementTargetOutcome.Unknown,
							failure,
							actualVersion: null
						)
					);
				}
			}
			catch (OperationCanceledException exception)
				when (cancellationToken.IsCancellationRequested && exception.CancellationToken == cancellationToken)
			{
				// The delegate observed the applier's own token before it reached the document, so the target did not change it and the
				// application ends as a cancellation between targets does, with the exception attached.
				AddNotAppliedTargetResults(targets, targetResults, targetIndex);

				return CreateCanceledOrPartialResult(
					documentChanges,
					targetResults,
					unknownDocumentIds,
					firstFailure,
					options: _options,
					cancellation: exception
				);
			}
			catch (OperationCanceledException)
			{
				// A cancellation of another token is not a target outcome: it propagates so the caller can
				// stop the session instead of receiving a per-target failure that hides the cancellation.
				throw;
			}
			catch (Exception exception)
			{
				failure = new WorkspaceOperationFailure(DocumentReplacementFailureCodes.TargetApplicationFailed, exception.Message, exception);

				rejectedDocuments.Add(target.Identity.DocumentId);
				unknownDocumentIds.Add(target.Identity.DocumentId);

				targetResults.Add(
					CreateResult(
						target,
						preparedOperationCount: GetPreparedReplacementCount(target),
						outcome: DocumentReplacementTargetOutcome.Unknown,
						failure,
						actualVersion: null
					)
				);
			}

			firstFailure ??= failure;

			if (!_continueOnFailure)
			{
				AddNotAppliedTargetResults(targets, targetResults, targetIndex + 1);
				return CreatePartialResult(documentChanges, targetResults, unknownDocumentIds, failure, options: _options);
			}
		}

		return firstFailure is null
			? CreateCompletedResult(documentChanges, targetResults, _options)
			: CreatePartialResult(documentChanges, targetResults, unknownDocumentIds, failure: firstFailure, options: _options);
	}

	private static DocumentReplacementTargetResult CreateResult(
		DocumentReplacementTarget target,
		int preparedOperationCount,
		DocumentReplacementTargetOutcome outcome,
		WorkspaceOperationFailure? failure,
		long? actualVersion
	)
	{
		return new()
		{
			DocumentId = target.Identity.DocumentId,
			ExpectedVersion = target.Identity.Version,
			ActualVersion = actualVersion,
			PreparedOperationCount = preparedOperationCount,
			Outcome = outcome,
			Failure = failure,
		};
	}

	// Entries are validated up front - a null entry would otherwise surface as a
	// NullReferenceException while the per-target results are built, and an unencodable format
	// would otherwise surface as an unknown per-target outcome.
	private static void ValidateTargets(IReadOnlyList<DocumentReplacementTarget> targets)
	{
		for (int index = 0; index < targets.Count; index++)
		{
			if (targets[index] is null)
				throw new ArgumentException("A prepared replacement target cannot be null.", nameof(targets));

			DocumentReplacementTarget target = targets[index];

			if (string.IsNullOrWhiteSpace(target.Identity.DocumentId))
				throw new ArgumentException("A prepared replacement target must carry a non-blank document id in its identity.", nameof(targets));

			WorkspaceTextCodec.EnsureEncodable(target.AfterFileFormat, $"targets[{index}].AfterFileFormat");
		}
	}

	// Records a confirmed replacement: advances the version later targets for the same document use,
	// appends a change record for a Changed outcome, and adds the applied target result. Returns false
	// when the result is a rejection the caller maps next.
	private static bool RecordSuccessfulReplacement(
		DocumentReplacementTarget target,
		WorkspaceDocumentMutationResult result,
		List<DocumentReplacementChange> documentChanges,
		Dictionary<string, long> observedVersions,
		List<DocumentReplacementTargetResult> targetResults
	)
	{
		if (!result.Applied)
			return false;

		// Only a confirmed replacement advances the version used by later targets for the same
		// document: a rejection carries a current-state snapshot of its own, and adopting that
		// version would authorize a later write against state the applier never observed. A
		// replacement delegate that confirms a mutation without returning a snapshot falls back to
		// the version the request was built with.
		observedVersions[target.Identity.DocumentId] = result.Snapshot?.Version ?? result.RequestedIdentity.Version;

		// Only a confirmed replacement produces a change record. A NoChange result means the store
		// already held the requested content (for example after another writer applied it), so the
		// applier did not transform the document and recording BeforeContent -> AfterContent would
		// misreport the change to host undo tracking.
		if (result.Outcome == WorkspaceDocumentMutationOutcome.Changed)
		{
			documentChanges.Add(
				new DocumentReplacementChange
				{
					DocumentId = target.Identity.DocumentId,
					BeforeContent = target.BeforeContent,
					AfterContent = target.AfterContent,
					BeforeFileFormat = target.BeforeFileFormat,
					AfterFileFormat = target.AfterFileFormat,
				}
			);
		}

		// The store increments the document version before it creates the result snapshot, so the
		// snapshot carries the post-mutation version while the requested identity echoes the version
		// the request was built with.
		targetResults.Add(
			CreateResult(
				target,
				preparedOperationCount: GetPreparedReplacementCount(target),
				outcome: DocumentReplacementTargetOutcome.Applied,
				failure: null,
				actualVersion: result.Snapshot?.Version ?? result.RequestedIdentity.Version
			)
		);

		return true;
	}

	private static void AddNotAppliedTargetResults(
		IReadOnlyList<DocumentReplacementTarget> targets,
		List<DocumentReplacementTargetResult> targetResults,
		int startIndex
	)
	{
		for (int index = startIndex; index < targets.Count; index++)
		{
			targetResults.Add(
				CreateResult(
					target: targets[index],
					preparedOperationCount: GetPreparedReplacementCount(targets[index]),
					outcome: DocumentReplacementTargetOutcome.NotApplied,
					failure: null,
					actualVersion: null
				)
			);
		}
	}

	// A target contributes one prepared replacement operation when its content or format differs from
	// the before state.
	private static int GetPreparedReplacementCount(DocumentReplacementTarget target)
	{
		return string.Equals(target.BeforeContent, target.AfterContent, StringComparison.Ordinal) && target.BeforeFileFormat == target.AfterFileFormat
			? 0
			: 1;
	}

	// Store outcomes that prove the replacement did not touch the document: they are returned before
	// any mutation is applied.
	private static bool IsDeterministicRejection(WorkspaceDocumentMutationOutcome outcome)
	{
		return outcome
			is WorkspaceDocumentMutationOutcome.StaleDocument
				or WorkspaceDocumentMutationOutcome.StaleDocumentInstance
				or WorkspaceDocumentMutationOutcome.DocumentNotFound
				or WorkspaceDocumentMutationOutcome.OperationInProgress;
	}

	private static DocumentReplacementResult CreateCompletedResult(
		IReadOnlyList<DocumentReplacementChange> documentChanges,
		IReadOnlyList<DocumentReplacementTargetResult> targetResults,
		WorkspaceOptions options
	) => DocumentReplacementResult.Completed(targetResults, new DocumentReplacementChangeSet(documentChanges), options);

	// Nothing was applied when no failure occurred and no change was recorded: the processed targets were removed by the no-op check or
	// already held the requested content, so there is no partial application to report.
	private static DocumentReplacementResult CreateCanceledOrPartialResult(
		List<DocumentReplacementChange> documentChanges,
		IReadOnlyList<DocumentReplacementTargetResult> targetResults,
		IReadOnlyList<string> unknownDocumentIds,
		WorkspaceOperationFailure? firstFailure,
		WorkspaceOptions options,
		OperationCanceledException? cancellation = null
	)
	{
		if (firstFailure is null && documentChanges.Count == 0)
			return CreateCanceledResult(targetResults, options, cancellation);

		return CreatePartialResult(
			documentChanges,
			targetResults,
			unknownDocumentIds,

			failure: firstFailure
				?? new WorkspaceOperationFailure(
					WorkspaceOperationFailureCodes.Canceled,
					"The application was canceled before the target was applied.",
					cancellation
				),

			options
		);
	}

	private static DocumentReplacementResult CreateCanceledResult(
		IReadOnlyList<DocumentReplacementTargetResult> targetResults,
		WorkspaceOptions options,
		OperationCanceledException? cancellation = null
	)
	{
		return DocumentReplacementResult.Canceled(
			targetResults,

			new WorkspaceOperationFailure(
				WorkspaceOperationFailureCodes.Canceled,
				"The application was canceled before any target changed a document.",
				cancellation
			),

			options
		);
	}

	private static DocumentReplacementResult CreatePartialResult(
		IReadOnlyList<DocumentReplacementChange> documentChanges,
		IReadOnlyList<DocumentReplacementTargetResult> targetResults,
		IReadOnlyList<string> unknownDocumentIds,
		WorkspaceOperationFailure failure,
		WorkspaceOptions options
	)
	{
		return DocumentReplacementResult.PartiallyApplied(
			targetResults,
			unknownDocumentIds,
			failure,
			changeSet: new DocumentReplacementChangeSet(documentChanges),
			options
		);
	}
}

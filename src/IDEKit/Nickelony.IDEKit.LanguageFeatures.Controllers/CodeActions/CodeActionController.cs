using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nickelony.IDEKit.Core.Callbacks;
using Nickelony.IDEKit.Core.LineStatus;
using Nickelony.IDEKit.Core.Notifications;
using Nickelony.IDEKit.Core.Requests;
using Nickelony.IDEKit.Core.Threading;
using Nickelony.IDEKit.LanguageFeatures.CodeActions;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.CodeActions;

/// <summary>
/// Coordinates code-action requests for an editor: it watches the context its selection source reports, debounces refresh requests,
/// and publishes the available actions together with the line the indicator belongs to.
/// </summary>
/// <remarks>
/// <para>
/// The controller owns the request policy and the provider answers it; the host owns the mapping from an editor
/// context to a request. For every settled context the controller builds a <see cref="CodeActionSelection"/> and asks the host's request
/// builder which range (if any) to ask about; a <see langword="null"/> request vetoes the context. The context carries a lazily materialized snapshot
/// of the document text, so a settled request whose builder does not read the text does not copy the whole document; a host that reads it pays
/// the copy once per settled request, and can raise <see cref="CodeActionControllerOptions.RequestDebounceDelay"/> to reduce how often that happens.
/// </para>
/// <para>
/// A completed result is published only when no newer context change invalidated it, and the indicator
/// belongs to the line the caret was on when the result was published. The dispatched publication step compares
/// a generation that every request, context change, document replacement, cancellation, and invalidation advances, and it checks that the controller
/// is not disposed, so a result or a failure that one of them made stale never reaches the published state, whichever thread the provider
/// continuation ran on. <include file="../../../../shared/docs/ThreadAffinity.xml" path="doc/members/member[@name='ThreadAffinity.Remarks']/*"/>
/// </para>
/// <para>
/// The controller presents nothing itself. A presenter - an indicator margin and a menu, a status-bar light bulb,
/// or a popup the host builds from <see cref="Actions"/> - refreshes when <see cref="Changed"/> is raised, reads <see cref="Actions"/>,
/// <see cref="HasActions"/> and <see cref="GetMarkedLineNumbers"/>, and runs a chosen action through <see cref="ExecuteActionAsync"/>.
/// </para>
/// <para>
/// The host calls <see cref="RequestAsync"/> when state the request depends on changed outside the editor -
/// for example after the provider reported new diagnostics, because diagnostic-based quick fixes can become available without a caret move.
/// Editor context changes (caret, selection, and document) schedule a debounced request on their own.
/// </para>
/// <para>
/// Actions are snapshots: the user may invoke an action after the document changed. The controller refuses an action when the editor's text differs
/// from the text the action was requested for, as <see cref="ExecuteActionAsync"/> says, so a host's execute hook never receives a stale action;
/// the host's edit pipeline stays the last guard for text that changes between the hook's start and its edit.
/// </para>
/// </remarks>
public sealed partial class CodeActionController : IDisposable, IChangeNotificationSource, ILineStatusSource
{
	[LoggerMessage(EventId = 1030, EventName = "CodeActionRequestFailed", Level = LogLevel.Warning, Message = "The code-action request failed.")]
	private static partial void LogRequestFailed(ILogger logger, Exception? exception);

	[LoggerMessage(
		EventId = 1031,
		EventName = "CodeActionHostCallbackFailed",
		Level = LogLevel.Warning,
		Message = "A code-action host callback failed."
	)]
	private static partial void LogHostCallbackFailed(ILogger logger, Exception? exception);

	[LoggerMessage(
		EventId = 1032,
		EventName = "CodeActionStale",
		Level = LogLevel.Information,
		Message = "The code action '{Title}' was not applied because the text it was offered for has changed."
	)]
	private static partial void LogStale(ILogger logger, string title);

	[LoggerMessage(
		EventId = 1033,
		EventName = "CodeActionNotResolved",
		Level = LogLevel.Warning,
		Message = "The code action '{Title}' was not applied because the provider could not resolve it into an edit or a command."
	)]
	private static partial void LogNotResolved(ILogger logger, string title);

	[LoggerMessage(
		EventId = 1034,
		EventName = "CodeActionCommandNotRun",
		Level = LogLevel.Warning,
		Message = "The provider did not run the command '{Command}' of the code action '{Title}'."
	)]
	private static partial void LogCommandNotRun(ILogger logger, string command, string title);

	private readonly ICodeActionSelectionSource _selectionSource;
	private readonly IUiDispatcher _dispatcher;
	private readonly ICodeActionProvider _provider;
	private readonly Func<CodeActionSelection, CodeActionRequest?> _buildRequest;
	private readonly Func<CodeAction, CancellationToken, Task> _executeActionAsync;
	private readonly ILogger _logger;
	private readonly DispatcherDebouncer _debouncer;
	private readonly RequestLifetime _lifetime = new();
	private readonly List<int> _markedLines = [];
	private readonly IReadOnlyList<int> _markedLinesView;

	// The actions whose execution is still running, so a second call for one of them does nothing.
	private readonly HashSet<CodeAction> _executing = [];

	private int _indicatorLineNumber;

	// The selection the published actions were requested for, whose snapshot text the actions address.
	private CodeActionSelection? _publishedSelection;

	/// <param name="selectionSource">
	/// The source of the editor context. The controller owns it from the moment this constructor returns and disposes it in <see cref="Dispose"/>;
	/// when the constructor throws, the caller still owns the source.
	/// </param>
	/// <param name="provider">The provider that supplies the code actions for a request.</param>
	/// <param name="hooks">The host callbacks.</param>
	/// <param name="dispatcher">The dispatcher the debounce timer, the reads of the source, and the publication steps run on.</param>
	/// <param name="options">The controller options, or <see langword="null"/> to use the defaults.</param>
	/// <param name="logger">An optional logger for request failures.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="selectionSource"/>, <paramref name="provider"/>, or <paramref name="dispatcher"/> is <see langword="null"/>, or a required
	/// hook is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">The caller is not on the thread of <paramref name="dispatcher"/>.</exception>
	public CodeActionController(
		ICodeActionSelectionSource selectionSource,
		ICodeActionProvider provider,
		CodeActionControllerHooks hooks,
		IUiDispatcher dispatcher,
		CodeActionControllerOptions? options = null,
		ILogger? logger = null
	)
	{
		ArgumentNullException.ThrowIfNull(selectionSource);
		ArgumentNullException.ThrowIfNull(provider);
		ArgumentNullException.ThrowIfNull(dispatcher);
		ArgumentNullException.ThrowIfNull(hooks.BuildRequest);
		ArgumentNullException.ThrowIfNull(hooks.ExecuteActionAsync);

		// The controller reads the source and publishes its state on the dispatcher's thread, so creating it elsewhere is a caller error.
		dispatcher.VerifyAccess();

		CodeActionControllerOptions resolvedOptions = options ?? CodeActionControllerOptions.Default;

		_selectionSource = selectionSource;
		_dispatcher = dispatcher;
		_provider = provider;
		_buildRequest = hooks.BuildRequest;
		_executeActionAsync = hooks.ExecuteActionAsync;
		_logger = logger ?? NullLogger.Instance;

		_debouncer = new DispatcherDebouncer(resolvedOptions.RequestDebounceDelay, _dispatcher);
		_markedLinesView = _markedLines.AsReadOnly();

		selectionSource.Changed += HandleContextChanged;
		selectionSource.DocumentReplaced += HandleDocumentReplaced;
	}

	/// <summary>The actions published for the current context, in provider order, or an empty list when none are available.</summary>
	/// <remarks>
	/// The returned list is the controller's read-only snapshot. It is replaced wholesale whenever a request publishes a new result, so a caller
	/// reads it after <see cref="Changed"/> (or immediately before opening a menu) instead of caching it across context changes.
	/// </remarks>
	public IReadOnlyList<CodeAction> Actions { get; private set; } = [];

	public bool HasActions { get; private set; }

	/// <inheritdoc/>
	public event EventHandler? Changed;

	/// <summary>Requests code actions for the current context immediately, superseding any scheduled or outstanding request.</summary>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>
	/// A task that completes after the request concluded. Failures are contained: they are logged and clear the published actions instead of faulting
	/// the task.
	/// </returns>
	/// <exception cref="InvalidOperationException">The caller is not on the controller's dispatcher thread; the returned task faults.</exception>
	/// <remarks>
	/// The request discards a result that was still in flight when it started, even when the request itself concludes without admitting a replacement
	/// (a vetoed context or a failing request builder), so a superseded request can never publish after this call.
	/// </remarks>
	public async Task RequestAsync(CancellationToken cancellationToken = default)
	{
		if (_lifetime.IsDisposed)
			return;

		_dispatcher.VerifyAccess();

		_debouncer.Cancel();
		await RequestActionsAsync(cancellationToken).ConfigureAwait(true);
	}

	/// <summary>
	/// Runs an action: the provider resolves it when it has no edit, the host's execute hook applies it, and the provider then runs its command.
	/// </summary>
	/// <param name="action">The action to run, normally one of <see cref="Actions"/>.</param>
	/// <returns>
	/// A task that completes with the outcome after the action ran or was refused. A failure of the hook, the resolution or the command is contained
	/// and logged instead of faulting the task, and the command does not run after a failed hook.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>; the returned task faults.</exception>
	/// <exception cref="InvalidOperationException">The caller is not on the controller's dispatcher thread; the returned task faults.</exception>
	/// <remarks>
	/// <para>
	/// An action without an edit that carries provider data is resolved through <see cref="ICodeActionResolver"/> first,
	/// once and only for this action; the hook receives the resolved action. A provider that implements <see cref="ICodeActionCommandRunner"/> runs
	/// the action's command after the hook returned, so the command sees the edit applied.
	/// </para>
	/// <para>
	/// Every action addresses the editor text its request was built from. The controller refuses an action when the editor's text
	/// differs from it, and one that a newer publication replaced, before it resolves or applies anything, and again after a resolution returned,
	/// because the user can type while the provider computes the edit. A second call for an action whose first call is still running does nothing.
	/// </para>
	/// </remarks>
	public async Task<CodeActionExecutionOutcome> ExecuteActionAsync(CodeAction action)
	{
		ArgumentNullException.ThrowIfNull(action);

		if (_lifetime.IsDisposed)
			return CodeActionExecutionOutcome.Disposed;

		_dispatcher.VerifyAccess();

		if (!_executing.Add(action))
			return CodeActionExecutionOutcome.AlreadyRunning;

		try
		{
			return await ExecuteCoreAsync(action).ConfigureAwait(true);
		}
		finally
		{
			_executing.Remove(action);
		}
	}

	/// <summary>Cancels the scheduled debounced request, if any, so it never runs.</summary>
	public void CancelScheduledRequest()
	{
		if (_lifetime.IsDisposed)
			return;

		_debouncer.Cancel();
	}

	/// <summary>
	/// Cancels the in-flight request, if any, so a cooperative host can stop early; the canceled request's result is rejected
	/// because its cancellation token is canceled, and a result that a newer request replaced is rejected as well.
	/// </summary>
	public void CancelInFlightRequest()
	{
		_lifetime.CancelInFlightRequest();
		_lifetime.AdvanceGeneration();
	}

	/// <summary>Marks outstanding requests as stale so their completed results are discarded without canceling the work.</summary>
	public void InvalidateRequests()
	{
		_lifetime.InvalidateRequests();
		_lifetime.AdvanceGeneration();
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		if (_lifetime.IsDisposed)
			return;

		_lifetime.Dispose();

		_selectionSource.Changed -= HandleContextChanged;
		_selectionSource.DocumentReplaced -= HandleDocumentReplaced;

		_debouncer.Dispose();

		Actions = [];
		HasActions = false;
		_publishedSelection = null;
		_markedLines.Clear();

		try
		{
			// A presenter that is still attached refreshes without the indicator; a detached one has already unsubscribed from the event.
			Changed?.Invoke(this, EventArgs.Empty);
		}
		finally
		{
			// The source is disposed last, so a handler that throws still releases it and a source that throws leaves the controller torn down.
			_selectionSource.Dispose();
		}
	}

	/// <summary>
	/// Returns the one-based document lines an indicator must mark: the line the caret was on when the actions were published,
	/// while actions are available, otherwise nothing.
	/// </summary>
	/// <returns>The cached indicator lines; the returned list is a read-only view of the controller's own state.</returns>
	public IReadOnlyList<int> GetMarkedLineNumbers() => _markedLinesView;

	private void HandleContextChanged(object? sender, EventArgs e) => HandleContextChanged();

	private void HandleDocumentReplaced(object? sender, EventArgs e)
	{
		if (_lifetime.IsDisposed)
			return;

		_dispatcher.VerifyAccess();

		ClearState();
		HandleContextChanged();
	}

	private void HandleContextChanged()
	{
		if (_lifetime.IsDisposed)
			return;

		// The handler reads the source and changes the published state, which only the dispatcher's thread may do.
		_dispatcher.VerifyAccess();

		// Pending work belongs to the previous context: a cooperative host is asked to stop early and
		// the completed result cannot be published after this point.
		_lifetime.CancelAndInvalidate();
		_lifetime.AdvanceGeneration();

		// A published indicator whose line has moved off the caret line is stale immediately;
		// movement inside the same line keeps it until the refreshed request replaces or clears it.
		if (HasActions && (_selectionSource.CaretLineNumber ?? 0) != _indicatorLineNumber)
			ClearState();

		ScheduleRequest();
	}

	private void ScheduleRequest() => _debouncer.Arm(() => _ = RequestActionsAsync(CancellationToken.None));

	private async Task RequestActionsAsync(CancellationToken cancellationToken)
	{
		if (_lifetime.IsDisposed)
			return;

		// The attempt supersedes any request that is still outstanding before the host request builder can
		// veto or fail: a request that stayed current could otherwise publish after this attempt already
		// replaced or cleared the state. The gate supersedes on admission, so claiming the slot up
		// front also covers the paths that conclude without admitting a replacement request.
		_lifetime.CancelAndInvalidate();

		// The attempt starts a new generation before it reads the context, so a context change or a document replacement
		// that lands while the request builder runs, or while the provider works, makes this attempt stale.
		int generation = _lifetime.AdvanceGeneration();

		bool failureReported = false;
		bool requestAdmitted = false;

		try
		{
			// The capture validates the caret and selection and can throw, so it runs inside the contained region: on the
			// fire-and-forget debounced path a validation failure would otherwise escape as an unobserved fault instead of
			// clearing the state and logging.
			CodeActionSelection? context = _selectionSource.Capture();

			if (context is null)
			{
				ClearIfCurrent(generation);
				return;
			}

			if (
				!CallbackContainment.TryRun(
					callback: () => _buildRequest(context),
					reportFault: ReportHostCallbackFault,
					result: out CodeActionRequest? request
				)
			)
			{
				ClearIfCurrent(generation);
				return;
			}

			if (request is not CodeActionRequest providerRequest)
			{
				ClearIfCurrent(generation);
				return;
			}

			// A context change or a document replacement during the capture or the request builder made this attempt stale. The change
			// already scheduled its own refresh, and admitting the attempt would only supersede a newer request.
			if (!_lifetime.IsCurrent(generation))
				return;

			// The gate cancels the previous request when a newer one starts and discards its
			// superseded results. Its publication callbacks run on the caller's captured synchronization
			// context only when one exists, so the currency check and the publish are one step that marshals
			// to the dispatcher explicitly: hosts that pump a dispatcher manually and tests get the same
			// guarantee. A failure is reported through the failure callback, which the gate invokes
			// only while this request owns the slot, so a superseded failure cannot clear the newer state.
			requestAdmitted = true;

			await _lifetime
				.RunAsync(
					state: providerRequest,
					computeAsync: _provider.GetCodeActionsAsync,

					apply: actions =>
						_dispatcher.Invoke(() =>
						{
							if (_lifetime.IsCurrent(generation))
								PublishActions(actions, context);
						}),

					onFailure: exception =>
					{
						failureReported = true;
						LogRequestFailed(_logger, exception);

						_dispatcher.Invoke(() => ClearIfCurrent(generation));
					},

					cancellationToken
				)
				.ConfigureAwait(true);
		}
		catch (OperationCanceledException)
		{
			// A superseded request reports cancellation, not a failure; the superseding context has
			// already scheduled its own refresh.
		}
		catch (Exception exception)
		{
			if (_lifetime.IsDisposed || failureReported)
				return;

			// A failure of the request itself was either reported by the failure callback (which also
			// cleared the state) or belongs to a superseded request, which only leaves its log entry.
			LogRequestFailed(_logger, exception);

			// A failure raised before the request was admitted (a context-validation failure) is not
			// routed through the gate's failure callback, which reports only admitted requests; the
			// previous snapshot would otherwise stay published. Clear it through the same currency guard
			// the callback uses. A failure raised after admission belongs to an admitted request and only
			// leaves its log entry.
			if (requestAdmitted)
				return;

			_dispatcher.Invoke(() => ClearIfCurrent(generation));
		}
	}

	private async Task<CodeActionExecutionOutcome> ExecuteCoreAsync(CodeAction action)
	{
		CancellationToken cancellationToken = _lifetime.LifetimeToken;

		// The text is read before anything awaits, so a publication that replaces the actions while the provider works cannot change it.
		if (OfferedTextOf(action) is not { } offeredText || !EditorHolds(offeredText))
		{
			LogStale(_logger, action.Title);
			return CodeActionExecutionOutcome.Stale;
		}

		ICodeActionResolver? resolver = action.Edit is null && action.Data is not null ? _provider as ICodeActionResolver : null;
		CodeAction resolved = action;

		if (resolver is not null)
		{
			try
			{
				resolved = await resolver.ResolveAsync(action, cancellationToken).ConfigureAwait(true);
			}
			catch (OperationCanceledException) when (_lifetime.IsDisposed)
			{
				return CodeActionExecutionOutcome.Disposed;
			}
			catch (Exception exception)
			{
				LogRequestFailed(_logger, exception);
				return CodeActionExecutionOutcome.NotResolved;
			}

			if (_lifetime.IsDisposed)
				return CodeActionExecutionOutcome.Disposed;

			// The user can type while the provider computes the edit, and the edit addresses the text the action was offered for.
			if (!EditorHolds(offeredText))
			{
				LogStale(_logger, action.Title);
				return CodeActionExecutionOutcome.Stale;
			}

			if (resolved.Edit is null && resolved.Command is null)
			{
				LogNotResolved(_logger, action.Title);
				return CodeActionExecutionOutcome.NotResolved;
			}
		}

		try
		{
			await _executeActionAsync(resolved, cancellationToken).ConfigureAwait(true);
		}
		catch (OperationCanceledException) when (_lifetime.IsDisposed)
		{
			return CodeActionExecutionOutcome.Disposed;
		}
		catch (Exception exception)
		{
			LogHostCallbackFailed(_logger, exception);
			return CodeActionExecutionOutcome.HookFailed;
		}

		if (resolved.Command is not { } command || _provider is not ICodeActionCommandRunner runner)
			return CodeActionExecutionOutcome.Completed;

		if (_lifetime.IsDisposed)
			return CodeActionExecutionOutcome.Disposed;

		try
		{
			if (await runner.TryRunAsync(command, cancellationToken).ConfigureAwait(true))
				return CodeActionExecutionOutcome.Completed;

			LogCommandNotRun(_logger, command.Name, action.Title);
			return CodeActionExecutionOutcome.CommandFailed;
		}
		catch (OperationCanceledException) when (_lifetime.IsDisposed)
		{
			return CodeActionExecutionOutcome.Disposed;
		}
		catch (Exception exception)
		{
			LogRequestFailed(_logger, exception);
			return CodeActionExecutionOutcome.CommandFailed;
		}
	}

	private void ReportHostCallbackFault(Exception exception) => LogHostCallbackFailed(_logger, exception);

	// An action addresses the text of the selection its request was built from, and only the published set still has such a selection behind it.
	private string? OfferedTextOf(CodeAction action) => _publishedSelection is { } selection && Actions.Contains(action) ? selection.GetText() : null;

	private bool EditorHolds(string offeredText)
	{
		try
		{
			return _selectionSource.Capture() is { } current && string.Equals(current.GetText(), offeredText, StringComparison.Ordinal);
		}
		catch (Exception exception)
		{
			// A source that cannot report its text cannot vouch for the action, so the action is refused.
			LogHostCallbackFailed(_logger, exception);
			return false;
		}
	}

	// An attempt that a newer request, a context change, or a document replacement made stale changes nothing: whatever made it stale owns the state.
	private void ClearIfCurrent(int generation)
	{
		if (_lifetime.IsCurrent(generation))
			ClearState();
	}

	private void PublishActions(IReadOnlyList<CodeAction>? actions, CodeActionSelection selection)
	{
		// A misbehaving host delegate must not corrupt the published state or crash the dispatch pass.
		if (actions is null)
		{
			ClearState();
			return;
		}

		List<CodeAction> usable = [];

		for (int index = 0; index < actions.Count; index++)
		{
			CodeAction? action = actions[index];

			if (action is not null)
				usable.Add(action);
		}

		if (usable.Count == 0)
		{
			ClearState();
			return;
		}

		// The published snapshot is wrapped read-only so a caller that downcasts it cannot mutate the controller's state, and each publish
		// creates a new wrapper, so a presenter tells a replaced set from the one it shows by reference.
		Actions = usable.AsReadOnly();
		HasActions = true;
		_publishedSelection = selection;
		_indicatorLineNumber = _selectionSource.CaretLineNumber ?? 0;

		_markedLines.Clear();

		if (_indicatorLineNumber > 0)
			_markedLines.Add(_indicatorLineNumber);

		Changed?.Invoke(this, EventArgs.Empty);
	}

	private void ClearState()
	{
		bool raisedChange = HasActions || _markedLines.Count > 0;

		Actions = [];
		HasActions = false;
		_publishedSelection = null;
		_indicatorLineNumber = 0;
		_markedLines.Clear();

		if (raisedChange)
			Changed?.Invoke(this, EventArgs.Empty);
	}
}

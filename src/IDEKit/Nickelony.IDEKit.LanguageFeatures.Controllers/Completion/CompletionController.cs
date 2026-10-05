using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.Core.Threading;
using Nickelony.IDEKit.LanguageFeatures.Completion;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

/// <summary>
/// Coordinates the completion requests of one document in one editor: their triggers and scheduling, the decision of the newest request,
/// the move of a late answer onto the word at the caret, and the end of the session when the editor shows another document.
/// </summary>
/// <remarks>
/// <para>
/// The controller reads the editor through the delegate
/// it was given and shows decisions through an <see cref="ICompletionPresenter"/>, so it names no engine type. It never disposes the presenter:
/// the caller keeps it, and disposes it after the controller. A binding supplies the engine through a per-engine factory, which also forwards
/// the typed characters and the document replacement of its editor to <see cref="HandleCharacterTyped"/> and <see cref="HandleDocumentReplaced"/>.
/// <include file="../../../../shared/docs/ThreadAffinity.xml" path="doc/members/member[@name='ThreadAffinity.Remarks']/*"/>
/// </para>
/// <para>
/// <see cref="RequestAsync"/> admits each request as the newest one, so a newer request supersedes the request in flight, and the debounce timers
/// run on the dispatcher. A typed character opens completion by the <see cref="CompletionControllerOptions.Triggers"/> rule: the controller reads
/// the scope from the scope hook and the trigger characters from a provider that implements <see cref="ICompletionTriggerCharacters"/>, and a request
/// for a trigger character cancels the request that a word character scheduled before it.
/// </para>
/// <para>
/// One controller serves one document. After <see cref="HandleDocumentReplaced"/>, the controller requests nothing, so a host creates a new
/// one for the next document.
/// </para>
/// </remarks>
public sealed partial class CompletionController : IDisposable
{
	[LoggerMessage(EventId = 1003, EventName = "CompletionTriggerFailed", Level = LogLevel.Warning, Message = "A completion trigger failed.")]
	private static partial void LogCompletionTriggerFailed(ILogger logger, Exception exception);

	private readonly Func<CompletionEditorContext?> _captureContext;
	private readonly string _documentId;
	private readonly ICompletionProvider _provider;
	private readonly ICompletionPresenter _presenter;
	private readonly IUiDispatcher _dispatcher;
	private readonly Func<int, CompletionScopes>? _scopeAt;
	private readonly CompletionControllerOptions _options;
	private readonly CompletionSessionKernel _kernel;
	private readonly ILogger _logger;
	private readonly CompletionRequestGate _gate;

	// The snapshot the newest request was made against. The gate applies the decision of the newest request only, so the decision it hands to
	// ApplyRequestedDirective belongs to this snapshot.
	private ITextSnapshot? _requestSnapshot;

	// Set once the editor replaced its document: a request must never carry another document's text under the fixed identifier.
	private bool _isDocumentReplaced;

	private bool _isRequestScheduled;

	/// <param name="captureContext">
	/// Captures the document text and the caret of the editor on the dispatcher's thread, or returns <see langword="null"/> when the editor
	/// has no document. The controller calls it when a request starts and again when the answer arrives, to move the answer onto the word
	/// at the caret then.
	/// </param>
	/// <param name="documentId">
	/// The identifier of the edited document, which every request carries. One controller serves one document: when the editor replaces its document,
	/// the host forwards the replacement to <see cref="HandleDocumentReplaced"/>, and the controller stops requesting.
	/// </param>
	/// <param name="provider">
	/// The provider that answers the requests; when it implements <see cref="ICompletionTriggerCharacters"/>, its characters open completion
	/// when typed.
	/// </param>
	/// <param name="presenter">
	/// The presenter that shows the decisions. The controller never disposes it: the caller keeps it, and disposes it after the controller.
	/// </param>
	/// <param name="dispatcher">The dispatcher the debounce timers, the reads of the editor and the publication steps run on.</param>
	/// <param name="hooks">The optional host hooks, or <see langword="null"/> when the host needs none.</param>
	/// <param name="options">The controller options, or <see langword="null"/> to use the defaults.</param>
	/// <param name="kernel">
	/// The kernel that turns the provider's items into a decision, or <see langword="null"/> for one with the default word locator and filter.
	/// </param>
	/// <param name="logger">An optional logger for request and trigger failures.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="captureContext"/>, <paramref name="documentId"/>, <paramref name="provider"/>,
	/// <paramref name="presenter"/> or <paramref name="dispatcher"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="documentId"/> is blank.</exception>
	/// <exception cref="InvalidOperationException">The caller is not on the thread of <paramref name="dispatcher"/>.</exception>
	public CompletionController(
		Func<CompletionEditorContext?> captureContext,
		string documentId,
		ICompletionProvider provider,
		ICompletionPresenter presenter,
		IUiDispatcher dispatcher,
		CompletionControllerHooks? hooks = null,
		CompletionControllerOptions? options = null,
		CompletionSessionKernel? kernel = null,
		ILogger? logger = null
	)
	{
		ArgumentNullException.ThrowIfNull(captureContext);
		ArgumentException.ThrowIfNullOrWhiteSpace(documentId);

		ArgumentNullException.ThrowIfNull(provider);
		ArgumentNullException.ThrowIfNull(presenter);
		ArgumentNullException.ThrowIfNull(dispatcher);

		// The controller reads the editor and publishes its state on the dispatcher's thread, so creating it elsewhere is a caller error.
		dispatcher.VerifyAccess();

		_captureContext = captureContext;
		_documentId = documentId;
		_provider = provider;
		_presenter = presenter;
		_dispatcher = dispatcher;
		_scopeAt = hooks?.ScopeAt;
		_options = options ?? CompletionControllerOptions.Default;
		_kernel = kernel ?? new CompletionSessionKernel();
		_logger = logger ?? NullLogger.Instance;

		_gate = new CompletionRequestGate(
			dispatcher,
			applyDecision: ApplyRequestedDirective,
			scheduledRequestAsync: _ => RequestAsync(),
			contextVersionProvider: hooks?.ContextVersionProvider,
			setRequestScheduled: isRequestScheduled => _isRequestScheduled = isRequestScheduled,
			options: _options,
			logger: _logger
		);
	}

	/// <summary>
	/// The presentation state the controller last published: list and tooltip visibility, a scheduled request, and the tooltip content.
	/// </summary>
	/// <remarks>
	/// The list and tooltip members are what the <see cref="ICompletionPresenter"/> reports; the scheduled-request flag is the controller's own.
	/// </remarks>
	public CompletionPresentationState CurrentPresentation =>
		new(
			IsListVisible: _presenter.IsListVisible,
			IsRequestScheduled: _isRequestScheduled,
			IsTooltipVisible: _presenter.IsTooltipVisible,
			TooltipContent: _presenter.TooltipContent
		);

	/// <summary>Indicates whether the controller was disposed, after which it starts no work.</summary>
	public bool IsDisposed => _gate.IsDisposed;

	/// <summary>Schedules a <see cref="RequestAsync"/> at the caret to run after the configured debounce delay.</summary>
	/// <remarks>
	/// Arming again while a request is scheduled restarts the debounce delay with the newest schedule call. The request runs on the dispatcher
	/// and reads the editor when it runs, and a typed word character schedules it by the <see cref="CompletionControllerOptions.Triggers"/> rule.
	/// After the editor replaced its document, the call does nothing.
	/// </remarks>
	public void ScheduleRequest()
	{
		if (_isDocumentReplaced)
			return;

		_gate.ScheduleRequest();
	}

	/// <summary>
	/// Cancels the debounced completion request scheduled by <see cref="ScheduleRequest"/>, if one is pending; the in-flight provider request
	/// is not affected, so use <see cref="CancelInFlightRequest"/> or <see cref="InvalidateRequests"/> for that.
	/// </summary>
	public void CancelScheduledRequest() => _gate.CancelScheduledRequest();

	/// <summary>Cancels the in-flight request's token, if any, so a cooperative provider can stop early.</summary>
	/// <remarks>
	/// The canceled request's decision is discarded because its token is canceled; use <see cref="InvalidateRequests"/> to discard outstanding
	/// decisions without canceling the provider. After disposal the call does nothing.
	/// </remarks>
	public void CancelInFlightRequest() => _gate.CancelInFlightRequest();

	/// <summary>Marks outstanding requests as stale so their decisions are discarded when they complete.</summary>
	/// <remarks>
	/// The in-flight provider call is not canceled; use <see cref="CancelInFlightRequest"/> for that. After disposal the call does nothing.
	/// </remarks>
	public void InvalidateRequests() => _gate.InvalidateRequests();

	/// <summary>
	/// Asks the provider for completion at the caret and shows the kernel's decision: the controller admits the request, rejects a superseded
	/// or canceled result, and hands an accepted one to the presenter.
	/// </summary>
	/// <param name="context">
	/// The reason the request was triggered, or <see langword="null"/> for <see cref="CompletionContext.Invoked"/>; a typed trigger character
	/// passes <see cref="CompletionContext.ForCharacter"/>.
	/// </param>
	/// <returns>
	/// <see langword="true"/> when the request was applied and the presenter opened or refreshed a list;
	/// <see langword="false"/> when the request was superseded, canceled, failed, discarded because the caret left the word, or produced no list,
	/// when the editor has no document, and after the editor replaced its document.
	/// </returns>
	/// <exception cref="InvalidOperationException">The caller is not on the controller's dispatcher thread.</exception>
	/// <remarks>
	/// <para>
	/// The request carries a snapshot of the document and the caret taken when the call starts. Starting a request cancels the previous request's
	/// token and the request <see cref="ScheduleRequest"/> armed, so a scheduled word request never supersedes it later. A result that completes
	/// after a newer request started, after <see cref="CancelInFlightRequest"/> or <see cref="InvalidateRequests"/> was called, or after the host
	/// context version changed, is discarded even when the provider ignored the token.
	/// </para>
	/// <para>
	/// A result that completes after the user typed on within the word is applied to the word at the caret now, so the list
	/// opens filtered by the text typed since the request. A result that completes after the caret left the word is discarded
	/// (<see href="https://github.com/Nickelony/LanguageServer/blob/main/docs/EditorLanguageFeaturesGuide.md#answers-that-arrive-after-typing">answers that arrive after typing</see>).
	/// </para>
	/// <para>
	/// A provider failure is logged as a warning and the task reports <see langword="false"/>, so timer, dispatcher, and event-handler callers
	/// never observe it, and a cancellation reports <see langword="false"/> too. A canceled or failed request leaves an open window
	/// as it is, and so does an answer without items.
	/// </para>
	/// </remarks>
	public Task<bool> RequestAsync(CompletionContext? context = null)
	{
		if (_gate.IsDisposed)
			return Task.FromResult(false);

		_dispatcher.VerifyAccess();

		// The snapshot and the caret are taken now, on the dispatcher's thread, so the provider reads the text the request was made for.
		if (_isDocumentReplaced || _captureContext() is not { } editor)
			return Task.FromResult(false);

		_gate.CancelScheduledRequest();
		_requestSnapshot = editor.Snapshot;

		return _gate.RequestAsync(cancellationToken =>
			_kernel.GetDirectiveAsync(
				documentId: _documentId,
				provider: _provider,
				snapshot: editor.Snapshot,
				caretOffset: editor.CaretOffset,
				context,
				cancellationToken
			)
		);
	}

	/// <summary>
	/// Evaluates a character that was typed into the editor after its input applied, and opens completion when the trigger rule says so.
	/// </summary>
	/// <param name="typed">The typed character.</param>
	/// <param name="caretOffset">The caret offset after the character.</param>
	/// <exception cref="InvalidOperationException">The caller is not on the controller's dispatcher thread.</exception>
	/// <remarks>
	/// After the editor replaced its document and after disposal, the call does nothing, and the scope hook is not asked.
	/// </remarks>
	public void HandleCharacterTyped(char typed, int caretOffset)
	{
		if (_isDocumentReplaced || _gate.IsDisposed)
			return;

		_dispatcher.VerifyAccess();

		// The scope hook and the provider's characters are host code, so a failure of either is logged instead of escaping.
		try
		{
			IReadOnlyList<string> triggerCharacters = (_provider as ICompletionTriggerCharacters)?.TriggerCharacters ?? [];
			CompletionScopes scope = _scopeAt?.Invoke(caretOffset) ?? CompletionScopes.Code;

			switch (CompletionTriggerResolver.Resolve(typed, scope, triggerCharacters, policy: _options.Triggers))
			{
				case CompletionTriggerMode.Scheduled:
					ScheduleRequest();
					break;

				case CompletionTriggerMode.Immediate:
					_ = RequestAsync(CompletionContext.ForCharacter(typed));
					break;
			}
		}
		catch (Exception exception)
		{
			LogCompletionTriggerFailed(_logger, exception);
		}
	}

	/// <summary>Ends the session because the editor started showing another document.</summary>
	/// <exception cref="InvalidOperationException">The caller is not on the controller's dispatcher thread.</exception>
	/// <remarks>
	/// The requests carry the identifier of the document the controller was created for, so the controller cancels the scheduled request
	/// and the one in flight, discards the work that read the earlier document, dismisses the presenter, and requests nothing more. A second call
	/// and a call after disposal do nothing.
	/// </remarks>
	public void HandleDocumentReplaced()
	{
		if (_isDocumentReplaced || _gate.IsDisposed)
			return;

		_dispatcher.VerifyAccess();

		_isDocumentReplaced = true;
		_gate.CancelScheduledRequest();
		_gate.CancelInFlightRequest();
		_presenter.Dismiss();
	}

	/// <summary>Stops completion scheduling and cancels the request in flight.</summary>
	/// <remarks>
	/// Disposal is synchronous: the debounce timers stop, the scheduled-request flag clears, an in-flight request
	/// token is canceled and its decision is discarded. It does not touch the presenter, which the caller keeps and disposes itself. After disposal,
	/// <see cref="RequestAsync"/> yields <see langword="false"/> and the other members start no work.
	/// </remarks>
	public void Dispose()
	{
		if (_gate.IsDisposed)
			return;

		// The gate reports the cleared flag only while it is live, so the flag is cleared before the gate stops.
		_gate.CancelScheduledRequest();
		_gate.Dispose();
	}

	// An answer can arrive after the user typed on, so it is moved onto the word at the caret now, or discarded when the caret left that word.
	private bool ApplyRequestedDirective(CompletionSessionDirective decision)
	{
		if (_requestSnapshot is not { } requestSnapshot || _captureContext() is not { } editor)
			return _presenter.ApplyDirective(decision);

		return _kernel.TryRetarget(
				directive: decision,
				requestSnapshot,
				snapshot: editor.Snapshot,
				caretOffset: editor.CaretOffset,
				out CompletionSessionDirective retargeted
			) && _presenter.ApplyDirective(retargeted);
	}
}

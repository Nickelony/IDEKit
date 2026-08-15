#if AVALONIAEDIT
using Avalonia;
using Avalonia.Controls;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using Nickelony.IDEKit.AvaloniaEdit.Documents;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Requests;
#else
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.Documents;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Requests;
using System.Windows;
#endif
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nickelony.IDEKit.Core.Notifications;
using Nickelony.IDEKit.Infrastructure;
using Nickelony.IDEKit.IntelliSense.CodeActions;

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.CodeActions;
#else
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.CodeActions;
#endif

/// <summary>
/// Coordinates code-action requests for a text area: it watches the caret, selection, and document,
/// debounces refresh requests through the host hooks, publishes the available actions to the margin
/// indicator (<see cref="Margin"/>), and presents them in a skinned drop-down menu.
/// </summary>
/// <remarks>
/// <para>
/// The controller owns the request policy; the host owns the language-server mapping. For every
/// settled context the controller builds a <see cref="TextCodeActionContext"/> and asks the host's
/// request builder which range (if any) to ask about; a <see langword="null"/> request vetoes the
/// context. The context carries a lazily materialized snapshot of the document text, so a settled
/// request whose builder does not read the text does not copy the whole document; a host that reads
/// it pays the copy once per settled request, and can raise
/// <see cref="TextCodeActionControllerOptions.RequestDebounceDelay"/> to reduce how often that happens.
/// A completed result is published only when no newer context change invalidated it, and the indicator
/// is rendered on the line the caret is on when the result was published.
/// </para>
/// <para>
/// The controller must be created and used on the thread that owns the text area, because it touches
/// the text area's document and dispatcher and hosts the menu directly. Hook continuations are
/// marshalled back to that thread.
/// </para>
/// <para>
/// The host calls <see cref="RefreshAsync"/> when state the request depends on changed outside the
/// editor - for example after the provider reported new diagnostics, because diagnostic-based quick
/// fixes can become available without a caret move. Editor context changes (caret, selection, and
/// document) schedule a debounced request on their own.
/// </para>
/// <para>
/// Actions are snapshots: the user may invoke an action after the document changed. A host must apply
/// actions through its version-validated edit pipeline, so a stale action is rejected instead of
/// corrupting the document.
/// </para>
/// <para>
/// The menu presents the published snapshot: it is closed whenever a newer request replaces or clears
/// the action set, so its items always belong to the current snapshot.
/// </para>
/// </remarks>
public sealed partial class TextCodeActionController : IDisposable, IChangeNotificationSource
{
	// Code actions use package log event ids 1030 and 1031 (see the README for the canonical table).
	[LoggerMessage(
		EventId = 1030,
		EventName = "CodeActionRequestFailed",
		Level = LogLevel.Warning,
		Message = "The code-action request failed."
	)]
	private static partial void LogRequestFailed(ILogger logger, Exception? exception);

	[LoggerMessage(
		EventId = 1031,
		EventName = "CodeActionHostCallbackFailed",
		Level = LogLevel.Warning,
		Message = "A code-action host callback failed."
	)]
	private static partial void LogHostCallbackFailed(ILogger logger, Exception? exception);

	private readonly TextArea _textArea;
	private readonly Func<TextCodeActionContext, TextCodeActionRequest?> _buildRequest;
	private readonly Func<TextCodeActionRequest, CancellationToken, Task<IReadOnlyList<TextCodeActionItem>>> _requestCodeActionsAsync;
	private readonly Func<TextCodeActionItem, Task> _executeActionAsync;
	private readonly ILogger _logger;
	private readonly DispatcherDebouncer _debouncer;
	private readonly RequestLifetime _lifetime = new();
	private readonly TextCodeActionMenuOptions _menuOptions;
	private readonly TextCodeActionMenuPresenter _menu;
	private readonly List<int> _indicatorLines = [];

	private TextDocument? _document;
	private IReadOnlyList<TextCodeActionItem> _actions = [];
	private int _indicatorLineNumber;
	private bool _hasActions;

	/// <summary>
	/// Initializes a new instance of the <see cref="TextCodeActionController"/> class.
	/// </summary>
	/// <param name="textArea">The text area the controller serves.</param>
	/// <param name="presentation">The menu skin and menu presentation options.</param>
	/// <param name="hooks">The host callbacks; the hook group documents which members are required.</param>
	/// <param name="options">The controller options, or <see langword="null"/> to use the defaults.</param>
	/// <param name="logger">An optional logger for request failures.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textArea"/>, <paramref name="presentation"/>, <paramref name="hooks"/>, or a
	/// required hook is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// The caller is not on the thread that owns <paramref name="textArea"/>.
	/// </exception>
	public TextCodeActionController(
		TextArea textArea,
		TextCodeActionPresentation presentation,
		TextCodeActionControllerHooks hooks,
		TextCodeActionControllerOptions? options = null,
		ILogger? logger = null)
	{
		ArgumentNullException.ThrowIfNull(textArea);
		ArgumentNullException.ThrowIfNull(presentation);
		ArgumentNullException.ThrowIfNull(hooks);
		ArgumentNullException.ThrowIfNull(hooks.BuildRequest);
		ArgumentNullException.ThrowIfNull(hooks.RequestCodeActionsAsync);
		ArgumentNullException.ThrowIfNull(hooks.ExecuteActionAsync);

		// The controller touches the text area's document and its dispatcher and hosts the menu directly,
		// so creating it off the text area's thread is a caller error.
		textArea.Dispatcher.VerifyAccess();

		TextCodeActionControllerOptions resolvedOptions = options ?? TextCodeActionControllerOptions.Default;

		_textArea = textArea;
		_buildRequest = hooks.BuildRequest;
		_requestCodeActionsAsync = hooks.RequestCodeActionsAsync;
		_executeActionAsync = hooks.ExecuteActionAsync;
		_logger = logger ?? NullLogger.Instance;

		_menuOptions = presentation.MenuOptions;
		_debouncer = new DispatcherDebouncer(resolvedOptions.RequestDebounceDelay, textArea.Dispatcher);
		_menu = new TextCodeActionMenuPresenter(textArea, presentation, hooks.ConfigureMenu, hooks.ConfigureMenuItem);
		Margin = new TextCodeActionMargin(this);

		textArea.Caret.PositionChanged += HandleContextChangedEvent;
		textArea.SelectionChanged += HandleContextChangedEvent;
		textArea.DocumentChanged += HandleDocumentChanged;
		AttachDocument(textArea.Document);
	}

	/// <summary>
	/// Gets the margin that renders the indicator for the current context.
	/// </summary>
	/// <remarks>
	/// Add the margin to the text area's left margins to show the indicator; the margin is driven by
	/// this controller and repaints when actions become available or disappear. Disposing the
	/// controller clears the indicator but does not remove the margin from the text area - remove it
	/// yourself when the editor is torn down.
	/// </remarks>
	public TextCodeActionMargin Margin { get; }

	/// <summary>
	/// Gets the actions published for the current context, in provider order, or an empty list when none are
	/// available.
	/// </summary>
	/// <remarks>
	/// The returned list is the controller's read-only snapshot. It is replaced wholesale whenever a request
	/// publishes a new result, so read it after <see cref="Changed"/> (or immediately before opening the menu)
	/// instead of caching it across context changes. Actions are snapshots: apply them through a
	/// version-validated edit pipeline, like the execute hook receives them.
	/// </remarks>
	public IReadOnlyList<TextCodeActionItem> Actions => _actions;

	/// <summary>
	/// Gets a value indicating whether actions are available for the current context.
	/// </summary>
	public bool HasActions => _hasActions;

	/// <summary>
	/// Gets a value indicating whether the actions menu is open.
	/// </summary>
	public bool IsActionsOpen => _menu.IsOpen;

	/// <inheritdoc/>
	public event EventHandler? Changed;

	/// <summary>
	/// Requests code actions for the current context immediately, superseding any scheduled or outstanding
	/// request.
	/// </summary>
	/// <remarks>
	/// The refresh discards a result that was still in flight when it started, even when the refresh itself
	/// concludes without admitting a replacement request (a vetoed context or a failing state builder), so a
	/// superseded request can never publish after this call.
	/// </remarks>
	/// <returns>
	/// A task that completes after the request concluded. Failures are contained: they are logged and
	/// clear the indicator instead of faulting the task.
	/// </returns>
	public async Task RefreshAsync()
	{
		if (_lifetime.IsDisposed)
			return;

		_debouncer.Cancel();
		await RequestActionsAsync().ConfigureAwait(true);
	}

	/// <summary>
	/// Opens the actions menu for the current context, anchored at the indicator line, when actions are available.
	/// </summary>
	/// <returns>
	/// <see langword="true"/> when the menu opened; otherwise, <see langword="false"/> because no
	/// actions are available or the editor is not connected to a presentation source.
	/// </returns>
	public bool TryOpenActions()
	{
		// The anchor is computed from the text-area visual tree, so the connectivity refusal must run
		// before it: a disconnected editor has no presentation source to translate against.
		if (!CanOpenMenu())
			return false;

		// Without actions there is nothing to open, and GetIndicatorAnchor walks the visual tree, so the
		// no-actions refusal runs before the anchor is computed.
		if (!_hasActions)
			return false;

		return TryOpenActions(_indicatorLineNumber, GetIndicatorAnchor());
	}

	/// <summary>
	/// Opens the actions menu for the specified document line, anchored at the specified position, when the
	/// current context's actions are available for that line.
	/// </summary>
	/// <remarks>
	/// This is the anchoring seam for a host-owned indicator: a host that draws its own code-action indicator
	/// - or handles its own trigger gesture with a known anchor - opens the library menu at its own click
	/// position instead of the caret by passing the line the indicator belongs to and a position in the text
	/// area's coordinate space. The menu itself stays library-owned: its chrome and item styling are customized
	/// through <see cref="TextCodeActionControllerHooks.ConfigureMenu"/> and
	/// <see cref="TextCodeActionControllerHooks.ConfigureMenuItem"/>, while its anchoring, focus handling,
	/// and lifetime stay with the controller so the presentation state remains consistent.
	/// </remarks>
	/// <param name="lineNumber">The one-based document line the action context belongs to.</param>
	/// <param name="anchor">The menu's top-left position in the text area's coordinate space.</param>
	/// <returns>
	/// <see langword="true"/> when the menu opened; otherwise, <see langword="false"/> because no actions are
	/// available for the line or the editor is not connected to a presentation source.
	/// </returns>
	public bool TryOpenActions(int lineNumber, Point anchor)
	{
		if (_lifetime.IsDisposed || !_hasActions || lineNumber != _indicatorLineNumber || !CanOpenMenu())
			return false;

		return _menu.TryOpenMenu(_actions, _textArea, anchor, InvokeAction);
	}

	/// <summary>
	/// Closes the actions menu when it is open.
	/// </summary>
	public void CloseActions()
	{
		if (_lifetime.IsDisposed)
			return;

		_menu.Close();
	}

	/// <summary>
	/// Cancels the scheduled debounced request, if any, so it never runs.
	/// </summary>
	public void CancelScheduledRequest()
	{
		if (_lifetime.IsDisposed)
			return;

		_debouncer.Cancel();
	}

	/// <summary>
	/// Cancels the in-flight request, if any, so a cooperative host can stop early. The canceled
	/// request's result is rejected because its cancellation token is canceled; a result that a newer
	/// request replaced is rejected as well.
	/// </summary>
	public void CancelInFlightRequest()
	{
		_lifetime.CancelInFlightRequest();
	}

	/// <summary>
	/// Marks outstanding requests as stale so their completed results are discarded without canceling
	/// the work.
	/// </summary>
	public void InvalidateRequests()
	{
		_lifetime.InvalidateRequests();
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		if (_lifetime.IsDisposed)
			return;

		_lifetime.Dispose();

		_textArea.Caret.PositionChanged -= HandleContextChangedEvent;
		_textArea.SelectionChanged -= HandleContextChangedEvent;
		_textArea.DocumentChanged -= HandleDocumentChanged;
		AttachDocument(null);

		_debouncer.Dispose();
		_menu.Dispose();

		_actions = [];
		_hasActions = false;
		_indicatorLines.Clear();

		// A margin that is still attached repaints without the indicator; a detached margin has
		// already unsubscribed from the event.
		Changed?.Invoke(this, EventArgs.Empty);
	}

	/// <summary>
	/// Gets the one-based document lines the margin must mark: the indicator's line while actions are
	/// available, otherwise nothing.
	/// </summary>
	/// <returns>The cached indicator lines; the returned list is reused and must not be mutated.</returns>
	internal IReadOnlyList<int> GetIndicatorLineNumbers() => _indicatorLines;

	/// <summary>
	/// Opens the actions menu for a margin click on the indicator's line.
	/// </summary>
	/// <param name="lineNumber">The one-based document line the margin click resolved to.</param>
	/// <param name="marginPoint">The click position in the margin's coordinate space.</param>
	/// <returns><see langword="true"/> when the menu opened; otherwise, <see langword="false"/>.</returns>
	internal bool TryOpenActionsFromMargin(int lineNumber, Point marginPoint)
	{
		// The margin point is translated against the visual tree, so the connectivity refusal must run
		// before the translation.
		if (!CanOpenMenu())
			return false;

		Point areaPoint =
#if AVALONIAEDIT
			Margin.TranslatePoint(marginPoint, _textArea) ?? default;
#else
			Margin.TranslatePoint(marginPoint, _textArea);
#endif

		return TryOpenActions(
			lineNumber,
			new Point(GetTextViewOrigin().X + _menuOptions.AnchorXOffset, areaPoint.Y + _menuOptions.MarginAnchorYOffset));
	}

	private void HandleContextChangedEvent(object? sender, EventArgs e) => HandleContextChanged();

	private void HandleDocumentChanged(object? sender, EventArgs e)
	{
		if (_lifetime.IsDisposed)
			return;

		AttachDocument(_textArea.Document);
		ClearState();
		HandleContextChanged();
	}

	private void Document_Changed(object? sender, DocumentChangeEventArgs e)
	{
		if (_lifetime.IsDisposed)
			return;

		HandleContextChanged();
	}

	private void HandleContextChanged()
	{
		if (_lifetime.IsDisposed)
			return;

		// Pending work belongs to the previous context: a cooperative host is asked to stop early and
		// the completed result cannot be published after this point.
		_lifetime.CancelAndInvalidate();

		// A published indicator whose line no longer matches the caret line is stale immediately;
		// movement inside the same line keeps it until the refreshed request replaces or clears it.
		if (_hasActions && GetCaretLineNumber() != _indicatorLineNumber)
			ClearState();

		ScheduleRequest();
	}

	private void ScheduleRequest()
	{
		_debouncer.Arm(() => _ = RequestActionsAsync());
	}

	private void AttachDocument(TextDocument? document)
	{
		if (ReferenceEquals(_document, document))
			return;

		if (_document is not null)
			_document.Changed -= Document_Changed;

		_document = document;

		if (_document is not null)
			_document.Changed += Document_Changed;
	}

	private async Task RequestActionsAsync()
	{
		if (_lifetime.IsDisposed)
			return;

		// The attempt supersedes any request that is still outstanding before the host state builder can
		// veto or fail: a request that stayed current could otherwise publish after this attempt already
		// replaced or cleared the state. The coordinator supersedes on admission, so claiming the slot up
		// front also covers the paths that conclude without admitting a replacement request.
		_lifetime.CancelAndInvalidate();

		TextDocument? document = _textArea.Document;

		if (document is null)
		{
			ClearState();
			return;
		}

		// The document text travels as a lazy snapshot: the context's carrier copies the text only when
		// the host's request builder reads DocumentText, so a settled request whose builder vetoes
		// without reading the text never copies the whole document. CreateSnapshot is amortized constant
		// time and the memoizing factory materializes the text at most once.
		ITextSource snapshot = document.CreateSnapshot();
		int documentLength = snapshot.TextLength;
		string? documentText = null;
		int caretOffset = document.ClampOffset(_textArea.Caret.Offset);
		(int selectionStart, int selectionEnd) = GetSelectionOffsets(caretOffset);
		var context = TextCodeActionContext.FromDocument(
			TextCodeActionDocumentText.FromFactory(documentLength, () => documentText ??= snapshot.Text),
			caretOffset,
			selectionStart,
			selectionEnd);

		if (!HostCallback.TryRun(() => _buildRequest(context), _logger, LogHostCallbackFailed, out TextCodeActionRequest? request))
		{
			ClearState();
			return;
		}

		if (request is not TextCodeActionRequest providerRequest)
		{
			ClearState();
			return;
		}

		bool failureReported = false;

		try
		{
			// The coordinator cancels the previous request when a newer one starts and discards results
			// that are no longer current. Its publication callbacks run on the caller's captured
			// synchronization context only when one exists, so the current-state check and the publish
			// path marshal to the text area's dispatcher explicitly: hosts that pump a dispatcher
			// manually and tests get the same guarantee as application hosts. A failure is reported
			// through the failure callback, which the coordinator invokes only while this request still
			// owns the slot, so a superseded provider failure cannot clear the newer request's state.
			await _lifetime.Coordinator.RunAsync(
				state: (State: providerRequest, Document: document),
				computeAsync: (request, cancellationToken) => _requestCodeActionsAsync(request.State, cancellationToken),
				canApply: (request, _) => DispatcherInvocation.Run(
					_textArea.Dispatcher,
					() => CanPublishRequestState(request.Document)),
				apply: actions => DispatcherInvocation.Run(
					_textArea.Dispatcher,
					() => PublishActions(actions)),
				onFailure: exception =>
				{
					failureReported = true;
					LogRequestFailed(_logger, exception);
					DispatcherInvocation.Run(
						_textArea.Dispatcher,
						() =>
						{
							if (CanPublishRequestState(document))
								ClearState();
						});
				}).ConfigureAwait(true);
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
		}
	}

	// The publication predicate is shared by the success and failure paths so both honor the same
	// supersession and document-identity guards.
	private bool CanPublishRequestState(TextDocument document)
		=> !_lifetime.IsDisposed && ReferenceEquals(_textArea.Document, document);

	private void PublishActions(IReadOnlyList<TextCodeActionItem> actions)
	{
		if (_lifetime.IsDisposed)
			return;

		// A misbehaving host delegate must not corrupt the published state or crash the dispatch pass.
		if (actions is null)
		{
			ClearState();
			return;
		}

		List<TextCodeActionItem> usable = [];

		for (int index = 0; index < actions.Count; index++)
		{
			TextCodeActionItem? action = actions[index];

			if (action is not null)
				usable.Add(action);
		}

		if (usable.Count == 0)
		{
			ClearState();
			return;
		}

		// An open menu still presents the previous snapshot, whose items may already be stale, so the set
		// replacement closes it before the new snapshot becomes current. The published snapshot is wrapped
		// read-only so a caller that downcasts it cannot mutate the controller's state.
		_menu.Close();

		_actions = usable.AsReadOnly();
		_hasActions = true;
		_indicatorLineNumber = GetCaretLineNumber();

		_indicatorLines.Clear();

		if (_indicatorLineNumber > 0)
			_indicatorLines.Add(_indicatorLineNumber);

		Changed?.Invoke(this, EventArgs.Empty);
	}

	private void ClearState()
	{
		bool raisedChange = _hasActions || _indicatorLines.Count > 0;

		// The published action set disappears; an open menu would still offer its items, so it closes with
		// the state it presented.
		_menu.Close();

		_actions = [];
		_hasActions = false;
		_indicatorLines.Clear();

		if (raisedChange)
			Changed?.Invoke(this, EventArgs.Empty);
	}

	private int GetCaretLineNumber()
	{
		TextDocument? document = _textArea.Document;

		if (document is null)
			return 0;

		int offset = document.ClampOffset(_textArea.Caret.Offset);
		return document.GetLineByOffset(offset).LineNumber;
	}

	/// <summary>
	/// Resolves the selection range the request context carries.
	/// </summary>
	/// <remarks>
	/// The editor exposes a selection as one surrounding <see cref="ISegment"/> even when the user selected
	/// several disjoint ranges, so a multi-range selection is collapsed to a single ordered range here. The
	/// context therefore cannot distinguish the individual ranges; a host that must act on each range
	/// separately has to read the editor's selection itself.
	/// </remarks>
	/// <param name="caretOffset">The clamped caret offset, used when no non-empty selection is present.</param>
	/// <returns>
	/// The zero-based selection start and end offsets; both equal <paramref name="caretOffset"/> when the
	/// selection is empty or its segment is unavailable.
	/// </returns>
	private (int Start, int End) GetSelectionOffsets(int caretOffset)
	{
		Selection? selection = _textArea.Selection;

		if (selection is null || selection.IsEmpty)
			return (caretOffset, caretOffset);

		ISegment? segment = selection.SurroundingSegment;

		if (segment is null)
			return (caretOffset, caretOffset);

		// The offsets are normalized for the host regardless of the selection direction; Selection is an
		// extensible contract, so the segment's ordering is not relied on.
		return (Math.Min(segment.Offset, segment.EndOffset), Math.Max(segment.Offset, segment.EndOffset));
	}

	private bool CanOpenMenu()
#if AVALONIAEDIT
		=> TopLevel.GetTopLevel(_textArea) is not null;
#else
		=> PresentationSource.FromVisual(_textArea) is not null;
#endif

	private Point GetIndicatorAnchor()
	{
		Point origin = GetTextViewOrigin();
		double y = origin.Y;
		TextView textView = _textArea.TextView;

		if (_indicatorLineNumber > 0 && textView.VisualLinesValid
			&& textView.GetVisualLine(_indicatorLineNumber) is VisualLine line)
		{
			y += line.VisualTop - textView.VerticalOffset;
		}

		return new Point(origin.X + _menuOptions.AnchorXOffset, y + _menuOptions.CaretAnchorYOffset);
	}

	private Point GetTextViewOrigin()
#if AVALONIAEDIT
		=> _textArea.TextView.TranslatePoint(new Point(0.0, 0.0), _textArea) ?? default;
#else
		=> _textArea.TextView.TranslatePoint(new Point(0.0, 0.0), _textArea);
#endif

	private void InvokeAction(TextCodeActionItem item) => _ = ExecuteActionAsync(item);

	private async Task ExecuteActionAsync(TextCodeActionItem item)
	{
		if (_lifetime.IsDisposed)
			return;

		try
		{
			await _executeActionAsync(item).ConfigureAwait(true);
		}
		catch (Exception exception)
		{
			LogHostCallbackFailed(_logger, exception);
		}
	}
}

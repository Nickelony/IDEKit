using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nickelony.IDEKit.Core.Callbacks;
using Nickelony.IDEKit.Core.Requests;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.Core.Threading;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Diagnostics;

/// <summary>
/// Keeps the diagnostics of one editor current, from a provider it asks after each edit settles and from a source that pushes them.
/// </summary>
/// <remarks>
/// <para>
/// The newest delivery wins. A pushed set replaces the shown one, and so does the answer of a pull, unless an edit
/// or a push came after the pull started: the edit makes the pulled text stale, and the push is the producer's newer word, so either cancels the pull
/// and drops its answer. A pull that fails keeps the shown set and logs the failure. A pushed set that the producer computed for older text is shown
/// at its offsets in the current text until the next set.
/// </para>
/// <para>
/// The host reports each edit through <see cref="HandleTextChanged"/>, which restarts the <see cref="DiagnosticsControllerOptions.SettleDelay"/>,
/// so typing pulls once after the last keystroke. The controller pulls once more on the dispatcher turn after its creation, and the source's
/// subscription delivers the diagnostics the source already holds on that turn too.
/// </para>
/// <para>
/// <include file="../../../../shared/docs/ThreadAffinity.xml" path="doc/members/member[@name='ThreadAffinity.Remarks']/*"/>
/// </para>
/// </remarks>
public sealed partial class DiagnosticsController : IDisposable
{
	[LoggerMessage(EventId = 1050, EventName = "DiagnosticsPullFailed", Level = LogLevel.Warning, Message = "The diagnostics pull failed.")]
	private static partial void LogPullFailed(ILogger logger, Exception? exception);

	[LoggerMessage(
		EventId = 1051,
		EventName = "DiagnosticsHostCallbackFailed",
		Level = LogLevel.Warning,
		Message = "A diagnostics host callback failed."
	)]
	private static partial void LogHostCallbackFailed(ILogger logger, Exception? exception);

	private readonly string _documentId;
	private readonly IDiagnosticsProvider? _provider;
	private readonly IUiDispatcher _dispatcher;
	private readonly Func<string> _getText;
	private readonly Action<IReadOnlyList<Diagnostic>> _showDiagnostics;
	private readonly Func<int, int?>? _findShownIndexAt;
	private readonly Func<int, TextRange?>? _getShownRange;
	private readonly Action? _detach;
	private readonly ILogger _logger;
	private readonly DispatcherDebouncer _debouncer;
	private readonly RequestLifetime _lifetime = new();
	private readonly IDisposable? _subscription;

	// Set once the editor replaced its document: a pull must never carry another document's text under the fixed id.
	private bool _isDocumentReplaced;

	/// <param name="documentId">
	/// The document
	/// whose diagnostics the controller shows: the id that each pull carries and each push must name. The controller serves that one document,
	/// as the
	/// <see href="https://github.com/Nickelony/LanguageServer/blob/main/docs/EditorLanguageFeaturesGuide.md#one-controller-per-document">document rule</see>
	/// says.
	/// </param>
	/// <param name="provider">The provider the controller asks, or <see langword="null"/> to pull nothing.</param>
	/// <param name="source">The source whose pushes the controller shows, or <see langword="null"/> to take no pushes.</param>
	/// <param name="hooks">The editor callbacks; the hook group documents which members are required.</param>
	/// <param name="dispatcher">The dispatcher whose thread the members, the settle timer and the hooks run on.</param>
	/// <param name="options">The controller options, or <see langword="null"/> to use the defaults.</param>
	/// <param name="logger">An optional logger for pull and hook failures.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="documentId"/>, <paramref name="hooks"/> or <paramref name="dispatcher"/> is <see langword="null"/>, or a required hook
	/// is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="documentId"/> is blank, or <paramref name="provider"/> and <paramref name="source"/> are both <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">The caller is not on the thread of <paramref name="dispatcher"/>.</exception>
	public DiagnosticsController(
		string documentId,
		IDiagnosticsProvider? provider,
		IDiagnosticsSource? source,
		DiagnosticsControllerHooks hooks,
		IUiDispatcher dispatcher,
		DiagnosticsControllerOptions? options = null,
		ILogger? logger = null
	)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(documentId);

		ArgumentNullException.ThrowIfNull(hooks);
		ArgumentNullException.ThrowIfNull(dispatcher);
		ArgumentNullException.ThrowIfNull(hooks.GetText);
		ArgumentNullException.ThrowIfNull(hooks.ShowDiagnostics);

		if (provider is null && source is null)
			throw new ArgumentException("A diagnostics controller needs a provider, a source, or both.", nameof(provider));

		// The controller shows its sets on the dispatcher's thread, so creating it elsewhere is a caller error.
		dispatcher.VerifyAccess();

		DiagnosticsControllerOptions resolvedOptions = options ?? DiagnosticsControllerOptions.Default;

		_documentId = documentId;
		_provider = provider;
		_dispatcher = dispatcher;
		_getText = hooks.GetText;
		_showDiagnostics = hooks.ShowDiagnostics;
		_findShownIndexAt = hooks.FindShownIndexAt;
		_getShownRange = hooks.GetShownRange;
		_detach = hooks.Detach;
		_logger = logger ?? NullLogger.Instance;
		_debouncer = new DispatcherDebouncer(resolvedOptions.SettleDelay, dispatcher);

		_subscription = source?.Subscribe(documentId, dispatcher, onChanged: HandlePushed, documentIdComparer: resolvedOptions.DocumentIdComparer);

		if (provider is not null)
			dispatcher.Post(() => _ = PullAsync());
	}

	/// <summary>Raised on the dispatcher thread after <see cref="Current"/> changed.</summary>
	public event EventHandler? Changed;

	/// <summary>
	/// The diagnostics shown in the editor, with the ranges the producer reported; an empty list before the first delivery and after disposal.
	/// </summary>
	/// <remarks>
	/// The ranges describe the text the producer saw, so after an edit <see cref="TryGetShownRange"/> answers where each one is shown now.
	/// </remarks>
	public IReadOnlyList<Diagnostic> Current { get; private set; } = [];

	/// <summary>Finds the shown diagnostic at an offset of the document as it is now.</summary>
	/// <param name="offset">The zero-based offset in the current document.</param>
	/// <returns>
	/// The diagnostic as it is shown now, or <see langword="null"/> when none covers the offset or the controller is disposed.
	/// After an edit moved it, the answer is a copy of the <see cref="Current"/> entry whose range is the shown one.
	/// </returns>
	/// <remarks>
	/// With <see cref="DiagnosticsControllerHooks.FindShownIndexAt"/>, the editor answers from the positions
	/// its decorations moved to; otherwise, the first diagnostic of <see cref="Current"/> whose range contains the offset answers.
	/// </remarks>
	public Diagnostic? FindAt(int offset)
	{
		if (_lifetime.IsDisposed)
			return null;

		IReadOnlyList<Diagnostic> current = Current;

		if (_findShownIndexAt is not { } findShownIndexAt)
		{
			foreach (Diagnostic diagnostic in current)
			{
				if (diagnostic.ContainsOffset(offset))
					return diagnostic;
			}

			return null;
		}

		if (!CallbackContainment.TryRun(() => findShownIndexAt(offset), ReportHostCallbackFault, out int? index))
			return null;

		if (index is not int found || found < 0 || found >= current.Count)
			return null;

		Diagnostic shown = current[found];

		if (!TryGetShownRange(found, out TextRange range) || range == shown.Range)
			return shown;

		return new Diagnostic(shown.Severity, shown.Message, range) { Source = shown.Source, Code = shown.Code };
	}

	/// <summary>Returns where an entry of <see cref="Current"/> is shown in the document as it is now.</summary>
	/// <param name="index">The position of the diagnostic in <see cref="Current"/>.</param>
	/// <param name="range">Receives the shown range.</param>
	/// <returns>
	/// <see langword="true"/> when the diagnostic is shown; <see langword="false"/> when an edit removed its text,
	/// the index is outside <see cref="Current"/>, or the controller is disposed.
	/// </returns>
	/// <remarks>
	/// Without <see cref="DiagnosticsControllerHooks.GetShownRange"/>, the shown range is the one the producer reported. A list of problems reads
	/// it for each row, so a row points where its text is now.
	/// </remarks>
	public bool TryGetShownRange(int index, out TextRange range)
	{
		range = default;

		IReadOnlyList<Diagnostic> current = Current;

		if (_lifetime.IsDisposed || index < 0 || index >= current.Count)
			return false;

		if (_getShownRange is not { } getShownRange)
		{
			range = current[index].Range;
			return true;
		}

		if (!CallbackContainment.TryRun(() => getShownRange(index), ReportHostCallbackFault, out TextRange? shown) || shown is not TextRange found)
			return false;

		range = found;
		return true;
	}

	/// <summary>Reports an edit of the document, which drops the answer of the pull in flight and restarts the settle delay.</summary>
	/// <exception cref="InvalidOperationException">The caller is not on the controller's dispatcher thread.</exception>
	/// <remarks>
	/// A call after disposal or after <see cref="HandleDocumentReplaced"/> does nothing.
	/// </remarks>
	public void HandleTextChanged()
	{
		if (_lifetime.IsDisposed || _isDocumentReplaced)
			return;

		_dispatcher.VerifyAccess();

		// The pull in flight asked about text that is gone, so it is canceled and its answer is dropped.
		_lifetime.CancelAndInvalidate();
		_lifetime.AdvanceGeneration();

		if (_provider is not null)
			_debouncer.Arm(() => _ = PullAsync());
	}

	/// <summary>Reports that the editor shows another document, which retires the controller until it is disposed.</summary>
	/// <exception cref="InvalidOperationException">The caller is not on the controller's dispatcher thread.</exception>
	/// <remarks>
	/// The id of a controller is fixed,
	/// so the controller clears the shown set, cancels the pull in flight and the scheduled one, and pulls and shows nothing more, as the
	/// <see href="https://github.com/Nickelony/LanguageServer/blob/main/docs/EditorLanguageFeaturesGuide.md#one-controller-per-document">document rule</see>
	/// says. A call after disposal or a second call does nothing.
	/// </remarks>
	public void HandleDocumentReplaced()
	{
		if (_lifetime.IsDisposed || _isDocumentReplaced)
			return;

		_dispatcher.VerifyAccess();

		_isDocumentReplaced = true;
		_lifetime.CancelAndInvalidate();
		_lifetime.AdvanceGeneration();
		_debouncer.Cancel();
		_subscription?.Dispose();

		Show([]);
	}

	/// <summary>
	/// Cancels the pull in flight and the scheduled one, stops the source's deliveries, and calls <see cref="DiagnosticsControllerHooks.Detach"/>.
	/// </summary>
	/// <remarks>
	/// No hook other than <see cref="DiagnosticsControllerHooks.Detach"/> runs after disposal, and the method is idempotent.
	/// </remarks>
	public void Dispose()
	{
		if (_lifetime.IsDisposed)
			return;

		_lifetime.Dispose();
		_debouncer.Dispose();
		_subscription?.Dispose();

		Current = [];

		if (_detach is { } detach)
			CallbackContainment.TryRun(detach, ReportHostCallbackFault);
	}

	private void HandlePushed(IReadOnlyList<Diagnostic> diagnostics)
	{
		if (_lifetime.IsDisposed || _isDocumentReplaced)
			return;

		// A push is the producer's newer word on the document, so the pull in flight is dropped and the push replaces the shown set.
		_lifetime.CancelAndInvalidate();
		_lifetime.AdvanceGeneration();

		Show(diagnostics);
	}

	private async Task PullAsync()
	{
		if (_lifetime.IsDisposed || _isDocumentReplaced || _provider is not { } provider)
			return;

		// The pull supersedes the outstanding one before it reads the text, and its generation is taken before the read, so an edit or a push
		// that lands from here on makes this pull stale.
		_lifetime.CancelAndInvalidate();
		int generation = _lifetime.AdvanceGeneration();

		if (!CallbackContainment.TryRun(_getText, ReportHostCallbackFault, out string? text) || text is null)
			return;

		bool failureReported = false;

		try
		{
			// The provider's continuation can resume on any thread, so the currency check and the show are one step on the dispatcher.
			await _lifetime
				.RunAsync(
					state: new DiagnosticsRequest(_documentId, text),
					computeAsync: provider.GetDiagnosticsAsync,

					apply: diagnostics =>
						_dispatcher.Invoke(() =>
						{
							if (_lifetime.IsCurrent(generation))
								Show(diagnostics);
						}),

					onFailure: exception =>
					{
						failureReported = true;
						LogPullFailed(_logger, exception);
					}
				)
				.ConfigureAwait(true);
		}
		catch (OperationCanceledException)
		{
			// An edit, a push, a newer pull or disposal canceled the pull; whatever canceled it owns the shown set.
		}
		catch (Exception exception)
		{
			if (_lifetime.IsDisposed || failureReported)
				return;

			LogPullFailed(_logger, exception);
		}
	}

	private void Show(IReadOnlyList<Diagnostic>? diagnostics)
	{
		// A provider that breaks its contract with a null list or a null entry must not corrupt the shown set.
		List<Diagnostic> shown = new(diagnostics?.Count ?? 0);

		if (diagnostics is not null)
		{
			foreach (Diagnostic? diagnostic in diagnostics)
			{
				if (diagnostic is not null)
					shown.Add(diagnostic);
			}
		}

		IReadOnlyList<Diagnostic> current = shown.AsReadOnly();

		Current = current;
		CallbackContainment.TryRun(() => _showDiagnostics(current), ReportHostCallbackFault);
		Changed?.Invoke(this, EventArgs.Empty);
	}

	private void ReportHostCallbackFault(Exception exception) => LogHostCallbackFailed(_logger, exception);
}

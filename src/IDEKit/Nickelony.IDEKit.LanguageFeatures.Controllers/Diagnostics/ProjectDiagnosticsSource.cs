using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nickelony.IDEKit.Core.Callbacks;
using Nickelony.IDEKit.Core.Requests;
using Nickelony.IDEKit.Core.Threading;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Diagnostics;

/// <summary>Produces the diagnostics of the documents of a project from a host analysis that it reruns after each reported change settles.</summary>
/// <remarks>
/// <para>
/// The host reports each change of any document with <see cref="HandleProjectChanged"/>. The source then calls
/// the analysis hook once, on the dispatcher thread, after <see cref="ProjectDiagnosticsSourceOptions.SettleDelay"/>. A change reported
/// while the analysis runs cancels its token and drops its result even if it completes, so the source raises a result only when no change
/// was reported after its analysis started.
/// </para>
/// <para>
/// At most one analysis runs and one waits: the source starts the next analysis only after the canceled one returned, so a hook that ignores
/// its token never runs twice at once. The source raises <see cref="DiagnosticsUpdated"/> on the dispatcher thread, once for each document
/// whose diagnostics differ from the previous analysis; a document that had diagnostics and has none now gets an empty set. An analysis that fails
/// keeps the previous diagnostics and is logged.
/// </para>
/// <para>
/// A change that a subscriber reports during a raise takes effect from the next analysis,
/// and the rest of the current raise still happens. Create the source on the dispatcher thread, and call <see cref="HandleProjectChanged"/>
/// and <see cref="Dispose"/> from it. <see cref="GetDiagnostics"/> and the event subscription are safe from any thread.
/// </para>
/// </remarks>
public sealed partial class ProjectDiagnosticsSource : IDiagnosticsSource, IDisposable
{
	[LoggerMessage(
		EventId = 1052,
		EventName = "ProjectDiagnosticsAnalysisFailed",
		Level = LogLevel.Warning,
		Message = "The project diagnostics analysis failed; the previous diagnostics stay."
	)]
	private static partial void LogAnalysisFailed(ILogger logger, Exception? exception);

	[LoggerMessage(
		EventId = 1053,
		EventName = "ProjectDiagnosticsSubscriberFailed",
		Level = LogLevel.Warning,
		Message = "A project diagnostics subscriber failed; the later subscribers are still notified."
	)]
	private static partial void LogSubscriberFailed(ILogger logger, Exception? exception);

	private readonly Func<CancellationToken, Task<IReadOnlyDictionary<string, IReadOnlyList<Diagnostic>>>> _analyzeAsync;
	private readonly IUiDispatcher _dispatcher;
	private readonly IEqualityComparer<string> _documentIdComparer;
	private readonly ILogger _logger;
	private readonly DispatcherDebouncer _debouncer;
	private readonly RequestLifetime _lifetime = new();

	// The dictionary is replaced as a whole on the dispatcher thread and never changed afterwards, so a reader on any thread sees one analysis.
	private volatile Dictionary<string, DiagnosticsUpdatedEventArgs> _current;

	// The run whose analysis has not returned yet, and whether a start waits for it; both belong to the dispatcher thread.
	private Task? _running;
	private bool _isStartPending;

	/// <param name="hooks">The host hook that analyzes the project.</param>
	/// <param name="dispatcher">The dispatcher whose thread runs the settle timer, the members and the event.</param>
	/// <param name="options">The source options, or <see langword="null"/> to use the defaults.</param>
	/// <param name="logger">An optional logger for failed analyses and failed subscribers.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="dispatcher"/> is <see langword="null"/>, or the hook of <paramref name="hooks"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">The caller is not on the thread of <paramref name="dispatcher"/>.</exception>
	public ProjectDiagnosticsSource(
		ProjectDiagnosticsSourceHooks hooks,
		IUiDispatcher dispatcher,
		ProjectDiagnosticsSourceOptions? options = null,
		ILogger? logger = null
	)
	{
		ArgumentNullException.ThrowIfNull(hooks.AnalyzeAsync);
		ArgumentNullException.ThrowIfNull(dispatcher);

		// The source raises its event and arms its timer on the dispatcher's thread, so creating it elsewhere is a caller error.
		dispatcher.VerifyAccess();

		ProjectDiagnosticsSourceOptions resolvedOptions = options ?? ProjectDiagnosticsSourceOptions.Default;

		_analyzeAsync = hooks.AnalyzeAsync;
		_dispatcher = dispatcher;
		_documentIdComparer = resolvedOptions.DocumentIdComparer ?? StringComparer.Ordinal;
		_logger = logger ?? NullLogger.Instance;
		_debouncer = new DispatcherDebouncer(resolvedOptions.SettleDelay, dispatcher);
		_current = new Dictionary<string, DiagnosticsUpdatedEventArgs>(_documentIdComparer);
	}

	/// <inheritdoc/>
	public event EventHandler<DiagnosticsUpdatedEventArgs>? DiagnosticsUpdated;

	/// <inheritdoc/>
	public IReadOnlyList<Diagnostic> GetDiagnostics(string documentId)
	{
		ArgumentNullException.ThrowIfNull(documentId);
		return _current.TryGetValue(documentId, out DiagnosticsUpdatedEventArgs? update) ? update.Diagnostics : [];
	}

	/// <summary>Reports a change of any document of the project, which supersedes the analysis in flight and restarts the settle delay.</summary>
	/// <exception cref="InvalidOperationException">The caller is not on the source's dispatcher thread.</exception>
	/// <remarks>
	/// A call after disposal does nothing. The source has no analysis until the first call, so a host reports once when it opens the project.
	/// </remarks>
	public void HandleProjectChanged()
	{
		if (_lifetime.IsDisposed)
			return;

		_dispatcher.VerifyAccess();

		// The analysis in flight read text that the change replaced, so its token is canceled and its result is dropped.
		_lifetime.CancelAndInvalidate();
		_lifetime.AdvanceGeneration();

		_debouncer.Arm(StartAnalysis);
	}

	/// <summary>Cancels the pending and the running analysis, drops the held diagnostics, and raises nothing afterwards.</summary>
	/// <exception cref="InvalidOperationException">
	/// The caller is not on the source's dispatcher thread, and the source is not disposed yet.
	/// </exception>
	/// <remarks>
	/// The method is idempotent. A subscriber that disposes the source is the last one the source calls. The method does not wait for the analysis
	/// that is running, which stops raising at once and ends when its hook returns.
	/// </remarks>
	public void Dispose()
	{
		if (_lifetime.IsDisposed)
			return;

		_dispatcher.VerifyAccess();

		_lifetime.Dispose();
		_debouncer.Dispose();

		_isStartPending = false;
		_current = new Dictionary<string, DiagnosticsUpdatedEventArgs>(_documentIdComparer);
	}

	private void StartAnalysis()
	{
		// Only the run in flight owns the host's analysis, so a canceled run that ignores its token returns before the next one starts.
		if (_running is not null)
		{
			_isStartPending = true;
			return;
		}

		BeginAnalysis();
	}

	private void BeginAnalysis()
	{
		// The run supersedes the outstanding one and takes its generation on the turn that calls the hook, before the host reads any text, so
		// a change reported from here on makes this run stale.
		_lifetime.CancelAndInvalidate();
		int generation = _lifetime.AdvanceGeneration();

		_running = RunAnalysisAsync(generation);
	}

	private void HandleRunEnded()
	{
		_running = null;

		if (!_isStartPending || _lifetime.IsDisposed)
			return;

		_isStartPending = false;
		BeginAnalysis();
	}

	private async Task RunAnalysisAsync(int generation)
	{
		try
		{
			// The result reaches the dispatcher by a post, because the continuation can resume on any thread.
			await _lifetime
				.RunAsync(
					state: _analyzeAsync,
					computeAsync: ComputeSnapshotAsync,
					apply: snapshot => _dispatcher.Post(() => PublishIfCurrent(generation, snapshot)),
					onFailure: exception => LogAnalysisFailed(_logger, exception)
				)
				.ConfigureAwait(false);
		}
		catch (Exception)
		{
			// The run logged a failure that belongs to it. Whatever else lands here belongs to a run that disposal or a later change ended,
			// which has no result to report, and a fire-and-forget task must not fault.
		}

		try
		{
			// The post follows the post of the result, so the dispatcher publishes a result before it starts the analysis that waited.
			_dispatcher.Post(HandleRunEnded);
		}
		catch (Exception exception) when (exception is OperationCanceledException or InvalidOperationException)
		{
			// A dispatcher that shuts down rejects the post, and the source has no work left to start on it.
		}
	}

	// The hook runs on the caller's thread, which is the dispatcher thread, so the host captures the text it needs before it moves work off the
	// thread. The validation of the result runs wherever the hook's task completes.
	private async Task<Dictionary<string, DiagnosticsUpdatedEventArgs>> ComputeSnapshotAsync(
		Func<CancellationToken, Task<IReadOnlyDictionary<string, IReadOnlyList<Diagnostic>>>> analyzeAsync,
		CancellationToken cancellationToken
	)
	{
		IReadOnlyDictionary<string, IReadOnlyList<Diagnostic>> result;

		try
		{
			Task<IReadOnlyDictionary<string, IReadOnlyList<Diagnostic>>> analysis =
				analyzeAsync(cancellationToken) ?? throw new InvalidOperationException("The project analysis returned no task.");

			result = await analysis.ConfigureAwait(false) ?? throw new InvalidOperationException("The project analysis returned no result.");
		}
		catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
		{
			// Nothing asked the analysis to stop, so a cancellation from inside the host, such as a timeout, is a failed analysis.
			throw new InvalidOperationException("The project analysis was canceled without a request.", exception);
		}

		Dictionary<string, DiagnosticsUpdatedEventArgs> snapshot = new(_documentIdComparer);

		foreach ((string documentId, IReadOnlyList<Diagnostic> diagnostics) in result)
		{
			// The event arguments validate the list and own a copy of it, and a document with no diagnostics is absent from the snapshot.
			var update = new DiagnosticsUpdatedEventArgs(documentId, diagnostics);

			if (update.Diagnostics.Count == 0)
				continue;

			// Ids that the comparer treats as equal name one document, so their diagnostics add up under the first spelling.
			if (snapshot.TryGetValue(documentId, out DiagnosticsUpdatedEventArgs? earlier))
				update = new DiagnosticsUpdatedEventArgs(earlier.DocumentId, [.. earlier.Diagnostics, .. update.Diagnostics]);

			snapshot[documentId] = update;
		}

		return snapshot;
	}

	private void PublishIfCurrent(int generation, Dictionary<string, DiagnosticsUpdatedEventArgs> next)
	{
		if (!_lifetime.IsCurrent(generation))
			return;

		Dictionary<string, DiagnosticsUpdatedEventArgs> previous = _current;
		List<DiagnosticsUpdatedEventArgs> changes = [];

		foreach ((string documentId, DiagnosticsUpdatedEventArgs update) in next)
		{
			if (!previous.TryGetValue(documentId, out DiagnosticsUpdatedEventArgs? held) || !held.Diagnostics.SequenceEqual(update.Diagnostics))
				changes.Add(update);
		}

		// A document that had diagnostics and has none now is announced with an empty set, so an editor clears its underlines.
		foreach (string documentId in previous.Keys)
		{
			if (!next.ContainsKey(documentId))
				changes.Add(new DiagnosticsUpdatedEventArgs(documentId, []));
		}

		_current = next;

		foreach (DiagnosticsUpdatedEventArgs update in changes)
			Raise(update);
	}

	private void Raise(DiagnosticsUpdatedEventArgs update)
	{
		if (DiagnosticsUpdated is not { } handlers)
			return;

		foreach (Delegate handler in handlers.GetInvocationList())
		{
			// A subscriber can dispose the source, and nothing is raised after that.
			if (_lifetime.IsDisposed)
				return;

			CallbackContainment.TryRun(
				() => ((EventHandler<DiagnosticsUpdatedEventArgs>)handler)(this, update),
				exception => LogSubscriberFailed(_logger, exception)
			);
		}
	}
}

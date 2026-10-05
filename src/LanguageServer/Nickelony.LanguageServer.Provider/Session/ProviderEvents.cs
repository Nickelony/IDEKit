using Nickelony.IDEKit.LanguageFeatures.Diagnostics;

namespace Nickelony.LanguageServer.Provider;

/// <summary>Holds a provider's events, raises them with handler isolation, and drops every subscriber once the provider is disposed.</summary>
/// <remarks>
/// <para>
/// One lock orders subscribing, raising and closing. A handler added after disposal began is dropped instead of kept, and a raise that already copied
/// its subscribers stops at the next handler boundary.
/// </para>
/// <para>
/// The lock is a leaf: a raise copies the subscriber list under it and invokes the handlers outside it, so no subscriber code runs while it is held.
/// </para>
/// </remarks>
internal sealed class ProviderEvents
{
	private readonly object _syncRoot = new();
	private readonly object _sender;

	private readonly ProviderLifetime _disposal;

	private readonly ILogger _logger;

	private EventHandler<DiagnosticsUpdatedEventArgs>? _diagnosticsUpdated;
	private EventHandler<SemanticTokensUpdatedEventArgs>? _semanticTokensUpdated;
	private EventHandler? _capabilitiesChanged;
	private EventHandler<StartupFailedEventArgs>? _startupFailed;
	private EventHandler<WorkspaceFileWatchFailedEventArgs>? _workspaceFileWatchFailed;

	/// <param name="sender">The object passed as the sender of every raised event.</param>
	/// <param name="disposal">The disposal whose start stops admission of new handlers and of further raises.</param>
	/// <param name="logger">The logger that records a subscriber that throws.</param>
	internal ProviderEvents(object sender, ProviderLifetime disposal, ILogger logger)
	{
		_sender = sender;
		_disposal = disposal;
		_logger = logger;
	}

	internal event EventHandler<DiagnosticsUpdatedEventArgs>? DiagnosticsUpdated
	{
		add => Add(ref _diagnosticsUpdated, value);
		remove => Remove(ref _diagnosticsUpdated, value);
	}

	internal event EventHandler<SemanticTokensUpdatedEventArgs>? SemanticTokensUpdated
	{
		add => Add(ref _semanticTokensUpdated, value);
		remove => Remove(ref _semanticTokensUpdated, value);
	}

	internal event EventHandler? CapabilitiesChanged
	{
		add => Add(ref _capabilitiesChanged, value);
		remove => Remove(ref _capabilitiesChanged, value);
	}

	internal event EventHandler<StartupFailedEventArgs>? StartupFailed
	{
		add => Add(ref _startupFailed, value);
		remove => Remove(ref _startupFailed, value);
	}

	internal event EventHandler<WorkspaceFileWatchFailedEventArgs>? WorkspaceFileWatchFailed
	{
		add => Add(ref _workspaceFileWatchFailed, value);
		remove => Remove(ref _workspaceFileWatchFailed, value);
	}

	/// <summary>Drops every subscriber, so a disposed provider keeps no reference to a host handler.</summary>
	/// <remarks>
	/// Closing is idempotent, and the provider closes its events after it signaled its disposal.
	/// </remarks>
	internal void Close()
	{
		lock (_syncRoot)
		{
			_diagnosticsUpdated = null;
			_semanticTokensUpdated = null;
			_capabilitiesChanged = null;
			_startupFailed = null;
			_workspaceFileWatchFailed = null;
		}
	}

	internal void RaiseDiagnosticsUpdated(string documentId, IReadOnlyList<Diagnostic> diagnostics)
	{
		Raise<EventHandler<DiagnosticsUpdatedEventArgs>>(
			() => _diagnosticsUpdated,
			handler => handler(_sender, new DiagnosticsUpdatedEventArgs(documentId, diagnostics)),
			"Diagnostics subscriber"
		);
	}

	internal void RaiseSemanticTokensUpdated(string documentId, IReadOnlyList<SemanticToken> semanticTokens)
	{
		Raise<EventHandler<SemanticTokensUpdatedEventArgs>>(
			() => _semanticTokensUpdated,
			handler => handler(_sender, new SemanticTokensUpdatedEventArgs(documentId, semanticTokens)),
			"Semantic-token subscriber"
		);
	}

	internal void RaiseCapabilitiesChanged() =>
		Raise<EventHandler>(() => _capabilitiesChanged, handler => handler(_sender, EventArgs.Empty), "Capability-change subscriber");

	internal void RaiseStartupFailed(StartupFailure failure)
	{
		Raise<EventHandler<StartupFailedEventArgs>>(
			() => _startupFailed,
			handler => handler(_sender, new StartupFailedEventArgs(failure)),
			"Startup-failure subscriber"
		);
	}

	internal void RaiseWorkspaceFileWatchFailed(WorkspaceFileWatchFailure failure)
	{
		Raise<EventHandler<WorkspaceFileWatchFailedEventArgs>>(
			() => _workspaceFileWatchFailed,
			handler => handler(_sender, new WorkspaceFileWatchFailedEventArgs(failure)),
			"Workspace-watcher subscriber"
		);
	}

	private void Add<TDelegate>(ref TDelegate? callbackField, TDelegate? value)
		where TDelegate : Delegate
	{
		lock (_syncRoot)
		{
			if (!_disposal.IsDisposed)
				callbackField = (TDelegate?)Delegate.Combine(callbackField, value);
		}
	}

	private void Remove<TDelegate>(ref TDelegate? callbackField, TDelegate? value)
		where TDelegate : Delegate
	{
		lock (_syncRoot)
			callbackField = (TDelegate?)Delegate.Remove(callbackField, value);
	}

	/// <summary>Raises the subscribers of one callback field with handler isolation and an admission check before each handler.</summary>
	/// <typeparam name="TDelegate">The callback delegate type of the raised event.</typeparam>
	/// <param name="callbackAccessor">Returns the current callback list for the event; it runs under the lock.</param>
	/// <param name="invoke">Invokes one handler.</param>
	/// <param name="subscriberDescription">The subscriber description used in failure logs.</param>
	private void Raise<TDelegate>(Func<TDelegate?> callbackAccessor, Action<TDelegate> invoke, string subscriberDescription)
		where TDelegate : Delegate
	{
		TDelegate? handlers;

		lock (_syncRoot)
		{
			if (_disposal.IsDisposed)
				return;

			handlers = callbackAccessor();
		}

		if (handlers is null)
			return;

		foreach (Delegate handler in handlers.GetInvocationList())
		{
			if (_disposal.IsDisposed)
				return;

			try
			{
				invoke((TDelegate)handler);
			}
			catch (Exception exception)
			{
				_logger.LogWarning(exception, "{SubscriberDescription} threw; later subscribers will still be notified.", subscriberDescription);
			}
		}
	}
}

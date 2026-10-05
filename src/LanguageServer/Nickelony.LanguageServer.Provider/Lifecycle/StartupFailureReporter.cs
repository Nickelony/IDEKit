namespace Nickelony.LanguageServer.Provider;

/// <summary>Reports startup failures to the host, once per persistence until the next successful start.</summary>
/// <remarks>
/// <para>
/// The first transient failure and the first persistent failure since the last successful start each reach the host,
/// through the claim of <see cref="StartupState"/>, and a successful start re-arms both. The claim is taken before the message is read, so a blank
/// message uses up the report of its persistence.
/// </para>
/// <para>
/// A failure reaches the host only when the profile supplies a non-blank message for it: a blank message raises nothing. Every failed attempt
/// is also logged, a persistent failure as an error and a transient one as a warning, whether or not the host hears of it again.
/// </para>
/// </remarks>
internal sealed class StartupFailureReporter
{
	private readonly StartupState _startupState;
	private readonly ProviderEvents _events;

	private readonly string _workspaceRootsDisplayText;

	private readonly string _providerDisplayName;

	private readonly string? _transientStartupFailureMessage;
	private readonly string? _persistentStartupFailureMessage;
	private readonly string? _missingClientFailureMessage;

	private readonly LanguageServerFeatureProviderOptions _options;
	private readonly ILogger _logger;

	/// <param name="context">The collaborators and values the reporter is built from.</param>
	internal StartupFailureReporter(StartupFailureReporterContext context)
	{
		_startupState = context.StartupState;
		_events = context.Events;
		_workspaceRootsDisplayText = context.WorkspaceRootsDisplayText;
		_providerDisplayName = context.ProviderDisplayName;
		_transientStartupFailureMessage = context.TransientStartupFailureMessage;
		_persistentStartupFailureMessage = context.PersistentStartupFailureMessage;
		_missingClientFailureMessage = context.MissingClientFailureMessage;
		_options = context.Options;
		_logger = context.Logger;
	}

	/// <summary>Logs one failed startup attempt and reports it to the host unless that persistence was reported already.</summary>
	/// <param name="exception">The unexpected startup exception, or <see langword="null"/> for a reported false start result.</param>
	/// <param name="consecutiveStartupFailures">The number of consecutive failed startup attempts, this one included.</param>
	/// <param name="isPersistentFailure">Whether the failure is persistent for this provider instance.</param>
	internal void ReportStartupFailure(Exception? exception, int consecutiveStartupFailures, bool isPersistentFailure)
	{
		// Log a persistent failure as an error: the provider stops starting the server until it is recreated.
		if (isPersistentFailure)
		{
			_logger.LogError(
				exception,
				"{DisplayName} language server failed to start {Count} times consecutively for workspace '{Workspace}'; language features are now disabled until the provider is recreated.",
				_providerDisplayName,
				consecutiveStartupFailures,
				_workspaceRootsDisplayText
			);
		}
		else
		{
			_logger.LogWarning(
				exception,
				"Failed to start the {DisplayName} language server for workspace '{Workspace}' (attempt {Attempt}/{Threshold}).",
				_providerDisplayName,
				_workspaceRootsDisplayText,
				consecutiveStartupFailures,
				_options.HardStartupFailureThreshold
			);
		}

		if (!_startupState.TryMarkStartupFailureReported(isPersistentFailure))
			return;

		string? message = isPersistentFailure ? _persistentStartupFailureMessage : _transientStartupFailureMessage;

		if (!string.IsNullOrWhiteSpace(message))
		{
			_events.RaiseStartupFailed(
				new StartupFailure(code: ProviderFailureCodes.StartFailed, message, isPersistent: isPersistentFailure, exception)
			);
		}
	}

	/// <summary>Logs the failure of a provider with no language-server client and reports it to the host once.</summary>
	/// <remarks>
	/// Idempotent: the failure is logged and raised at most once.
	/// </remarks>
	internal void ReportMissingClientFailure()
	{
		if (!_startupState.TryMarkStartupFailureReported(isPersistentFailure: true))
			return;

		_logger.LogError(
			"{DisplayName} language server is unavailable for workspace '{Workspace}'; language features are disabled until the provider is recreated.",
			_providerDisplayName,
			_workspaceRootsDisplayText
		);

		if (!string.IsNullOrWhiteSpace(_missingClientFailureMessage))
			_events.RaiseStartupFailed(new StartupFailure(ProviderFailureCodes.ClientUnavailable, _missingClientFailureMessage, isPersistent: true));
	}
}

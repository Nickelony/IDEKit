using System.Text.Json;
using System.Text.Json.Nodes;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Client;

/// <summary>Runs the handshake that follows the activation of one connection session and publishes the readiness of its generation.</summary>
/// <remarks>
/// The steps are the initialize request, the capture of the negotiated capabilities, the initialized notification,
/// the host's session-start notifications, the settings push and the readiness publication. Each publication is fenced on the connection generation,
/// so a session that was invalidated while the handshake ran is not reported as ready.
/// </remarks>
internal sealed class HandshakeRunner
{
	private readonly ConnectionState _state;
	private readonly ILogger _logger;
	private readonly string _workspaceRootsDisplayText;
	private readonly ProtocolSender _protocolSender;
	private readonly SettingsSnapshotCache _settingsCache;
	private readonly InitializeParamsBuilder _initializeParamsBuilder;
	private readonly SessionStartNotificationBuilder _sessionStartNotificationBuilder;

	/// <param name="context">The shared connection-collaborator context (state, logger and workspace display text).</param>
	/// <param name="protocolSender">Sends the handshake requests and notifications.</param>
	/// <param name="settingsCache">Supplies the settings snapshot that the handshake pushes.</param>
	/// <param name="initializeParamsBuilder">Builds the parameters of the initialize request.</param>
	/// <param name="sessionStartNotificationBuilder">Builds the notifications sent right after the initialized notification.</param>
	internal HandshakeRunner(
		ConnectionCollaboratorContext context,
		ProtocolSender protocolSender,
		SettingsSnapshotCache settingsCache,
		InitializeParamsBuilder initializeParamsBuilder,
		SessionStartNotificationBuilder sessionStartNotificationBuilder
	)
	{
		_state = context.State;
		_logger = context.Logger;
		_workspaceRootsDisplayText = context.WorkspaceRootsDisplayText;
		_protocolSender = protocolSender;
		_settingsCache = settingsCache;
		_initializeParamsBuilder = initializeParamsBuilder;
		_sessionStartNotificationBuilder = sessionStartNotificationBuilder;
	}

	/// <summary>
	/// Completes the post-activation handshake sequence: initialize request, capability capture, initialized notification,
	/// session-start notifications, settings push, and readiness publication.
	/// </summary>
	/// <param name="session">The activated connection session.</param>
	/// <param name="initializeCancellationToken">
	/// A token that can cancel the initialize request; it is canceled when the configured timeout elapses or startup is canceled.
	/// </param>
	/// <param name="cancellationToken">A token that can cancel the remaining handshake notifications.</param>
	/// <returns><see langword="true"/> when the session is ready after the handshake; otherwise, <see langword="false"/>.</returns>
	internal async Task<bool> RunAsync(ConnectionSession session, CancellationToken initializeCancellationToken, CancellationToken cancellationToken)
	{
		InitializeResponse? initializeResponse = await _protocolSender
			.SendRequestCoreAsync<InitializeResponse>(
				session,
				method: LspMethodNames.Initialize,
				parameters: _initializeParamsBuilder.Build(),
				disposedAccess: DisposedAccessPolicy.Enforce,
				cancellationToken: initializeCancellationToken
			)
			.ConfigureAwait(false);

		_state.CaptureServerCapabilitiesForGeneration(session.ConnectionGeneration, initializeResponse);

		await _protocolSender
			.SendNotificationCoreAsync(
				session,
				method: LspMethodNames.Initialized,
				parameters: new EmptyParams(),
				disposedAccess: DisposedAccessPolicy.Enforce,
				cancellationToken
			)
			.ConfigureAwait(false);

		IReadOnlyList<(string Method, JsonNode? Parameters)> sessionStartNotifications = _sessionStartNotificationBuilder.Build();

		for (int i = 0; i < sessionStartNotifications.Count; i++)
		{
			(string method, JsonNode? parameters) = sessionStartNotifications[i];

			await _protocolSender
				.SendNotificationCoreAsync(session, method, parameters, disposedAccess: DisposedAccessPolicy.Enforce, cancellationToken)
				.ConfigureAwait(false);
		}

		JsonElement settingsPayload = _settingsCache.RefreshFromProvider();

		await _protocolSender
			.SendNotificationCoreAsync(
				session,
				method: LspMethodNames.DidChangeConfiguration,
				parameters: new DidChangeConfigurationParams(settingsPayload),
				disposedAccess: DisposedAccessPolicy.Enforce,
				cancellationToken
			)
			.ConfigureAwait(false);

		_state.MarkCapabilityReadyForGeneration(session.ConnectionGeneration);

		if (!_state.CanAcceptRequestResultForSession(session))
		{
			_logger.LogWarning(
				"Language server connection generation {Generation} for workspace '{Workspace}' was invalidated while initialization was completing; tearing down the session and leaving the client not ready.",
				session.ConnectionGeneration,
				_workspaceRootsDisplayText
			);

			return false;
		}

		_logger.LogInformation(
			"Language server connection generation {Generation} completed initialization and is ready.",
			session.ConnectionGeneration
		);

		return true;
	}
}

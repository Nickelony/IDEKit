using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nickelony.LanguageServer.Client;

public sealed partial class LanguageServerClient
{
	/// <inheritdoc/>
	public async Task<bool> StartAsync(CancellationToken cancellationToken)
	{
		ThrowIfDisposed(allowDisposed: false);

		if (IsReady)
			return true;

		// Start the callback dispatcher with the first startup attempt instead of from the constructor, so merely
		// constructing a client does not leave a background loop running until disposal.
		_diagnosticsRouter.EnsurePumpsRunning(includeDiagnosticsPump: false);

		using var disposeAwareStartupCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeToken);
		CancellationToken effectiveCancellationToken = disposeAwareStartupCts.Token;

		bool startLockHeld = false;
		TransportSession? startedSession = null;
		bool sessionActivated = false;
		CancellationTokenSource? initializeTimeout = null;

		try
		{
			await _startLock.WaitAsync(effectiveCancellationToken).ConfigureAwait(false);
			startLockHeld = true;

			ThrowIfDisposed(allowDisposed: false);

			if (IsReady)
				return true;

			// The diagnostic surface reports only the most recent attempt; clear it under the start lock so a
			// concurrent pair cannot leave an earlier attempt's failure in place after a later attempt succeeds.
			Volatile.Write(ref _lastStartupException, null);

			TransportSession? previousSession = _capabilityStore.DetachActiveSession();

			if (previousSession is not null)
			{
				_logger.LogInformation("Restarting language server transport by replacing generation {Generation}.", previousSession.Generation);
				await _transportHost.DisposeSessionAsync(previousSession).ConfigureAwait(false);
			}

			ILanguageServerConnection connection = await _transport
				.ConnectAsync(CreateTransportContext(), effectiveCancellationToken)
				.ConfigureAwait(false)
				?? throw new InvalidOperationException("The language-server transport returned no connection for the startup attempt.");

			TransportSession session;

			try
			{
				session = _transportHost.CreateTransportSession(connection);
			}
			catch
			{
				// The session owns the connection; if it cannot be created, the opened transport has no owner and must
				// be disposed here or it leaks the process and streams the connect just opened.
				await connection.DisposeAsync().ConfigureAwait(false);
				throw;
			}

			startedSession = session;

			// Disposal can win the race after the entry checks and the startup gate wait and before the session is
			// activated. Without this re-check the session would become active after DisposeAsync returned and its
			// process would only be torn down once the canceled lifetime token aborted the handshake.
			if (_isDisposed || _lifetimeToken.IsCancellationRequested)
			{
				await _transportHost.DisposeSessionAsync(session).ConfigureAwait(false);
				return false;
			}

			_transportHost.ConfigureTransportSession(session);
			_capabilityStore.SetActiveSession(session);
			sessionActivated = true;
			_transportHost.StartTransportSession(session);

			_logger.LogInformation("Activated language server transport generation {Generation} for workspace '{Workspace}'; completing initialization handshake.",
				session.Generation,
				_workspaceRootsDisplayText);

			if (_testHooks.SessionActivated is not null)
				await _testHooks.SessionActivated(effectiveCancellationToken).ConfigureAwait(false);

			_diagnosticsRouter.EnsurePumpsRunning(includeDiagnosticsPump: true);

			var initializeTimeoutSource = CancellationTokenSource.CreateLinkedTokenSource(effectiveCancellationToken);
			initializeTimeout = initializeTimeoutSource;
			initializeTimeoutSource.CancelAfter(_initializeTimeout);

			if (_testHooks.BeforeInitializeRequest is not null)
				await _testHooks.BeforeInitializeRequest(initializeTimeoutSource.Token).ConfigureAwait(false);

			if (!await CompleteHandshakeAsync(session, initializeTimeoutSource.Token, effectiveCancellationToken).ConfigureAwait(false))
			{
				await DisposeActiveSessionAsync().ConfigureAwait(false);
				return false;
			}

			return true;
		}
		catch (OperationCanceledException exception) when (sessionActivated && initializeTimeout is { IsCancellationRequested: true } && !effectiveCancellationToken.IsCancellationRequested)
		{
			Volatile.Write(ref _lastStartupException, exception);

			_logger.LogWarning("Language server transport generation {Generation} did not complete initialization within {TimeoutMs} ms for workspace '{Workspace}'; tearing down the session and leaving the client not ready.",
				startedSession?.Generation ?? 0,
				(int)_initializeTimeout.TotalMilliseconds,
				_workspaceRootsDisplayText);

			_transportHost.LogRecentStandardErrorContext(startedSession, "Initialization timeout");

			await DisposeActiveSessionAsync().ConfigureAwait(false);
			return false;
		}
		catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
		{
			Volatile.Write(ref _lastStartupException, exception);

			if (!sessionActivated && startedSession is not null)
				await _transportHost.DisposeSessionAsync(startedSession).ConfigureAwait(false);

			await DisposeActiveSessionAsync().ConfigureAwait(false);
			throw;
		}
		catch (OperationCanceledException exception) when (_isDisposed)
		{
			Volatile.Write(ref _lastStartupException, exception);

			if (!sessionActivated && startedSession is not null)
				await _transportHost.DisposeSessionAsync(startedSession).ConfigureAwait(false);

			await DisposeActiveSessionAsync().ConfigureAwait(false);
			return false;
		}
		catch (Exception exception)
		{
			Volatile.Write(ref _lastStartupException, exception);

			_logger.LogWarning(exception,
				"Failed to start the language server (executable='{Executable}', workspace='{Workspace}', generation={Generation}, stage='{Stage}').",
				_serverExecutablePath,
				_workspaceRootsDisplayText,
				startedSession?.Generation ?? 0,
				sessionActivated ? "initialization" : "startup");

			_transportHost.LogRecentStandardErrorContext(startedSession, sessionActivated ? "Initialization failure" : "Startup failure");

			if (!sessionActivated && startedSession is not null)
				await _transportHost.DisposeSessionAsync(startedSession).ConfigureAwait(false);

			await DisposeActiveSessionAsync().ConfigureAwait(false);
			return false;
		}
		finally
		{
			initializeTimeout?.Dispose();

			if (startLockHeld)
				_startLock.Release();
		}
	}

	/// <summary>
	/// Completes the post-activation handshake sequence: initialize request, capability capture, initialized
	/// notification, settings push, and readiness publication.
	/// </summary>
	/// <param name="session">The activated transport session.</param>
	/// <param name="initializeCancellationToken">Cancels the initialize request when the configured timeout elapses.</param>
	/// <param name="cancellationToken">A token that can cancel the remaining handshake notifications.</param>
	/// <returns><see langword="true"/> when the session is ready after the handshake; otherwise, <see langword="false"/>.</returns>
	internal async Task<bool> CompleteHandshakeAsync(
		TransportSession session,
		CancellationToken initializeCancellationToken,
		CancellationToken cancellationToken)
	{
		InitializeResponse? initializeResponse = await _protocolForwarder.SendRequestCoreAsync<InitializeResponse>(session,
			LspMethodNames.Initialize, BuildInitializeParams(), allowDisposed: false, initializeCancellationToken).ConfigureAwait(false);

		_capabilityStore.CaptureServerCapabilitiesForGeneration(session.Generation, initializeResponse);

		await _protocolForwarder.SendNotificationCoreAsync(session, LspMethodNames.Initialized, new EmptyParams(), allowDisposed: false, cancellationToken).ConfigureAwait(false);

		JsonElement settingsPayload = _protocolForwarder.RefreshSettingsSnapshotFromProviderElement();

		await _protocolForwarder.SendNotificationCoreAsync(session,
			LspMethodNames.DidChangeConfiguration,
			new DidChangeConfigurationParams(settingsPayload),
			allowDisposed: false,
			cancellationToken).ConfigureAwait(false);

		_capabilityStore.SetCapabilityReadinessForGeneration(session.Generation, true);

		if (!_capabilityStore.CanAcceptRequestResultForSession(session))
		{
			_logger.LogWarning("Language server transport generation {Generation} for workspace '{Workspace}' was invalidated while initialization was completing; tearing down the session and leaving the client not ready.",
				session.Generation,
				_workspaceRootsDisplayText);

			return false;
		}

		_logger.LogInformation("Language server transport generation {Generation} completed initialization and is ready.", session.Generation);
		return true;
	}

	// This client does not service dynamic capability registration requests (the server-callbacks target logs and
	// ignores client/registerCapability and client/unregisterCapability), so it must not advertise dynamic
	// registration for any capability the host's payload declares. The initialize payload therefore forces the flag
	// off wherever it is present under workspace and textDocument rather than tracking a fixed capability list.
	private const string DynamicRegistrationPropertyName = "dynamicRegistration";

	/// <summary>
	/// Builds the initialize request payload for the configured workspace roots.
	/// </summary>
	/// <returns>The initialize request payload.</returns>
	internal object BuildInitializeParams() => new
	{
		processId = Environment.ProcessId,
		initializationOptions = BuildInitializationOptionsPayload(),
		rootUri = _workspaceFolders.Count > 0 ? _workspaceFolders[0].Uri : null,
		workspaceFolders = _workspaceFolders.Count > 0 ? _workspaceFolders : null,
		capabilities = BuildClientCapabilitiesPayload()
	};

	/// <summary>
	/// Builds the initialization-options payload, normalizing member casing to the protocol's lower-camel
	/// convention.
	/// </summary>
	/// <remarks>
	/// The initialize payload is serialized with the transport's options, which apply no naming policy, so a host
	/// DTO with PascalCase members would otherwise ship mixed casing next to the normalized client capabilities.
	/// </remarks>
	/// <returns>The normalized initialization options, or <see langword="null"/> when the host supplied none.</returns>
	private JsonNode? BuildInitializationOptionsPayload()
	{
		object? initializationOptions = _initializationOptionsProvider(_workspaceRootDirectoryPaths);

		return initializationOptions is null
			? null
			: JsonSerializer.SerializeToNode(initializationOptions, ProtocolSerializerOptions);
	}

	/// <summary>
	/// Builds the client-capabilities payload, normalizing member casing to the protocol's lower-camel convention,
	/// forcing dynamic registration off, disabling work-done progress, and pinning the UTF-16 position encoding.
	/// </summary>
	/// <returns>The client-capabilities payload to send during initialization.</returns>
	private JsonObject BuildClientCapabilitiesPayload()
	{
		object? clientCapabilities = _clientCapabilitiesProvider(_workspaceRootDirectoryPaths);
		JsonObject capabilitiesObject;

		if (clientCapabilities is null)
		{
			capabilitiesObject = new JsonObject();
		}
		else if (JsonSerializer.SerializeToNode(clientCapabilities, ProtocolSerializerOptions) is JsonObject serializedObject)
		{
			capabilitiesObject = serializedObject;
		}
		else
		{
			_logger.LogWarning(
				"The client-capabilities payload does not serialize to a JSON object, so dynamic registration, work-done progress, and the UTF-16 position encoding cannot be enforced for it; sending empty client capabilities to keep the initialize payload protocol-valid.");

			return new JsonObject();
		}

		EnforceDynamicRegistrationFalse(capabilitiesObject, "workspace");
		EnforceDynamicRegistrationFalse(capabilitiesObject, "textDocument");
		EnforceWorkDoneProgressFalse(capabilitiesObject);
		EnforceApplyEditFalse(capabilitiesObject);
		EnforceUtf16PositionEncoding(capabilitiesObject);
		EnforceSemanticTokenSupportFalse(capabilitiesObject);

		return capabilitiesObject;
	}

	/// <summary>
	/// Forces <c>textDocument.semanticTokens.multilineTokenSupport</c> and <c>overlappingTokenSupport</c> to
	/// <see langword="false"/> when the host payload advertises them.
	/// </summary>
	/// <remarks>
	/// The semantic-token decoder implements the unsupported-multi-line-token rule (a token is clamped to its line
	/// end), and the decoded model cannot represent overlapping tokens.
	/// </remarks>
	/// <param name="rootObject">The root capabilities object.</param>
	private static void EnforceSemanticTokenSupportFalse(JsonObject rootObject)
	{
		if (!TryGetJsonObjectProperty(rootObject, "textDocument", out JsonObject? textDocumentObject)
			|| !TryGetJsonObjectProperty(textDocumentObject, "semanticTokens", out JsonObject? semanticTokensObject))
		{
			return;
		}

		if (semanticTokensObject.ContainsKey("multilineTokenSupport"))
			semanticTokensObject["multilineTokenSupport"] = false;

		if (semanticTokensObject.ContainsKey("overlappingTokenSupport"))
			semanticTokensObject["overlappingTokenSupport"] = false;
	}

	/// <summary>
	/// Forces <c>window.workDoneProgress = false</c> when the host payload advertises it, because this client
	/// acknowledges progress creation without exposing a client-side progress sink.
	/// </summary>
	/// <param name="rootObject">The root capabilities object.</param>
	private static void EnforceWorkDoneProgressFalse(JsonObject rootObject)
	{
		if (TryGetJsonObjectProperty(rootObject, "window", out JsonObject? windowObject)
			&& windowObject.ContainsKey("workDoneProgress"))
		{
			windowObject["workDoneProgress"] = false;
		}
	}

	/// <summary>
	/// Forces <c>workspace.applyEdit = false</c> when the host payload advertises it, because this client does
	/// not service <c>workspace/applyEdit</c>; an advertised capability would make a server send edits that can
	/// only be answered with a JSON-RPC method-not-found error.
	/// </summary>
	/// <param name="rootObject">The root capabilities object.</param>
	private static void EnforceApplyEditFalse(JsonObject rootObject)
	{
		if (TryGetJsonObjectProperty(rootObject, "workspace", out JsonObject? workspaceObject)
			&& workspaceObject.ContainsKey("applyEdit"))
		{
			workspaceObject["applyEdit"] = false;
		}
	}

	/// <summary>
	/// Pins <c>general.positionEncodings</c> to UTF-16, the only encoding the coordinate math implements, so a
	/// host payload cannot negotiate an encoding that would corrupt every position and range.
	/// </summary>
	/// <param name="rootObject">The root capabilities object.</param>
	private static void EnforceUtf16PositionEncoding(JsonObject rootObject)
	{
		JsonObject generalObject;

		if (rootObject.TryGetPropertyValue("general", out JsonNode? generalNode) && generalNode is JsonObject existingGeneralObject)
			generalObject = existingGeneralObject;
		else
			generalObject = new JsonObject();

		generalObject["positionEncodings"] = new JsonArray("utf-16");
		rootObject["general"] = generalObject;
	}

	/// <summary>
	/// Forces <c>dynamicRegistration = false</c> on every capability object of one capability parent.
	/// </summary>
	/// <param name="rootObject">The root capabilities object.</param>
	/// <param name="parentPropertyName">The parent property whose capability objects should be rewritten.</param>
	private static void EnforceDynamicRegistrationFalse(JsonObject rootObject, string parentPropertyName)
	{
		if (!TryGetJsonObjectProperty(rootObject, parentPropertyName, out JsonObject? parentObject))
			return;

		RewriteDynamicRegistrationFalse(parentObject);
	}

	/// <summary>
	/// Forces <c>dynamicRegistration = false</c> on one capability object and every nested object that declares
	/// the member.
	/// </summary>
	/// <param name="capabilityObject">The capability object to rewrite.</param>
	private static void RewriteDynamicRegistrationFalse(JsonObject capabilityObject)
	{
		foreach (KeyValuePair<string, JsonNode?> capabilityEntry in capabilityObject)
		{
			if (capabilityEntry.Value is not JsonObject nestedObject)
				continue;

			// Only existing members are rewritten: capability objects that never declared dynamicRegistration
			// must not gain a member, and nested capability objects such as workspace.fileOperations.didCreate
			// carry their own member that a shallow rewrite would miss.
			if (nestedObject.ContainsKey(DynamicRegistrationPropertyName))
				nestedObject[DynamicRegistrationPropertyName] = false;

			RewriteDynamicRegistrationFalse(nestedObject);
		}
	}

	/// <summary>
	/// Tries to read one JSON object property while rejecting non-object values.
	/// </summary>
	/// <param name="rootObject">The containing JSON object.</param>
	/// <param name="propertyName">The property name to read.</param>
	/// <param name="propertyObject">Receives the nested object value when present.</param>
	/// <returns><see langword="true"/> when the property exists and is a JSON object.</returns>
	private static bool TryGetJsonObjectProperty(JsonObject rootObject, string propertyName, [NotNullWhen(true)] out JsonObject? propertyObject)
	{
		propertyObject = null;

		if (!rootObject.TryGetPropertyValue(propertyName, out JsonNode? propertyNode))
			return false;

		propertyObject = propertyNode as JsonObject;
		return propertyObject is not null;
	}

	/// <summary>
	/// Gets the default working directory for the server process: the executable's directory, or the current
	/// directory when the executable path carries no directory part.
	/// </summary>
	/// <returns>The default working directory.</returns>
	private string GetDefaultServerWorkingDirectory()
	{
		string? executableDirectory = Path.GetDirectoryName(_serverExecutablePath);

		return string.IsNullOrEmpty(executableDirectory) ? Environment.CurrentDirectory : executableDirectory;
	}

	/// <summary>
	/// Builds the description of the language server that the configured transport is asked to connect to.
	/// </summary>
	/// <returns>The transport context for one startup attempt.</returns>
	private LanguageServerTransportContext CreateTransportContext() => new()
	{
		ServerExecutablePath = _serverExecutablePath,
		ServerArguments = _serverArguments,
		ServerWorkingDirectory = _serverWorkingDirectory ?? GetDefaultServerWorkingDirectory(),
		EnvironmentVariables = _environmentVariables,
		WorkspaceRootDirectoryPaths = _workspaceRootDirectoryPaths,
		ChildProcessLifetimeGuard = _childProcessLifetimeGuardFactory?.Invoke(),
		Logger = _logger
	};

	/// <summary>
	/// Derives the workspace-folder display name from the normalized workspace root path.
	/// </summary>
	/// <param name="workspaceRootDirectoryPath">The normalized workspace root path.</param>
	/// <returns>The folder name to advertise to the language server.</returns>
	private static string GetWorkspaceFolderName(string workspaceRootDirectoryPath)
	{
		string trimmedWorkspaceRootDirectoryPath = workspaceRootDirectoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

		if (trimmedWorkspaceRootDirectoryPath.Length == 0)
			trimmedWorkspaceRootDirectoryPath = workspaceRootDirectoryPath;

		string folderName = Path.GetFileName(trimmedWorkspaceRootDirectoryPath);

		if (!string.IsNullOrEmpty(folderName))
			return folderName;

		// A filesystem root (for example "C:\" or "/") has no file name; report the root itself rather than its
		// drive or separator, so the advertised name is not the misleading "C:".
		string rootPath = Path.GetPathRoot(workspaceRootDirectoryPath) ?? string.Empty;

		if (!string.IsNullOrEmpty(rootPath))
			return rootPath;

		return workspaceRootDirectoryPath;
	}
}
